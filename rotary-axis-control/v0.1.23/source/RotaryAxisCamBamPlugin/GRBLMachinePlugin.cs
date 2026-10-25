using System;
using System.Windows.Forms;
using System.Drawing;
using CamBam.UI;
using System.Xml.Serialization;
using System.IO;
using GRBLMachine.UI;
using GRBLMachine.Properties;
using CamBam;
using CamBam.Extensions;
using System.Reflection;

namespace GRBLMachine
{

  public static class GRBLMachinePlugin
  {
    public const  string       Version = "0.1.23";

    public static TabPage      Page;
    public static GRBLProps    Props   = new GRBLProps();
    public static bool         Connected           { get; private set; }
    public static MachineState CurrentMachineState { get; private set; }

    /// <summary>
    /// Static initializer, called as soon as a reference to this class is made. e.g. when being probed by 'InitPlugins'
    /// </summary>
    static GRBLMachinePlugin()
    {
      //
      // since our libraries are also in the plugins folder, the default resolving will fail, load 'manually' from our folder
      //
      AppDomain.CurrentDomain.AssemblyResolve += (object sender, ResolveEventArgs args) =>
      {
        AssemblyName assyName  = new AssemblyName(args.Name);

        if (!string.Equals(assyName.Name, "CamBam.CAD.Solids", StringComparison.OrdinalIgnoreCase))
          return null;

        string assemblyPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), assyName.Name + ".dll");

        return File.Exists(assemblyPath) ? Assembly.LoadFrom(assemblyPath) : null;
      };
    }

#region Events

    public        delegate void PropChangedHandler(string name, object newvalue);
    public static event         PropChangedHandler PropertyChanged;

    public static void PropertyChange(string name, object newvalue)
    {
      if (PropertyChanged != null)
        PropertyChanged(name, newvalue);
    }


    public        delegate void LogHandler(string log, bool verbose);
    public static event         LogHandler Logging;

    public static void Log(string log, bool verbose = false)
    {
      if (Logging != null)
        Logging(log, verbose);
    }


    public        delegate void PosHandler(PosType type, double x, double y, double z);
    public static event         PosHandler Position;

    public static void Pos(PosType type, double x, double y, double z)
    {
      if (Position != null)
        Position(type,x,y,z);
    }

    public        delegate void RotaryPosHandler(PosType type, double position);
    public static event         RotaryPosHandler RotaryPosition;
    public static event         Action<bool> RotaryAxisAvailabilityChanged;
    public static bool          RotaryAxisAvailable { get; private set; }
    private static bool[]       _rotaryAxesAvailable = new bool[3];
    public        delegate void RotaryAxisAvailabilityHandler(RotaryAxisLetter axis, bool available);
    public static event         RotaryAxisAvailabilityHandler RotaryAxisAvailabilityChangedForAxis;
    public        delegate void RotaryAxisPositionHandler(RotaryAxisLetter axis, PosType type, double position);
    public static event         RotaryAxisPositionHandler RotaryPositionForAxis;

    public static void RotaryPos(PosType type, double position)
    {
      if (RotaryPosition != null)
        RotaryPosition(type, position);
    }

    public static void SetRotaryAxisAvailability(bool available)
    {
      RotaryAxisAvailable = available;
      if (RotaryAxisAvailabilityChanged != null)
        RotaryAxisAvailabilityChanged(available);
    }

    public static bool IsRotaryAxisAvailable(RotaryAxisLetter axis)
    {
      return _rotaryAxesAvailable[(int)axis];
    }

    public static void SetRotaryAxisAvailability(RotaryAxisLetter axis, bool available)
    {
      int index = (int)axis;
      if (_rotaryAxesAvailable[index] == available)
        return;

      _rotaryAxesAvailable[index] = available;
      if (RotaryAxisAvailabilityChangedForAxis != null)
        RotaryAxisAvailabilityChangedForAxis(axis, available);

      if (Props.RotaryAxis == axis)
        SetRotaryAxisAvailability(available);
    }

    public static void RotaryPos(RotaryAxisLetter axis, PosType type, double position)
    {
      if (RotaryPositionForAxis != null)
        RotaryPositionForAxis(axis, type, position);
    }


    public enum MachineState
    {
      Idle,
      Run,
      Hold,
      Jog,
      Alarm,
      Door,
      Check,
      Home,
      Sleep
    }

    public        delegate void StatHandler(MachineState state, int code);
    public static event         StatHandler State;

    public static void Stat(MachineState state, int code = -1)
    {
      if (State != null)
        State(state,code);
    }


    public enum Response
    {
      ok,
      error,
      alarm
    }

    public        delegate void ResponseHandler(Response response, int code);
    public static event         ResponseHandler Responding;

    public static void Respond(Response response, int code = 0)
    {
      if (Responding != null)
        Responding(response, code);
    }


    public        delegate void SpeedHandler(double feed, int rpm);
    public static event         SpeedHandler Speed;

    public static void Speeds(double feed, int rpm)
    {
      if (Speed!= null)
        Speed(feed,rpm);
    }

#endregion // Events

#region Publics

    public static void InitPlugin(CamBamUI ui)
    {
#if DEBUG
      MessageBox.Show("Now it's time to attach the debugger :-)","GRBLMachinePlugin");
#endif
      Page  = new TabPage("Rotary Control") { UseVisualStyleBackColor = false };

      UpdatePostProcessor();

      ReadConfig();

      //
      // Controversial action #1: Should a plugin tamper with CamBam's UI ??? ;) :P
      //
      if (Props.EnableVisualStyles == EnabledDisabled.Enabled)
        Application.EnableVisualStyles();


      //
      // Create the menu item. Basically does not do much :P
      //
      ToolStripDropDownItem gcodeMenuItem = new ToolStripMenuItem();

      gcodeMenuItem.Text   = "Rotary Axis Control";
      gcodeMenuItem.Image  = Resources.Grbl_Logo_150px;
      gcodeMenuItem.Click += new EventHandler(plugin_clicked);

      ui.Menus.mnuPlugins.DropDownItems.Add(gcodeMenuItem);

      //
      // Find ZoomToFit menu item and replace it by ZoomToFitEx
      //
      foreach (ToolStripItem item in ui.Menus.mnuView.DropDownItems)
      {
        if (item.Name == "mnuZoomToFit")
        {
          ToolStripMenuItem mnuZoomToFit = item as ToolStripMenuItem;
          int               index        = ui.Menus.mnuView.DropDownItems.IndexOf(mnuZoomToFit);

          if (index != -1)
          {
            ui.Menus.mnuView.DropDownItems.Insert  (index, new ToolStripMenuItem(mnuZoomToFit.Text, mnuZoomToFit.Image, MnuZoomToFit_Click, mnuZoomToFit.ShortcutKeys));
            ui.Menus.mnuView.DropDownItems.RemoveAt(index + 1);
          }

          break;
        }
      }

      //
      // As soon as CamBam is up and running, we will hookup our UI. Checking is done on App.Idle...
      //
      Application.Idle    += Application_Idle;
    }

    #endregion // Publics

#region Privates

    /// <summary>
    /// Check for an update of this plugin's post processor
    /// </summary>
    private static void UpdatePostProcessor()
    {
      FileStream   fs              = null;
      StreamReader sr              = null;
      string       rotaryAxisPost = null;

      try {
        string systemFolder = Path.GetDirectoryName(CamBam.CamBamConfig.SystemConfigFilename);
        string postFolder = Path.Combine(systemFolder, "post");
        Directory.CreateDirectory(postFolder);
        rotaryAxisPost = Path.Combine(postFolder, "RotaryAxisControl.cbpp");

        const string PostVersionMarker = "RotaryAxisControl V";

        bool  create = false;

        if (File.Exists(rotaryAxisPost))
        {
          fs = File.OpenRead(rotaryAxisPost);

          sr = new StreamReader(fs);

          string sf = sr.ReadToEnd();
          int    gf = sf.IndexOf(PostVersionMarker);
          string vf = sf.Substring(gf + PostVersionMarker.Length, 7);

          sr.Close();
          sr = null;

          fs.Close();
          fs = null;

          sr = new StreamReader(new MemoryStream(Resources.RotaryAxisControl));

          string sm = sr.ReadToEnd();
          int    gm = sm.IndexOf(PostVersionMarker);
          string vm = sm.Substring(gm + PostVersionMarker.Length, 7);

          sr.Close();
          sr = null;

          if (vm.CompareTo(vf) > 0)
            create = MessageBox.Show("A newer version of 'RotaryAxisControl.cbpp' is available.\r\n\r\nCurrent version: V" + vf + " new version: V" + vm + "\r\n\r\n\r\nOverwrite current version with new version ?","Rotary Axis Control",MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }
        else
          create = true;

        if (create)
        {
          fs = File.Create(rotaryAxisPost);

          fs.Write(Resources.RotaryAxisControl,0,Resources.RotaryAxisControl.Length);

          fs.Close();
          fs = null;

          CamBamConfig.Defaults.PostProcessors.ScanFolder();
        }
      }
      catch (Exception e) { MessageBox.Show("Unable to open/check/create/update " + rotaryAxisPost + "\r\n\r\n" + e.Message, "Rotary Axis Control", MessageBoxButtons.OK, MessageBoxIcon.Error); }
      finally
      {
        if (sr != null)
          sr.Close();

        if (fs != null)
          fs.Close();
      }
    }

    /// <summary>
    /// Read the configuration
    /// </summary>
    private static void ReadConfig()
    {
      FileStream file = null;

      try {
        file                           = File.OpenRead(Path.Combine(Path.GetDirectoryName(CamBam.CamBamConfig.SystemConfigFilename),"RotaryAxisControl.config"));
        GRBLProps props                = new XmlSerializer(typeof(GRBLProps)).Deserialize(file) as GRBLProps;

        Props.COMPort                  = props.COMPort;
        Props.AutoConnect              = props.AutoConnect;
        Props.Baudrate                 = props.Baudrate;
        Props.EnableVisualStyles       = props.EnableVisualStyles;

        Props.JogStepUnit              = props.JogStepUnit;
        Props.JogStepXY                = props.JogStepXY;
        Props.JogStepZ                 = props.JogStepZ;
        Props.RotaryAxis               = props.RotaryAxis;
        Props.RotaryJogStep            = props.RotaryJogStep;
        Props.RotaryJogFeed            = props.RotaryJogFeed;
        Props.RotaryIndexFeed          = props.RotaryIndexFeed;

        Props.ToolChangeProcess        = props.ToolChangeProcess;
        Props.ToolChangeX              = props.ToolChangeX;
        Props.ToolChangeY              = props.ToolChangeY;
        Props.ToolChangeZ              = props.ToolChangeZ;
        Props.ToolChangeUnits          = props.ToolChangeUnits;
        Props.ToolChangePlungeDistance = props.ToolChangePlungeDistance;
        Props.ToolChangePlungeFeed     = props.ToolChangePlungeFeed;


        Props.DisplayPosType           = props.DisplayPosType;
        Props.TrackMachine             = props.TrackMachine;
        Props.DrawLimits               = props.DrawLimits;
        Props.DefaultToolDiameter      = props.DefaultToolDiameter;
        Props.DefaultToolFluteLength   = props.DefaultToolFluteLength;
        Props.DefaultToolTotalLength   = props.DefaultToolTotalLength;
        Props.DefaultToolShankDiameter = props.DefaultToolShankDiameter;
        Props.DefaultToolVAngle        = props.DefaultToolVAngle;
        Props.DefaultToolProfile       = props.DefaultToolProfile;

        Props.ToolColor                = Color.FromArgb(props.ToolColorARGB);
        Props.ToolTransparency         = props.ToolTransparency;
        Props.LaserColor               = Color.FromArgb(props.LaserColorARGB);
        Props.LaserTransparency        = props.LaserTransparency;
        Props.LimitsColor              = Color.FromArgb(props.LimitsColorARGB);
        Props.LimitsTransparency       = props.LimitsTransparency;
          
        Props.DisplayExpanded          = props.DisplayExpanded;
        Props.ProductionExpanded       = props.ProductionExpanded;
        Props.JoggingExpanded          = props.JoggingExpanded;
        Props.ConsoleExpanded          = props.ConsoleExpanded;
        Props.ConnectionExpanded       = props.ConnectionExpanded;
        Props.AboutExpanded            = props.AboutExpanded;

        Props.SelectedTab              = props.SelectedTab;
      }
      catch (FileNotFoundException)     { }
      catch (InvalidOperationException) { MessageBox.Show("Definition change detected for RotaryAxisControl.config\r\n\r\nAll settings reset to default", "Rotary Axis Control", MessageBoxButtons.OK, MessageBoxIcon.Information); }
      catch (Exception e)               { MessageBox.Show("Unable to open RotaryAxisControl.config\r\n\r\n" + e.ToString(), "Rotary Axis Control", MessageBoxButtons.OK, MessageBoxIcon.Error); }
      finally
      {
        if (file != null)
          file.Close();
      }
    }

    /// <summary>
    /// Write plugin settings using normal .NET XmlSerialization
    /// </summary>
    private static void WriteConfig()
    {
      Props.DisplayExpanded    = (Page.Controls[0] as GRBLMachinePage).DisplayExpander   .Expanded;
      Props.ProductionExpanded = (Page.Controls[0] as GRBLMachinePage).ProductionExpander.Expanded;
      Props.JoggingExpanded    = (Page.Controls[0] as GRBLMachinePage).JoggingExpander   .Expanded;
      Props.ConsoleExpanded    = (Page.Controls[0] as GRBLMachinePage).ConsoleExpander   .Expanded;
      Props.ConnectionExpanded = (Page.Controls[0] as GRBLMachinePage).ConnectionExpander.Expanded;
      Props.AboutExpanded      = (Page.Controls[0] as GRBLMachinePage).AboutExpander     .Expanded;

      Props.SelectedTab        = CamBamUI.MainUI.SysTabs                          .SelectedTab;

      // hack to satisfy an XMLDeserializer bug: Does not want to deserialize 'Color', so use ARGB values instead
      Props.ToolColorARGB      = Props.ToolColor  .ToArgb();
      Props.LaserColorARGB     = Props.LaserColor .ToArgb();
      Props.LimitsColorARGB    = Props.LimitsColor.ToArgb();

      FileStream file = null;
      try
      {
        file = File.Create(Path.Combine(Path.GetDirectoryName(CamBam.CamBamConfig.SystemConfigFilename),"RotaryAxisControl.config"));
        new XmlSerializer(typeof(GRBLProps)).Serialize(file, Props);
      }
      catch (Exception e) { MessageBox.Show("Unable to write RotaryAxisControl.config\r\n\r\n" + e.ToString(), "Rotary Axis Control", MessageBoxButtons.OK, MessageBoxIcon.Error); }
      finally
      {
        if (file != null)
          file.Close();
      }
    }

    /// <summary>
    /// This method integrates the plugin UI into CamBam
    /// </summary>
    /// <returns></returns>
    private static bool InstallTab()
    {
      CamBamUI ui = CamBamUI.MainUI;

      // Since CamBam comes with an unconstructed UI, durin 'InitPlugin' there is no UI content yet...
      if (ui.SysTabs != null)
      {
        // get the tabcontrol which hosts CamBam's 'Drawing' and 'System' pages
        TabControl tabs = ui.SysTabs.Tabs();

        //
        // Controversial action #2: Should a plugin tamper with CamBam's UI ??? ;) :P
        //
        if (Props.EnableVisualStyles == EnabledDisabled.Enabled)
        {
          // set some properties on SysTabs because it just looks better when VisualStyles ar on.
          ui.SysTabs.BackColor        = SystemColors.Control;
          ui.SysTabs.Padding          = new Padding(3,6,1,0);

          foreach (TabPage p in tabs.TabPages)
            p.UseVisualStyleBackColor = false;
        }

        //
        // Just for conveniance: Add a right-click menu to clear the messages
        //
        ThisApplication.TextLogger.ContextMenu              = new ContextMenu();
        ThisApplication.TextLogger.ContextMenu.MenuItems.Add( new MenuItem   ("Clear Messages", (x,y) => { ThisApplication.ClearLogMessages(); }));

        // hook up all eventhandlers
        ThisApplication.TopWindow.FormClosing  += TopWindow_FormClosing;

        tabs.SelectedIndexChanged              += Tabs_SelectedIndexChanged;
        tabs.Selecting                         += Tabs_Selecting;

        State                                  += GRBLMachinePlugin_State;
        ConnectionExpander.Connected           += ConnectionExpander_Connected;
        ConnectionExpander.Disconnected        += ConnectionExpander_Disconnected;

        // add the GRBL icon to the tabcontrol's imagelist
                          tabs.ImageList.Images.Add(Resources.Grbl_Logo_150px);
        Page.ImageIndex = tabs.ImageList.Images.Count - 1;
        // add our UI to the new tabpage
        Page.Controls.Add(new GRBLMachinePage());
        // finally add the Machine page to the tabcontrol
        tabs.TabPages.Add(Page);

        // set the various UI states according to what was saved in RotaryAxisControl.config
        (Page.Controls[0] as GRBLMachinePage).DisplayExpander   .Expanded    = Props.DisplayExpanded;
        (Page.Controls[0] as GRBLMachinePage).ProductionExpander.Expanded    = Props.ProductionExpanded;
        (Page.Controls[0] as GRBLMachinePage).JoggingExpander   .Expanded    = Props.JoggingExpanded;
        (Page.Controls[0] as GRBLMachinePage).ConsoleExpander   .Expanded    = Props.ConsoleExpanded;
        (Page.Controls[0] as GRBLMachinePage).ConnectionExpander.Expanded    = Props.ConnectionExpanded;
        (Page.Controls[0] as GRBLMachinePage).AboutExpander     .Expanded    = Props.AboutExpanded;

        CamBamUI.MainUI.SysTabs                                 .SelectedTab = Props.SelectedTab;

        // greet the user :)
        GRBLMachinePlugin.Log("Welcome to Rotary Axis Control v" + Version + "\n\n");

        return true;
      }
      return false;
    }

    private static void MnuZoomToFit_Click(object sender, EventArgs e)
    {
      CamBamUI.MainUI.ActiveView.ZoomToFitEx();
    }


#endregion // Privates

#region Event handlers
    // book keeping
    private static int        lastIndex          = 0;
    private static object[]   rgObjectProperties = new object[10];
    private static Control    _tabsHostPanel;
    private static Control    _propertiesHostPanel;
    private static DockStyle  _tabsHostDock;
    private static AnchorStyles _tabsHostAnchor;
    private static Rectangle _tabsHostBounds;
    private static bool       _propertiesHostWasVisible;
    private static bool       _propertiesPanelCollapseApplied;
    private static bool       _propertiesPanelLookupLogged;

    //
    // Forms
    //

    private static void Application_Idle(object sender, EventArgs e)
    {
      if (InstallTab())
        Application.Idle -= Application_Idle; // if installation successful, detach ourselves from Idle
    }

    private static void TopWindow_FormClosing(object sender, FormClosingEventArgs e)
    {
      WriteConfig();
    }

    private static void Tabs_Selecting(object sender, TabControlCancelEventArgs e)
    {
      CamBamUI ui = CamBamUI.MainUI;

      rgObjectProperties[lastIndex] = ui.ObjectProperties.SelectedObject;

      lastIndex = ui.SysTabs.SelectedTab;
    }

    private static void Tabs_SelectedIndexChanged(object sender, EventArgs e)
    {
      CamBamUI   ui   = CamBamUI.MainUI;
      TabControl tabs = ui.SysTabs.Tabs();

      if (tabs.SelectedIndex == tabs.TabPages.IndexOf(Page))
      {
        // prevent a smaller size when dragging the splitter than our UI requires
        ui.ObjectProperties.SelectedObject    = Props;
        ui.SysTabs.DividerPanel().MinimumSize = new Size(420,0);
        SetPropertiesPanelCollapsed(ui, true);
      }
      else
      {
        // back to default
        SetPropertiesPanelCollapsed(ui, false);
        ui.ObjectProperties.SelectedObject    = rgObjectProperties[tabs.SelectedIndex];
        ui.SysTabs.DividerPanel().MinimumSize = new Size(0,0);
      }
    } 

    /// <summary>
    /// Hide CamBam's property-inspector host panel while the machine controls are
    /// selected, and let the SystemTabs host fill the available sidebar space.
    /// </summary>
    private static void SetPropertiesPanelCollapsed(CamBamUI ui, bool collapse)
    {
      if (collapse)
      {
        if (_propertiesPanelCollapseApplied)
          return;

        Control tabsHost = ui.SysTabs.Parent;
        Control propertiesHost = ui.ObjectProperties.Parent;
        if (tabsHost == null || propertiesHost == null || tabsHost.Parent == null || tabsHost.Parent != propertiesHost.Parent)
        {
          if (!_propertiesPanelLookupLogged)
          {
            Log("Unable to locate the CamBam tabs and properties host panels; Rotary Control will leave the properties pane visible.");
            _propertiesPanelLookupLogged = true;
          }
          return;
        }

        _tabsHostPanel = tabsHost;
        _propertiesHostPanel = propertiesHost;
        _tabsHostDock = tabsHost.Dock;
        _tabsHostAnchor = tabsHost.Anchor;
        _tabsHostBounds = tabsHost.Bounds;
        _propertiesHostWasVisible = propertiesHost.Visible;
        _propertiesPanelCollapseApplied = true;

        tabsHost.Parent.SuspendLayout();
        try
        {
          propertiesHost.Visible = false;
          tabsHost.Dock = DockStyle.Fill;
        }
        finally { tabsHost.Parent.ResumeLayout(true); }
      }
      else if (_propertiesPanelCollapseApplied)
      {
        if (_tabsHostPanel != null && _propertiesHostPanel != null &&
            !_tabsHostPanel.IsDisposed && !_propertiesHostPanel.IsDisposed &&
            _tabsHostPanel.Parent == _propertiesHostPanel.Parent)
        {
          Control parent = _tabsHostPanel.Parent;
          parent.SuspendLayout();
          try
          {
            _tabsHostPanel.Dock = _tabsHostDock;
            _tabsHostPanel.Anchor = _tabsHostAnchor;
            _tabsHostPanel.Bounds = _tabsHostBounds;
            _propertiesHostPanel.Visible = _propertiesHostWasVisible;
          }
          finally { parent.ResumeLayout(true); }
        }

        _tabsHostPanel = null;
        _propertiesHostPanel = null;
        _propertiesPanelCollapseApplied = false;
      }
    }

    public static void EnsurePropertiesPaneState(bool rotaryTabSelected)
    {
      CamBamUI ui = CamBamUI.MainUI;
      if (ui != null)
        SetPropertiesPanelCollapsed(ui, rotaryTabSelected);
    }

    public static void plugin_clicked(object sender, EventArgs e)
    {
      TabControl tabs = CamBam.UI.CamBamUI.MainUI.SysTabs.Tabs();

      tabs.SelectedIndex = tabs.TabPages.IndexOf(Page);
    }

    //
    // GRBLMachine
    //

    private static void ConnectionExpander_Disconnected()
    {
      Connected = false;
    }

    private static void ConnectionExpander_Connected()
    {
      Connected = true;
    }

    private static void GRBLMachinePlugin_State(MachineState state, int code)
    {
      CurrentMachineState = state;
    }

#endregion // Event Handlers
  }
}
