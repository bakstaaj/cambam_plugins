using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml;
using CamBam;
using CamBam.CAD;
using CamBam.CAM;
using CamBam.UI;
using CamBam.Values;
using ThreadingMOP;

namespace Tapping
{
    public sealed class TappingPlugin
    {
        private static CamBamUI _ui;

        public static void InitPlugin(CamBamUI ui)
        {
            _ui = ui;
            AppDomain.CurrentDomain.AssemblyResolve += ResolvePluginAssembly;
            ToolStripMenuItem item = new ToolStripMenuItem("Tapping...");
            item.Click += OnClick;
            ui.Menus.mnuPlugins.DropDownItems.Add(item);
        }

        private static Assembly ResolvePluginAssembly(object sender, ResolveEventArgs args)
        {
            AssemblyName requested = new AssemblyName(args.Name);
            if (!String.Equals(requested.Name, "ThreadingMOP", StringComparison.OrdinalIgnoreCase))
                return null;

            string pluginPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "ThreadingMOP.dll");
            if (!File.Exists(pluginPath))
                return null;

            return Assembly.LoadFrom(pluginPath);
        }

        private static void OnClick(object sender, EventArgs e)
        {
            try
            {
                if (_ui == null || _ui.ActiveView == null || _ui.ActiveView.CADFile == null)
                {
                    MessageBox.Show("Open a CamBam drawing and select one or more bore circles first.", "Tapping");
                    return;
                }

                CADFile file = _ui.ActiveView.CADFile;
                if (file.DrawingUnits != Units.Millimeters && file.DrawingUnits != Units.Inches)
                {
                    MessageBox.Show("Tapping currently supports drawings in millimeters or inches.", "Tapping");
                    return;
                }
                List<Circle> circles = _ui.ActiveView.SelectedEntities.OfType<Circle>().ToList();
                if (circles.Count == 0)
                {
                    MessageBox.Show("Select only one or more bore circles. The circle diameter must be the clearance-hole drill size.", "Tapping");
                    return;
                }
                double tolerance = file.DrawingUnits == Units.Inches ? 0.002 : 0.05;
                List<ThreadMillSpec> mills = LoadThreadMillCatalog();
                List<List<Circle>> groups = new List<List<Circle>>();
                foreach (Circle circle in circles)
                {
                    List<Circle> group = groups.FirstOrDefault(g => Math.Abs(g[0].Diameter - circle.Diameter) <= tolerance);
                    if (group == null)
                    {
                        group = new List<Circle>();
                        groups.Add(group);
                    }
                    group.Add(circle);
                }

                List<OperationPlan> plans = new List<OperationPlan>();
                for (int i = 0; i < groups.Count; i++)
                {
                    List<Circle> group = groups[i];
                    double groupDiameter = group[0].Diameter;
                    using (ThreadPickerDialog dialog = new ThreadPickerDialog(groupDiameter, file.DrawingUnits, tolerance, mills, i + 1, groups.Count))
                    {
                        if (dialog.ShowDialog(ThisApplication.TopWindow) != DialogResult.OK || dialog.SelectedThread == null)
                            return;
                        plans.Add(new OperationPlan
                        {
                            Circles = group.Cast<Entity>().ToList(),
                            BoreDiameter = groupDiameter,
                            Thread = dialog.SelectedThread,
                            Mill = dialog.SelectedMill,
                            TargetDepth = dialog.TargetDepth
                        });
                    }
                }

                foreach (OperationPlan plan in plans)
                    EnsureToolInstalled(plan.Mill, file);

                foreach (OperationPlan plan in plans)
                {
                    plan.DrillMill = FindLargestEndMill(file, plan.BoreDiameter);
                    if (plan.DrillMill == null)
                        throw new InvalidOperationException("No regular end mill smaller than the " + Format(plan.BoreDiameter) + " " + UnitLabel(file.DrawingUnits) + " bore was found in the active default tool library. Add a smaller EndMill tool and run Tapping again.");
                }

                foreach (OperationPlan plan in plans)
                    CreateOperationPair(file, plan);

                file.Modified = true;
                MessageBox.Show(
                    "Created " + plans.Count + " clearance drilling MOP(s) and " + plans.Count + " Thread Mill MOP(s) for " + groups.Count + " bore size group(s).\r\n\r\nReview the selected tools, feeds, and clearance planes before generating G-code.",
                    "Tapping", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not create the drilling and thread milling operations.\r\n\r\n" + ex.Message, "Tapping", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void CreateOperationPair(CADFile file, OperationPlan plan)
        {
            List<Entity> geometry = plan.Circles;
            double boreDiameter = plan.BoreDiameter;
            ThreadSpec spec = plan.Thread;
            MOPDrill drill = new MOPDrill(file, geometry);
            drill.Name = "Clearance Drill " + Format(boreDiameter) + " " + UnitLabel(file.DrawingUnits);
            drill.HoleDiameter = new CBValue<double>(boreDiameter);
            drill.TargetDepth = new CBValue<double>(plan.TargetDepth);
            drill.DrillingMethod = new CBValue<DrillingMethods>(DrillingMethods.SpiralMill_CW);
            drill.SpiralFlatBase = new CBValue<bool>(true);
            drill.ToolNumber = new CBValue<int>(plan.DrillMill.ToolNumber);
            drill.ToolDiameter = new CBValue<double>(plan.DrillMill.Diameter);
            drill.ToolProfile = new CBValue<ToolProfiles>(ToolProfiles.EndMill);

            MOPThreading thread = new MOPThreading(file, geometry);
            thread.Name = "Thread Mill " + spec.Name;
            thread.TargetDepth = new CBValue<double>(plan.TargetDepth);
            thread.DepthIncrement = new CBValue<double>(ConvertFromMillimeters(spec.PitchMm, file.DrawingUnits));
            thread.HoleDiameter = new CBValue<double>(ConvertFromMillimeters(spec.MajorDiameterMm, file.DrawingUnits));
            thread.InsideOutside = new CBValue<InsideOutsideOptions>(InsideOutsideOptions.Inside);
            thread.ThreadHandedness = new CBValue<ThreadHandednessOptions>(ThreadHandednessOptions.RightHand);
            thread.ToolFluteLength = new CBValue<double>(0.0);
            thread.ToolNumber = new CBValue<int>(FindToolNumber(plan.Mill, file));
            thread.ToolDiameter = new CBValue<double>(ConvertFromMillimeters(plan.Mill.DiameterMm, file.DrawingUnits));
            thread.ToolProfile = new CBValue<ToolProfiles>(ToolProfiles.EndMill);

            CamBamUI.MainUI.InsertMOP(drill);
            CamBamUI.MainUI.InsertMOP(thread);
            file.Modified = true;
        }

        private sealed class OperationPlan
        {
            public List<Entity> Circles;
            public double BoreDiameter;
            public ThreadSpec Thread;
            public ThreadMillSpec Mill;
            public LibraryToolSelection DrillMill;
            public double TargetDepth;
        }

        private sealed class LibraryToolSelection
        {
            public int ToolNumber;
            public string Name;
            public double Diameter;
        }

        private sealed class ThreadMillSpec
        {
            public string PartCode;
            public string Name;
            public double DiameterMm;
            public double ShankMm;
            public double FluteLengthMm;
            public double LengthMm;
            public int Flutes;
            public double PitchMinMm;
            public double PitchMaxMm;
            public string PitchLabel;
        }

        private static List<ThreadMillSpec> LoadThreadMillCatalog()
        {
            string catalogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "ThreadMillCatalog.xml");
            if (!File.Exists(catalogPath))
                throw new FileNotFoundException("The thread mill catalog is missing. Run Install-Tapping.cmd again to install the updated plugin and tools.", catalogPath);

            XmlDocument document = new XmlDocument();
            document.Load(catalogPath);
            List<ThreadMillSpec> result = new List<ThreadMillSpec>();
            foreach (XmlNode node in document.SelectNodes("/ThreadMillCatalog/Tool"))
            {
                result.Add(new ThreadMillSpec
                {
                    PartCode = node.Attributes["PartCode"].Value,
                    Name = node.Attributes["Name"].Value,
                    DiameterMm = Double.Parse(node.Attributes["DiameterMm"].Value, CultureInfo.InvariantCulture),
                    ShankMm = Double.Parse(node.Attributes["ShankMm"].Value, CultureInfo.InvariantCulture),
                    FluteLengthMm = Double.Parse(node.Attributes["FluteLengthMm"].Value, CultureInfo.InvariantCulture),
                    LengthMm = Double.Parse(node.Attributes["LengthMm"].Value, CultureInfo.InvariantCulture),
                    Flutes = Int32.Parse(node.Attributes["Flutes"].Value, CultureInfo.InvariantCulture),
                    PitchMinMm = Double.Parse(node.Attributes["PitchMinMm"].Value, CultureInfo.InvariantCulture),
                    PitchMaxMm = Double.Parse(node.Attributes["PitchMaxMm"].Value, CultureInfo.InvariantCulture),
                    PitchLabel = node.Attributes["PitchLabel"].Value
                });
            }
            if (result.Count == 0) throw new InvalidDataException("The thread mill catalog contains no cutters.");
            return result;
        }

        private static ThreadMillSpec FindThreadMill(List<ThreadMillSpec> mills, double boreDiameterMm, double pitchMm)
        {
            const double epsilon = 0.000001;
            return mills
                .Where(m => m.DiameterMm < boreDiameterMm - epsilon && pitchMm >= m.PitchMinMm - epsilon && pitchMm <= m.PitchMaxMm + epsilon)
                .OrderByDescending(m => m.DiameterMm)
                .FirstOrDefault();
        }

        private static List<string> GetToolLibraryPaths(CADFile file)
        {
            Units units = file.DrawingUnits;
            string fileName = units == Units.Inches ? "Default-in.xml" : "Default-mm.xml";
            List<string> paths = new List<string>();
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            string partLibrary = file.ActivePart == null ? null : file.ActivePart.ToolLibrary;
            if (!String.IsNullOrWhiteSpace(partLibrary))
            {
                string namedLibrary = partLibrary.Trim();
                if (String.IsNullOrEmpty(Path.GetExtension(namedLibrary))) namedLibrary += ".xml";
                if (Path.IsPathRooted(namedLibrary))
                    paths.Add(namedLibrary);
                else
                {
                    paths.Add(Path.Combine(programData, "CamBam plus 1.0", "tools", namedLibrary));
                    paths.Add(Path.Combine(programFiles, "CamBam plus 1.0", "tools", namedLibrary));
                }
            }
            paths.Add(Path.Combine(programData, "CamBam plus 1.0", "tools", fileName));
            paths.Add(Path.Combine(programFiles, "CamBam plus 1.0", "tools", fileName));
            paths.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", fileName));
            string active = paths.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(File.Exists);
            return active == null ? new List<string>() : new List<string> { active };
        }

        private static int FindToolNumber(ThreadMillSpec mill, CADFile file)
        {
            string[] paths = GetToolLibraryPaths(file).Where(File.Exists).ToArray();
            foreach (string path in paths)
            {
                XmlDocument document = new XmlDocument();
                document.Load(path);
                foreach (XmlNode definition in document.SelectNodes("/ToolLibrary/ToolDefinition"))
                {
                    XmlNode partCode = definition.SelectSingleNode("PartCode");
                    XmlNode index = definition.SelectSingleNode("Index");
                    if (partCode != null && index != null && String.Equals(partCode.InnerText, mill.PartCode, StringComparison.OrdinalIgnoreCase))
                        return Int32.Parse(index.InnerText, CultureInfo.InvariantCulture);
                }
            }
            return 0;
        }

        private static LibraryToolSelection FindLargestEndMill(CADFile file, double boreDiameter)
        {
            Units units = file.DrawingUnits;
            string[] paths = GetToolLibraryPaths(file).Where(File.Exists).ToArray();
            if (paths.Length == 0) return null;

            XmlDocument document = new XmlDocument();
            document.Load(paths[0]);
            List<LibraryToolSelection> candidates = new List<LibraryToolSelection>();
            foreach (XmlNode definition in document.SelectNodes("/ToolLibrary/ToolDefinition"))
            {
                XmlNode index = definition.SelectSingleNode("Index");
                XmlNode diameter = definition.SelectSingleNode("Diameter");
                XmlNode profile = definition.SelectSingleNode("ToolProfile");
                if (index == null || diameter == null || profile == null || !String.Equals(profile.InnerText, "EndMill", StringComparison.OrdinalIgnoreCase))
                    continue;

                XmlNode notes = definition.SelectSingleNode("Notes");
                XmlNode comment = definition.SelectSingleNode("Comment");
                XmlNode partCode = definition.SelectSingleNode("PartCode");
                string description = ((notes == null ? "" : notes.InnerText) + " " + (comment == null ? "" : comment.InnerText) + " " + (partCode == null ? "" : partCode.InnerText)).ToLowerInvariant();
                if (description.Contains("thread mill") || (partCode != null && partCode.InnerText.StartsWith("CUT000", StringComparison.OrdinalIgnoreCase)))
                    continue;

                double toolDiameter;
                int toolNumber;
                if (!Double.TryParse(diameter.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, out toolDiameter) ||
                    !Int32.TryParse(index.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out toolNumber))
                    continue;
                if (toolDiameter > 0 && toolDiameter < boreDiameter - 0.000001)
                {
                    candidates.Add(new LibraryToolSelection
                    {
                        ToolNumber = toolNumber,
                        Name = definition.Attributes["Name"] == null ? (toolDiameter.ToString("0.####", CultureInfo.InvariantCulture) + " " + UnitLabel(units) + " EndMill") : definition.Attributes["Name"].Value,
                        Diameter = toolDiameter
                    });
                }
            }
            return candidates.OrderByDescending(tool => tool.Diameter).FirstOrDefault();
        }

        private static void EnsureToolInstalled(ThreadMillSpec mill, CADFile file)
        {
            if (FindToolNumber(mill, file) == 0)
            {
                string library = file.ActivePart == null || String.IsNullOrWhiteSpace(file.ActivePart.ToolLibrary) ? (file.DrawingUnits == Units.Inches ? "Default-in" : "Default-mm") : file.ActivePart.ToolLibrary;
                throw new InvalidOperationException("Thread mill " + mill.Name + " is not present in the active tool library (" + library + "). Re-run Install-Tapping.cmd as administrator for the default libraries, or add the cutter to the selected library.");
            }
        }

        private static double ConvertFromMillimeters(double value, Units units)
        {
            return units == Units.Inches ? value / 25.4 : value;
        }

        private static string UnitLabel(Units units) { return units == Units.Inches ? "in" : "mm"; }
        private static string Format(double value) { return value.ToString("0.####", CultureInfo.CurrentCulture); }

        private sealed class ThreadSpec
        {
            public string Name;
            public double MajorDiameterMm;
            public double PitchMm;
            public double TapDrillMm;
            public Units DisplayUnits;
            public bool IsCustom;

            public override string ToString()
            {
                double drill = DisplayUnits == Units.Inches ? TapDrillMm / 25.4 : TapDrillMm;
                return Name + "  |  tap drill " + drill.ToString(DisplayUnits == Units.Inches ? "0.####" : "0.###", CultureInfo.InvariantCulture) + " " + UnitLabel(DisplayUnits);
            }
        }

        private sealed class ThreadPickerDialog : Form
        {
            private readonly double _boreDiameter;
            private readonly Units _units;
            private readonly double _tolerance;
            private readonly List<ThreadMillSpec> _mills;
            private readonly ComboBox _threads;
            private readonly Label _status;
            private readonly NumericUpDown _depth;
            private ThreadSpec _selected;

            public ThreadSpec SelectedThread { get { return _selected; } }
            public ThreadMillSpec SelectedMill { get; private set; }
            public double TargetDepth { get { return -Math.Abs((double)_depth.Value); } }

            private static readonly ThreadSpec[] MetricThreads = new ThreadSpec[]
            {
                Spec("M1 × 0.25", 1.0, 0.25, 0.75),
                Spec("M1.2 × 0.25", 1.2, 0.25, 0.95),
                Spec("M1.4 × 0.3", 1.4, 0.3, 1.1),
                Spec("M1.6 × 0.35", 1.6, 0.35, 1.25),
                Spec("M1.8 × 0.35", 1.8, 0.35, 1.45),
                Spec("M2 × 0.4", 2.0, 0.4, 1.6),
                Spec("M2.2 × 0.45", 2.2, 0.45, 1.75),
                Spec("M2.5 × 0.45", 2.5, 0.45, 2.05),
                Spec("M3 × 0.5", 3.0, 0.5, 2.5),
                Spec("M3 × 0.35", 3.0, 0.35, 2.65),
                Spec("M3.5 × 0.6", 3.5, 0.6, 2.9),
                Spec("M4 × 0.7", 4.0, 0.7, 3.3),
                Spec("M4 × 0.5", 4.0, 0.5, 3.5),
                Spec("M4.5 × 0.75", 4.5, 0.75, 3.7),
                Spec("M5 × 0.8", 5.0, 0.8, 4.2),
                Spec("M5 × 0.5", 5.0, 0.5, 4.5),
                Spec("M5.5 × 0.9", 5.5, 0.9, 4.6),
                Spec("M6 × 1.0", 6.0, 1.0, 5.0),
                Spec("M6 × 0.75", 6.0, 0.75, 5.2),
                Spec("M7 × 1.0", 7.0, 1.0, 6.0),
                Spec("M7 × 0.75", 7.0, 0.75, 6.25),
                Spec("M8 × 1.25", 8.0, 1.25, 6.8),
                Spec("M8 × 1.0", 8.0, 1.0, 7.0),
                Spec("M8 × 0.75", 8.0, 0.75, 7.2),
                Spec("M9 × 1.25", 9.0, 1.25, 7.8),
                Spec("M10 × 1.5", 10.0, 1.5, 8.5),
                Spec("M10 × 1.25", 10.0, 1.25, 8.8),
                Spec("M10 × 1.0", 10.0, 1.0, 9.0),
                Spec("M11 × 1.5", 11.0, 1.5, 9.5),
                Spec("M12 × 1.75", 12.0, 1.75, 10.2),
                Spec("M12 × 1.5", 12.0, 1.5, 10.5),
                Spec("M12 × 1.25", 12.0, 1.25, 10.8),
                Spec("M14 × 2.0", 14.0, 2.0, 12.0),
                Spec("M14 × 1.5", 14.0, 1.5, 12.5),
                Spec("M16 × 2.0", 16.0, 2.0, 14.0),
                Spec("M16 × 1.5", 16.0, 1.5, 14.5),
                Spec("M18 × 2.5", 18.0, 2.5, 15.5),
                Spec("M18 × 2.0", 18.0, 2.0, 16.0),
                Spec("M18 × 1.5", 18.0, 1.5, 16.5),
                Spec("M20 × 2.5", 20.0, 2.5, 17.5),
                Spec("M20 × 2.0", 20.0, 2.0, 18.0),
                Spec("M20 × 1.5", 20.0, 1.5, 18.5),
                Spec("M22 × 2.5", 22.0, 2.5, 19.5),
                Spec("M22 × 2.0", 22.0, 2.0, 20.0),
                Spec("M22 × 1.5", 22.0, 1.5, 20.5),
                Spec("M24 × 3.0", 24.0, 3.0, 21.0),
                Spec("M24 × 2.0", 24.0, 2.0, 22.0),
                Spec("M24 × 1.5", 24.0, 1.5, 22.5),
                Spec("M27 × 3.0", 27.0, 3.0, 24.0),
                Spec("M27 × 2.0", 27.0, 2.0, 25.0),
                Spec("M27 × 1.5", 27.0, 1.5, 25.5),
                Spec("M30 × 3.5", 30.0, 3.5, 26.5),
                Spec("M30 × 2.0", 30.0, 2.0, 28.0),
                Spec("M30 × 1.5", 30.0, 1.5, 28.5),
                Spec("M33 × 3.5", 33.0, 3.5, 29.5),
                Spec("M33 × 2.0", 33.0, 2.0, 31.0),
                Spec("M36 × 4.0", 36.0, 4.0, 32.0),
                Spec("M36 × 3.0", 36.0, 3.0, 33.0),
                Spec("M39 × 4.0", 39.0, 4.0, 35.0),
                Spec("M42 × 4.5", 42.0, 4.5, 37.5),
                Spec("M45 × 4.5", 45.0, 4.5, 40.5),
                Spec("M48 × 5.0", 48.0, 5.0, 43.0),
                Spec("M52 × 5.0", 52.0, 5.0, 47.0),
                Spec("M56 × 5.5", 56.0, 5.5, 50.5),
                Spec("M60 × 5.5", 60.0, 5.5, 54.5),
                Spec("M64 × 6.0", 64.0, 6.0, 58.0)
            };

            private static readonly ThreadSpec[] UnifiedCoarseThreads = new ThreadSpec[]
            {
                SpecInches("#1-64 UNC", 0.0730, 64, 0.0595),
                SpecInches("#2-56 UNC", 0.0860, 56, 0.0670),
                SpecInches("#3-48 UNC", 0.0990, 48, 0.0781),
                SpecInches("#4-40 UNC", 0.1120, 40, 0.0890),
                SpecInches("#5-40 UNC", 0.1250, 40, 0.0995),
                SpecInches("#6-32 UNC", 0.1380, 32, 0.1065),
                SpecInches("#8-32 UNC", 0.1640, 32, 0.1360),
                SpecInches("#10-24 UNC", 0.1900, 24, 0.1495),
                SpecInches("#12-24 UNC", 0.2160, 24, 0.1730),
                SpecInches("1/4-20 UNC", 0.2500, 20, 0.2010),
                SpecInches("5/16-18 UNC", 0.3125, 18, 0.2570),
                SpecInches("3/8-16 UNC", 0.3750, 16, 0.3125),
                SpecInches("7/16-14 UNC", 0.4375, 14, 0.3680),
                SpecInches("1/2-13 UNC", 0.5000, 13, 0.4219),
                SpecInches("9/16-12 UNC", 0.5625, 12, 0.4844),
                SpecInches("5/8-11 UNC", 0.6250, 11, 0.5313),
                SpecInches("3/4-10 UNC", 0.7500, 10, 0.6563),
                SpecInches("7/8-9 UNC", 0.8750, 9, 0.7656),
                SpecInches("1-8 UNC", 1.0000, 8, 0.8750),
                SpecInches("1-1/8-7 UNC", 1.1250, 7, 0.9844),
                SpecInches("1-1/4-7 UNC", 1.2500, 7, 1.1094),
                SpecInches("1-3/8-6 UNC", 1.3750, 6, 1.2188),
                SpecInches("1-1/2-6 UNC", 1.5000, 6, 1.3438),
                SpecInches("1-3/4-5 UNC", 1.7500, 5, 1.5469),
                SpecInches("2-4.5 UNC", 2.0000, 4.5, 1.7813)
            };

            private static ThreadSpec Spec(string name, double major, double pitch, double drill)
            {
                return new ThreadSpec { Name = name, MajorDiameterMm = major, PitchMm = pitch, TapDrillMm = drill };
            }

            private static ThreadSpec SpecInches(string name, double major, double threadsPerInch, double drill)
            {
                return new ThreadSpec { Name = name, MajorDiameterMm = major * 25.4, PitchMm = 25.4 / threadsPerInch, TapDrillMm = drill * 25.4 };
            }

            public ThreadPickerDialog(double boreDiameter, Units units, double tolerance, List<ThreadMillSpec> mills, int groupNumber, int groupCount)
            {
                _boreDiameter = boreDiameter;
                _units = units;
                _tolerance = tolerance;
                _mills = mills;
                Text = "Tapping - Bore Group " + groupNumber + " of " + groupCount;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                MinimizeBox = false;
                MaximizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new Size(500, 220);

                Label bore = new Label { Left = 16, Top = 16, Width = 468, Height = 24, Text = "Selected clearance-hole diameter: " + Format(boreDiameter) + " " + UnitLabel(units) };
                Label label = new Label { Left = 16, Top = 52, Width = 120, Height = 24, Text = units == Units.Inches ? "Internal UNC thread:" : "Internal metric thread:" };
                _threads = new ComboBox { Left = 140, Top = 48, Width = 340, DropDownStyle = ComboBoxStyle.DropDownList };
                Label depthLabel = new Label { Left = 16, Top = 88, Width = 178, Height = 24, Text = "Target depth (± accepted):" };
                _depth = new NumericUpDown { Left = 198, Top = 84, Width = 140, DecimalPlaces = units == Units.Inches ? 4 : 3, Minimum = -999M, Maximum = 999M, Increment = units == Units.Inches ? 0.01M : 0.1M, Value = 0M };
                Label depthUnits = new Label { Left = 346, Top = 88, Width = 80, Height = 24, Text = UnitLabel(units) + "; stored negative" };
                _status = new Label { Left = 16, Top = 118, Width = 468, Height = 34 };
                Button custom = new Button { Left = 16, Top = 170, Width = 132, Text = "Custom thread..." };
                Button cancel = new Button { Left = 320, Top = 170, Width = 80, Text = "Cancel", DialogResult = DialogResult.Cancel };
                Button create = new Button { Left = 404, Top = 170, Width = 80, Text = "Create" };
                custom.Click += OnCustom;
                create.Click += OnCreate;
                _threads.SelectedIndexChanged += OnSelected;
                Controls.AddRange(new Control[] { bore, label, _threads, depthLabel, _depth, depthUnits, _status, custom, cancel, create });
                AcceptButton = create;
                CancelButton = cancel;
                RefreshCandidates();
            }

            private void RefreshCandidates()
            {
                double boreMm = _units == Units.Inches ? _boreDiameter * 25.4 : _boreDiameter;
                double toleranceMm = _units == Units.Inches ? _tolerance * 25.4 : _tolerance;
                ThreadSpec[] catalog = _units == Units.Inches ? UnifiedCoarseThreads : MetricThreads;
                List<ThreadSpec> matches = catalog.Where(x => Math.Abs(x.TapDrillMm - boreMm) <= toleranceMm).ToList();
                _threads.Items.Clear();
                foreach (ThreadSpec item in matches)
                {
                    item.DisplayUnits = _units;
                    _threads.Items.Add(item);
                }
                if (_threads.Items.Count > 0)
                {
                    _threads.SelectedIndex = 0;
                    if (_selected == null)
                        _status.Text = "Matching " + (_units == Units.Inches ? "UNC" : "metric") + " threads are filtered by the selected bore size.";
                }
                else
                {
                    _selected = null;
                    _status.Text = "No listed " + (_units == Units.Inches ? "UNC" : "metric") + " thread matches this bore size. Use Custom thread...";
                }
            }

            private void OnSelected(object sender, EventArgs e)
            {
                _selected = _threads.SelectedItem as ThreadSpec;
                UpdateMillStatus();
            }

            private double BoreDiameterMm { get { return _units == Units.Inches ? _boreDiameter * 25.4 : _boreDiameter; } }

            private void UpdateMillStatus()
            {
                SelectedMill = _selected == null ? null : FindThreadMill(_mills, BoreDiameterMm, _selected.PitchMm);
                if (_selected == null) return;
                if (SelectedMill == null)
                    _status.Text = "No listed cutter fits this bore and thread pitch. Choose another thread or enter a compatible custom thread.";
                else
                    _status.Text = "Auto-selected thread mill: " + SelectedMill.Name + " (" + SelectedMill.DiameterMm.ToString("0.##", CultureInfo.InvariantCulture) + " mm cutter; pitch " + SelectedMill.PitchLabel + ").";
            }

            private void OnCustom(object sender, EventArgs e)
            {
                using (CustomThreadDialog custom = new CustomThreadDialog(_boreDiameter, _units))
                {
                    if (custom.ShowDialog(this) == DialogResult.OK)
                    {
                        _selected = custom.Thread;
                        UpdateMillStatus();
                    }
                }
            }

            private void OnCreate(object sender, EventArgs e)
            {
                if (_selected == null)
                {
                    MessageBox.Show(this, "Choose a matching thread or define a custom thread.", "Tapping", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (SelectedMill == null)
                {
                    MessageBox.Show(this, "No supplied thread mill can fit inside this bore and cut the selected pitch.", "Tapping", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (_depth.Value == 0M)
                {
                    MessageBox.Show(this, "Enter a non-zero target depth. Positive or negative input is accepted; the MOP depth will be stored as negative.", "Tapping", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private sealed class CustomThreadDialog : Form
        {
            public ThreadSpec Thread { get; private set; }
            private readonly Units _units;
            private readonly double _boreDiameter;
            private readonly NumericUpDown _major;
            private readonly NumericUpDown _pitch;

            public CustomThreadDialog(double boreDiameter, Units units)
            {
                _units = units;
                _boreDiameter = boreDiameter;
                Text = "Custom Internal Thread";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                MinimizeBox = false;
                MaximizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new Size(390, 180);
                string unit = units == Units.Inches ? "in" : "mm";
                Label info = new Label { Left = 14, Top = 14, Width = 360, Height = 34, Text = "Bore diameter: " + Format(boreDiameter) + " " + unit + ". Enter the thread major diameter and pitch." };
                Label majorLabel = new Label { Left = 14, Top = 60, Width = 145, Text = "Thread major diameter:" };
                _major = NumberBox(165, 56, Math.Min(999, boreDiameter + (units == Units.Inches ? 0.02 : 0.5)));
                Label pitchLabel = new Label { Left = 14, Top = 96, Width = 145, Text = "Pitch:" };
                _pitch = NumberBox(165, 92, units == Units.Inches ? 0.02 : 0.5);
                Label unitLabelControl = new Label { Left = 275, Top = 60, Width = 100, Text = unit };
                Label units2 = new Label { Left = 275, Top = 96, Width = 100, Text = unit };
                Button cancel = new Button { Left = 205, Top = 138, Width = 78, Text = "Cancel", DialogResult = DialogResult.Cancel };
                Button apply = new Button { Left = 289, Top = 138, Width = 80, Text = "Use thread" };
                apply.Click += OnApply;
                Controls.AddRange(new Control[] { info, majorLabel, _major, pitchLabel, _pitch, unitLabelControl, units2, cancel, apply });
                AcceptButton = apply;
                CancelButton = cancel;
            }

            private NumericUpDown NumberBox(int left, int top, double value)
            {
                return new NumericUpDown { Left = left, Top = top, Width = 100, DecimalPlaces = _units == Units.Inches ? 4 : 3, Minimum = 0.001M, Maximum = 999M, Increment = _units == Units.Inches ? 0.001M : 0.05M, Value = (decimal)Math.Max(0.001, Math.Min(999, value)) };
            }

            private void OnApply(object sender, EventArgs e)
            {
                double major = (double)_major.Value;
                double pitch = (double)_pitch.Value;
                if (major <= _boreDiameter || pitch <= 0)
                {
                    MessageBox.Show(this, "Thread major diameter must be larger than the selected bore, and pitch must be positive.", "Custom Internal Thread", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                double mmPerUnit = _units == Units.Inches ? 25.4 : 1.0;
                string name = "Custom " + major.ToString("0.####", CultureInfo.CurrentCulture) + " × " + pitch.ToString("0.####", CultureInfo.CurrentCulture);
                Thread = new ThreadSpec { Name = name, MajorDiameterMm = major * mmPerUnit, PitchMm = pitch * mmPerUnit, TapDrillMm = _boreDiameter * mmPerUnit, IsCustom = true };
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}
