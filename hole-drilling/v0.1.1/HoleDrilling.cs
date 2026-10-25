using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using CamBam;
using CamBam.CAD;
using CamBam.CAM;
using CamBam.UI;
using CamBam.Values;

namespace HoleDrilling
{
    public sealed class HoleDrillingPlugin
    {
        private static CamBamUI _ui;

        public static void InitPlugin(CamBamUI ui)
        {
            _ui = ui;
            ToolStripMenuItem item = new ToolStripMenuItem("Hole Drilling...");
            item.Click += OnClick;
            ui.Menus.mnuPlugins.DropDownItems.Add(item);
        }

        private static void OnClick(object sender, EventArgs e)
        {
            try
            {
                if (_ui == null || _ui.ActiveView == null || _ui.ActiveView.CADFile == null)
                {
                    MessageBox.Show("Open a CamBam drawing and select the hole circles first.", "Hole Drilling");
                    return;
                }

                CADFile file = _ui.ActiveView.CADFile;
                if (file.DrawingUnits != Units.Millimeters && file.DrawingUnits != Units.Inches)
                {
                    MessageBox.Show("Hole Drilling supports drawings in millimeters or inches.", "Hole Drilling");
                    return;
                }

                List<Circle> circles = _ui.ActiveView.SelectedEntities.OfType<Circle>().ToList();
                if (circles.Count == 0)
                {
                    MessageBox.Show("Select one or more circles representing the required hole diameters.", "Hole Drilling");
                    return;
                }

                double tolerance = file.DrawingUnits == Units.Inches ? 0.002 : 0.05;
                List<List<Circle>> groups = GroupCircles(circles, tolerance);
                List<DrillPlan> plans = new List<DrillPlan>();

                foreach (List<Circle> group in groups)
                {
                    double holeDiameter = group[0].Diameter;
                    LibraryTool tool = FindLargestEndMill(file, holeDiameter);
                    if (tool == null)
                    {
                        string libraryName = file.ActivePart == null || String.IsNullOrWhiteSpace(file.ActivePart.ToolLibrary)
                            ? (file.DrawingUnits == Units.Inches ? "Default-in" : "Default-mm")
                            : file.ActivePart.ToolLibrary;
                        MessageBox.Show(
                            "No regular EndMill smaller than the " + Format(holeDiameter) + " " + UnitLabel(file.DrawingUnits) +
                            " hole was found in the active tool library (" + libraryName + "). Add a smaller EndMill, then try again.",
                            "Hole Drilling", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    using (DepthDialog dialog = new DepthDialog(holeDiameter, tool, file.DrawingUnits, plans.Count + 1, groups.Count))
                    {
                        if (dialog.ShowDialog(ThisApplication.TopWindow) != DialogResult.OK)
                            return;
                        plans.Add(new DrillPlan
                        {
                            Circles = group.Cast<Entity>().ToList(),
                            HoleDiameter = holeDiameter,
                            Tool = tool,
                            TargetDepth = dialog.TargetDepth
                        });
                    }
                }

                foreach (DrillPlan plan in plans)
                    CreateDrillOperation(file, plan);

                file.Modified = true;
                MessageBox.Show(
                    "Created " + plans.Count + " spiral drilling MOP(s) for " + groups.Count + " hole-size group(s).\r\n\r\n" +
                    "Review the selected tools, spiral depth increment, feeds, and clearance plane before generating G-code.",
                    "Hole Drilling", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not create the drilling operations.\r\n\r\n" + ex.Message, "Hole Drilling", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static List<List<Circle>> GroupCircles(List<Circle> circles, double tolerance)
        {
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
            return groups;
        }

        private static void CreateDrillOperation(CADFile file, DrillPlan plan)
        {
            MOPDrill drill = new MOPDrill(file, plan.Circles);
            drill.Name = "Hole Drill " + Format(plan.HoleDiameter) + " " + UnitLabel(file.DrawingUnits);
            // Start from CamBam's built-in Cutout CAM style so feedrates,
            // plunge rate, depth increment, and other inherited values come
            // from the user's unit-specific Standard style library.
            drill.Style = "cutout";

            // Apply the hole-specific settings after selecting the style.
            drill.HoleDiameter = new CBValue<double>(plan.HoleDiameter);
            drill.TargetDepth = new CBValue<double>(plan.TargetDepth);
            drill.DrillingMethod = new CBValue<DrillingMethods>(DrillingMethods.SpiralMill_CW);
            drill.SpiralFlatBase = new CBValue<bool>(true);
            drill.ToolNumber = new CBValue<int>(plan.Tool.Number);
            drill.ToolDiameter = new CBValue<double>(plan.Tool.Diameter);
            drill.ToolProfile = new CBValue<ToolProfiles>(ToolProfiles.EndMill);

            CamBamUI.MainUI.InsertMOP(drill);
        }

        private sealed class DrillPlan
        {
            public List<Entity> Circles;
            public double HoleDiameter;
            public LibraryTool Tool;
            public double TargetDepth;
        }

        private sealed class LibraryTool
        {
            public int Number;
            public string Name;
            public double Diameter;
        }

        private static string ResolveLibraryPath(CADFile file)
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string configured = file.ActivePart == null ? null : file.ActivePart.ToolLibrary;
            if (!String.IsNullOrWhiteSpace(configured))
            {
                string name = configured.Trim();
                if (String.IsNullOrEmpty(Path.GetExtension(name))) name += ".xml";
                if (Path.IsPathRooted(name)) return File.Exists(name) ? name : null;
                string[] namedPaths = new string[]
                {
                    Path.Combine(programData, "CamBam plus 1.0", "tools", name),
                    Path.Combine(programFiles, "CamBam plus 1.0", "tools", name)
                };
                return namedPaths.FirstOrDefault(File.Exists);
            }

            string defaultName = file.DrawingUnits == Units.Inches ? "Default-in.xml" : "Default-mm.xml";
            string[] defaults = new string[]
            {
                Path.Combine(programData, "CamBam plus 1.0", "tools", defaultName),
                Path.Combine(programFiles, "CamBam plus 1.0", "tools", defaultName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", defaultName)
            };
            return defaults.FirstOrDefault(File.Exists);
        }

        private static LibraryTool FindLargestEndMill(CADFile file, double holeDiameter)
        {
            string libraryPath = ResolveLibraryPath(file);
            if (libraryPath == null) return null;

            XmlDocument document = new XmlDocument();
            document.Load(libraryPath);
            List<LibraryTool> candidates = new List<LibraryTool>();
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
                string name = definition.Attributes["Name"] == null ? "" : definition.Attributes["Name"].Value;
                if (description.Contains("thread mill") || name.StartsWith("TM-", StringComparison.OrdinalIgnoreCase) ||
                    (partCode != null && partCode.InnerText.StartsWith("CUT000", StringComparison.OrdinalIgnoreCase)))
                    continue;

                int toolNumber;
                double toolDiameter;
                if (!Int32.TryParse(index.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out toolNumber) ||
                    !Double.TryParse(diameter.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, out toolDiameter))
                    continue;
                if (toolDiameter > 0 && toolDiameter < holeDiameter - 0.000001)
                {
                    candidates.Add(new LibraryTool
                    {
                        Number = toolNumber,
                        Name = String.IsNullOrWhiteSpace(name) ? Format(toolDiameter) + " " + UnitLabel(file.DrawingUnits) + " EndMill" : name,
                        Diameter = toolDiameter
                    });
                }
            }
            return candidates.OrderByDescending(tool => tool.Diameter).FirstOrDefault();
        }

        private sealed class DepthDialog : Form
        {
            private readonly NumericUpDown _depth;
            public double TargetDepth { get { return -Math.Abs((double)_depth.Value); } }

            public DepthDialog(double holeDiameter, LibraryTool tool, Units units, int groupNumber, int groupCount)
            {
                string unit = UnitLabel(units);
                Text = "Hole Drilling - Group " + groupNumber + " of " + groupCount;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                MinimizeBox = false;
                MaximizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new Size(480, 190);

                Label hole = new Label { Left = 16, Top = 16, Width = 448, Height = 24, Text = "Hole diameter: " + Format(holeDiameter) + " " + unit };
                Label toolLabel = new Label { Left = 16, Top = 44, Width = 448, Height = 36, Text = "Selected end mill: " + tool.Name + " (" + Format(tool.Diameter) + " " + unit + ")" };
                Label depthLabel = new Label { Left = 16, Top = 96, Width = 160, Height = 24, Text = "Target depth (± accepted):" };
                _depth = new NumericUpDown
                {
                    Left = 178, Top = 92, Width = 136, DecimalPlaces = units == Units.Inches ? 4 : 3,
                    Minimum = -999M, Maximum = 999M, Increment = units == Units.Inches ? 0.01M : 0.1M, Value = 0M
                };
                Label unitsLabel = new Label { Left = 322, Top = 96, Width = 120, Height = 24, Text = unit + "; stored negative" };
                Button cancel = new Button { Left = 294, Top = 146, Width = 78, Text = "Cancel", DialogResult = DialogResult.Cancel };
                Button create = new Button { Left = 378, Top = 146, Width = 80, Text = "Create" };
                create.Click += OnCreate;
                Controls.AddRange(new Control[] { hole, toolLabel, depthLabel, _depth, unitsLabel, cancel, create });
                AcceptButton = create;
                CancelButton = cancel;
            }

            private void OnCreate(object sender, EventArgs e)
            {
                if (_depth.Value == 0M)
                {
                    MessageBox.Show(this, "Enter a non-zero target depth. Positive or negative input is accepted; the MOP depth will be stored as negative.", "Hole Drilling", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private static string UnitLabel(Units units) { return units == Units.Inches ? "in" : "mm"; }
        private static string Format(double value) { return value.ToString("0.####", CultureInfo.CurrentCulture); }
    }
}
