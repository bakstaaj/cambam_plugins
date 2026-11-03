using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CamBam;
using CamBam.CAM;
using CamBam.CAD;
using CamBam.Geom;
using CamBam.UI;
using CamBam.Library;
using CamBam.Extensions;
using GRBLMachine.Utils;
using System.Media;
using System.Globalization;
using System.Drawing;

namespace GRBLMachine.UI
{
  public partial class ProductionExpander : Expander
  {
    private const  string                   V1JOG             = "$J=";
    private        string                   _jogLeadIn        = null;

    private string                         _lastOutfile       = "";
    private bool                           _connected;
    private LinkedList<string>             _fileData          = new LinkedList<string>();
    private Thread                         _writeThread;
    private ManualResetEvent               _pauseEvent        = new ManualResetEvent(true);
    private ManualResetEvent               _toolChangingEvent = new ManualResetEvent(true);
    private bool                           _toolChanging;
    private ManualResetEvent               _toolChangerEvent  = new ManualResetEvent(false);

    private string                         _sendingFileName;

    private double                         _lastX;
    private double                         _lastY;
    private double                         _lastZ;
    private int                            _lastSpeed;
    private PosType                        _lastType;
    private GRBLMachinePlugin.MachineState _lastState;

    private double                         _WCO_X;
    private double                         _WCO_Y;
    private double                         _WCO_Z;

    private double                         _preChangeX;
    private double                         _preChangeY;
    private double                         _preChangeZ;
    private int                            _preChangeSpeed;
    private PosType                        _preChangeType;


    private static HashSet<string>         _unsupportedGCODEs     = new HashSet<string>();
    private static HashSet<string>         _noRemoveCommentGCODEs = new HashSet<string>();
    private static CultureInfo              EN_US                 = new CultureInfo("en-US");
    private Label                          _simSpeedLabel;
    private Button[]                       _simSpeedButtons;
    private Label                          _partLabel;
    private ComboBox                       _partSelector;
    private Button                         _generatePartButton;
    private CADFile                        _partSelectorDocument;
    private string                         _partSelectorSignature;
    private bool                           _refreshingPartSelector;
    private bool                           _assigningPartOutput;
    private string                         _partOutputFile;

    private sealed class PartChoice
    {
      internal CAMPart Part;
      internal string DisplayName;
      public override string ToString() { return DisplayName; }
    }

    static ProductionExpander()
    {
      _unsupportedGCODEs    .Add("G40");
      _unsupportedGCODEs    .Add("G41");
      _unsupportedGCODEs    .Add("G64");
      _unsupportedGCODEs    .Add("G81");
      _unsupportedGCODEs    .Add("G83");
      _unsupportedGCODEs    .Add("G91.1");

      _noRemoveCommentGCODEs.Add("M6");
      _noRemoveCommentGCODEs.Add("T");
    }

    public ProductionExpander()
    {
      InitializeComponent();
      UiTheme.Apply(this);

      ApplyModernStyle(PlayButton, Color.FromArgb(226, 244, 232));
      ApplyModernStyle(PauseButton, Color.FromArgb(231, 241, 250));
      ApplyModernStyle(StopButton, Color.FromArgb(250, 232, 232));
      ApplyModernStyle(ToolChangeButton, Color.FromArgb(238, 241, 245));
      UseModernButtonText(PlayButton, "▶", 15F);
      UseModernButtonText(PauseButton, "Ⅱ", 13F);
      UseModernButtonText(StopButton, "■", 12F);
      UseModernButtonText(ToolChangeButton, "M6", 9F);
      PauseButton.CheckedChanged += (sender, e) =>
      {
        PauseButton.Text = PauseButton.Checked ? "▶" : "Ⅱ";
        PauseButton.BackColor = PauseButton.Checked ? Color.FromArgb(215, 233, 248) : Color.FromArgb(231, 241, 250);
      };

      if (!DesignMode)
        Application     .Idle         += Application_Idle;

      ConnectionExpander.Connected    += ConnectionExpander_Connected;
      ConnectionExpander.Disconnected += ConnectionExpander_Disconnected;

      CreateSimulatorSpeedControls();
      CreatePartSelectionControls();
      UpdateSimulatorSpeedControls();

      GRBLMachinePlugin .Responding   += GRBLMachinePlugin_Responding;
      GRBLMachinePlugin .Position     += GRBLMachinePlugin_Position;
      GRBLMachinePlugin .Speed        += GRBLMachinePlugin_Speed;
      GRBLMachinePlugin. State        += GRBLMachinePlugin_State;
    }

#region Publics 

    public        delegate void JobHandler();
    public static event         JobHandler JobStarted;
    public static event         JobHandler JobPaused;
    public static event         JobHandler JobResumed;
    public static event         JobHandler JobStopped;
    public static bool          JobActive { get; private set; }
    public static event Action<string> NCFileSelected;

    public        delegate void ToolChangeHandler();
    public static event         ToolChangeHandler ToolChangeStarted;
    public static event         ToolChangeHandler ToolChangeEnded;

    public        delegate void ToolChangedHandler(ToolDefinition tooldef);
    public static event         ToolChangedHandler ToolChanged;

#endregion // Publics

#region Overrides

    protected override void OnLoad(EventArgs e)
    {
      base.OnLoad(e);

      // Expander.OnLoad restores the designer height after the constructor runs.
      // Reapply the layout here so the new part selector row is not clipped.
      UpdateSimulatorSpeedControls();
      LayoutPartSelectionControls();

      if (!DesignMode)
        ThisApplication.TopWindow.FormClosing += TopWindow_FormClosing;
    }

#endregion // Overrides

#region Privates

    private bool CheckState()
    {
      if (_lastState == GRBLMachinePlugin.MachineState.Alarm || _lastState == GRBLMachinePlugin.MachineState.Hold)
      {
        MessageBox.Show("Machine is in " + _lastState.ToString().ToUpper() + " state.\r\n\r\n Please reset state and try again.","GRBLMachine - Play", MessageBoxButtons.OK, MessageBoxIcon.Stop);
        return false;
      }

      return true;
    }

    private void EnableButtons()
    {
      PlayButton      .Enabled = CanPlay      ();
      PauseButton     .Enabled = CanPause     ();
      StopButton      .Enabled = CanStop      ();
      ToolChangeButton.Enabled = CanToolChange();
    }

    private void CreateSimulatorSpeedControls()
    {
      _simSpeedLabel = new Label();
      _simSpeedLabel.AutoSize = true;
      _simSpeedLabel.Location = new System.Drawing.Point(12, 120);
      _simSpeedLabel.Text = "Sim speed:";
      _simSpeedLabel.ForeColor = UiTheme.Ink;
      ContentPanel.Controls.Add(_simSpeedLabel);

      int[] speeds = new int[] { 1, 2, 5, 10 };
      _simSpeedButtons = new Button[speeds.Length];
      for (int i = 0; i < speeds.Length; i++)
      {
        int speed = speeds[i];
        Button button = new Button();
        button.Location = new System.Drawing.Point(79 + (i * 39), 115);
        button.Size = new System.Drawing.Size(35, 25);
        button.Name = "SimulatorSpeed" + speed + "x";
        button.Text = speed + "x";
        button.Font = new System.Drawing.Font("Microsoft Sans Serif", 8F, System.Drawing.FontStyle.Bold);
        button.UseVisualStyleBackColor = false;
        ApplyModernStyle(button, Color.FromArgb(238, 244, 249));
        button.Click += (sender, e) =>
        {
          ConnectionExpander.SetSimulatorPlaybackSpeed(speed);
          UpdateSimulatorSpeedControls();
        };
        TheToolTip.SetToolTip(button, "Set simulated playback speed to " + speed + "x. Live machine operation is not affected.");
        ContentPanel.Controls.Add(button);
        _simSpeedButtons[i] = button;
      }
    }

    private void CreatePartSelectionControls()
    {
      _partLabel = new Label();
      _partLabel.AutoSize = true;
      _partLabel.Location = new System.Drawing.Point(12, 89);
      _partLabel.Text = "Part:";
      _partLabel.ForeColor = UiTheme.Ink;
      ContentPanel.Controls.Add(_partLabel);

      _partSelector = new ComboBox();
      _partSelector.DropDownStyle = ComboBoxStyle.DropDownList;
      _partSelector.Location = new System.Drawing.Point(52, 85);
      _partSelector.Size = new System.Drawing.Size(200, 24);
      _partSelector.Anchor = AnchorStyles.Top | AnchorStyles.Left;
      _partSelector.DropDown += (sender, e) => RefreshPartSelector(true);
      _partSelector.SelectedIndexChanged += (sender, e) =>
      {
        if (!_refreshingPartSelector && _generatePartButton != null)
          _generatePartButton.Enabled = _partSelector.SelectedItem is PartChoice && _writeThread == null;
      };
      TheToolTip.SetToolTip(_partSelector, "Choose a CamBam machining part from the active drawing.");
      ContentPanel.Controls.Add(_partSelector);

      _generatePartButton = new Button();
      _generatePartButton.Location = new System.Drawing.Point(259, 84);
      _generatePartButton.Size = new System.Drawing.Size(92, 26);
      _generatePartButton.Anchor = AnchorStyles.Top | AnchorStyles.Left;
      _generatePartButton.Text = "Generate";
      _generatePartButton.Font = new Font("Microsoft Sans Serif", 8F, FontStyle.Bold);
      ApplyModernStyle(_generatePartButton, Color.FromArgb(231, 241, 250));
      _generatePartButton.Enabled = false;
      _generatePartButton.Click += GeneratePartButton_Click;
      TheToolTip.SetToolTip(_generatePartButton, "Generate G-code for the selected CamBam part and show its toolpath preview.");
      ContentPanel.Controls.Add(_generatePartButton);
      ContentPanel.Resize += (sender, e) => LayoutPartSelectionControls();
      LayoutPartSelectionControls();
    }

    private void LayoutPartSelectionControls()
    {
      if (ContentPanel == null || _partSelector == null || _generatePartButton == null)
        return;

      const int selectorLeft = 52;
      const int rightMargin = 12;
      const int gap = 8;
      int buttonWidth = Math.Min(92, Math.Max(72, _generatePartButton.Width));
      int buttonLeft = ContentPanel.ClientSize.Width - rightMargin - buttonWidth;
      int selectorWidth = buttonLeft - selectorLeft - gap;
      if (selectorWidth < 80)
      {
        buttonWidth = Math.Max(64, buttonWidth - (80 - selectorWidth));
        buttonLeft = ContentPanel.ClientSize.Width - rightMargin - buttonWidth;
        selectorWidth = Math.Max(80, buttonLeft - selectorLeft - gap);
      }

      _generatePartButton.SetBounds(buttonLeft, 84, buttonWidth, 26);
      _partSelector.SetBounds(selectorLeft, 85, selectorWidth, 24);
    }

    private void RefreshPartSelector(bool force)
    {
      if (_partSelector == null || _writeThread != null)
        return;

      try
      {
        CamBamUI ui = CamBamUI.MainUI;
        CADFile document = ui == null || ui.ActiveView == null ? null : ui.ActiveView.CADFile;
        bool documentChanged = !Object.ReferenceEquals(document, _partSelectorDocument);
        if (documentChanged && _partSelectorDocument != null)
        {
          _partOutputFile = null;
          _lastOutfile = String.Empty;
        }
        StringBuilder signatureBuilder = new StringBuilder();
        List<PartChoice> choices = new List<PartChoice>();

        if (document != null && document.Parts != null)
        {
          foreach (CAMPart part in document.Parts)
          {
            if (part == null || !part.Enabled || part.MachineOps == null)
              continue;

            int enabledOps = 0;
            foreach (MachineOp op in part.MachineOps)
              if (op != null && op.Enabled) enabledOps++;
            if (enabledOps == 0)
              continue;

            string name = String.IsNullOrEmpty(part.Name) || part.Name.Trim().Length == 0 ? "Part" : part.Name;
            string displayName = name + " (" + enabledOps.ToString() + (enabledOps == 1 ? " operation)" : " operations)");
            choices.Add(new PartChoice { Part = part, DisplayName = displayName });
            signatureBuilder.Append(part.GetHashCode()).Append('|').Append(name).Append('|').Append(enabledOps).Append('|').Append(part.Enabled).Append(';');
          }
        }

        string signature = signatureBuilder.ToString();
        if (!force && Object.ReferenceEquals(document, _partSelectorDocument) && signature == _partSelectorSignature)
          return;

        PartChoice oldChoice = _partSelector.SelectedItem as PartChoice;
        CAMPart oldPart = oldChoice == null ? null : oldChoice.Part;
        _refreshingPartSelector = true;
        _partSelector.BeginUpdate();
        _partSelector.Items.Clear();
        foreach (PartChoice choice in choices)
          _partSelector.Items.Add(choice);

        int selectedIndex = -1;
        for (int i = 0; i < _partSelector.Items.Count; i++)
          if (Object.ReferenceEquals(((PartChoice)_partSelector.Items[i]).Part, oldPart)) { selectedIndex = i; break; }
        if (selectedIndex < 0 && _partSelector.Items.Count > 0)
          selectedIndex = 0;
        _partSelector.SelectedIndex = selectedIndex;
        _partSelector.EndUpdate();

        _partSelectorDocument = document;
        _partSelectorSignature = signature;
        _generatePartButton.Enabled = selectedIndex >= 0 && _writeThread == null;
      }
      catch (Exception ex)
      {
        GRBLMachinePlugin.Log("Unable to refresh CamBam part list: " + ex.Message);
      }
      finally
      {
        _refreshingPartSelector = false;
      }
    }

    private void GeneratePartButton_Click(object sender, EventArgs e)
    {
      PartChoice choice = _partSelector.SelectedItem as PartChoice;
      CamBamUI ui = CamBamUI.MainUI;
      if (choice == null || ui == null || ui.ActiveView == null || _writeThread != null)
        return;

      try
      {
        CADFile document = ui.ActiveView.CADFile;
        if (!Object.ReferenceEquals(document, _partSelectorDocument) || !Object.ReferenceEquals(choice.Part.CADFile, document))
        {
          RefreshPartSelector(true);
          MessageBox.Show("The active CamBam drawing changed. Select the part again and retry.", "Rotary Axis Control", MessageBoxButtons.OK, MessageBoxIcon.Information);
          return;
        }

        string outputFile = CAMUtils.GetGCodeFilename(choice.Part);
        if (String.IsNullOrEmpty(outputFile))
          return;

        _generatePartButton.Enabled = false;
        Cursor previousCursor = Cursor.Current;
        object defaults = CamBamConfig.Defaults;
        System.Reflection.PropertyInfo promptModeProperty = defaults.GetType().GetProperty("OutFilePrompt");
        object originalPromptMode = promptModeProperty == null ? null : promptModeProperty.GetValue(defaults, null);
        string generatedFile = null;
        // The CamBam API chooses a filename from the part settings (and may ask
        // once if the path is missing). Retain that chosen path on the part, then
        // suppress its second overwrite prompt during the actual generation call.
        try
        {
          Cursor.Current = Cursors.WaitCursor;
          choice.Part.OutFile = outputFile;
          generatedFile = FileUtils.GetFullPath(document, outputFile);
          if (promptModeProperty != null)
            promptModeProperty.SetValue(defaults, Enum.Parse(promptModeProperty.PropertyType, "Never"), null);
          // GenerateGCodeOutput handles the selected part's toolpath rebuild.
          CAMUtils.GenerateGCodeOutput(ui.ActiveView, choice.Part, null);
        }
        finally
        {
          if (promptModeProperty != null && originalPromptMode != null)
            promptModeProperty.SetValue(defaults, originalPromptMode, null);
          Cursor.Current = previousCursor;
        }

        if (!String.IsNullOrEmpty(generatedFile) && File.Exists(generatedFile))
        {
          bool filePathChanged = !String.Equals(FileName.Text, generatedFile, StringComparison.OrdinalIgnoreCase);
          _assigningPartOutput = true;
          try { FileName.Text = generatedFile; }
          finally { _assigningPartOutput = false; }
          _partOutputFile = generatedFile;
          if (!filePathChanged && NCFileSelected != null)
            NCFileSelected(generatedFile);
          GRBLMachinePlugin.Log("Generated G-code for part '" + choice.Part.Name + "': " + generatedFile);
        }
        else
        {
          MessageBox.Show("CamBam did not create a G-code file for this part. Check the part's OutFile setting and the drawing's post processor.", "Rotary Axis Control", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
      }
      catch (Exception ex)
      {
        MessageBox.Show("Could not generate G-code for the selected part.\r\n\r\n" + ex.Message, "Rotary Axis Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
      finally
      {
        _generatePartButton.Enabled = _partSelector.SelectedItem is PartChoice && _writeThread == null;
      }
    }

    private void UpdateSimulatorSpeedControls()
    {
      bool visible = ConnectionExpander.SimulatorActive;
      SetExpandedHeight(visible ? 186 : 154);
      if (_simSpeedLabel != null)
        _simSpeedLabel.Visible = visible;

      if (_simSpeedButtons == null)
        return;

      int selectedSpeed = ConnectionExpander.SimulatorPlaybackSpeed;
      for (int i = 0; i < _simSpeedButtons.Length; i++)
      {
        Button button = _simSpeedButtons[i];
        int speed = Convert.ToInt32(button.Text.TrimEnd('x'));
        button.Visible = visible;
        button.BackColor = speed == selectedSpeed ? Color.FromArgb(190, 220, 242) : Color.FromArgb(238, 244, 249);
        button.FlatAppearance.BorderColor = speed == selectedSpeed ? Color.FromArgb(42, 112, 165) : Color.FromArgb(157, 174, 188);
        button.FlatAppearance.BorderSize = speed == selectedSpeed ? 2 : 1;
      }
    }

    private static void ApplyModernStyle(Button button, Color backColor)
    {
      button.FlatStyle = FlatStyle.Flat;
      button.FlatAppearance.BorderSize = 1;
      button.FlatAppearance.BorderColor = Color.FromArgb(145, 162, 176);
      button.FlatAppearance.MouseOverBackColor = Color.FromArgb(222, 237, 248);
      button.FlatAppearance.MouseDownBackColor = Color.FromArgb(202, 224, 241);
      button.BackColor = backColor;
      button.ForeColor = Color.FromArgb(31, 55, 73);
      button.UseVisualStyleBackColor = false;
      button.Cursor = Cursors.Hand;
    }

    private static void UseModernButtonText(GRBLButton button, string text, float fontSize)
    {
      button.BackgroundImage = null;
      button.BackgroundImageDisabled = null;
      button.BackgroundImageChecked = null;
      button.Text = text;
      button.Font = new Font("Segoe UI Symbol", fontSize, FontStyle.Bold);
    }

    private void ToolChangeStart()
    {
      _toolChangingEvent.Reset();

      InvokeOnUI(() =>
      {
        _toolChanging = true;

        EnableButtons();

        if (ToolChangeStarted!= null)
          ToolChangeStarted();
      });
    }

    private void ToolChangeEnd()
    {
      InvokeOnUI(() =>
      {
        _toolChanging = false;

        if (ToolChangeEnded!= null)
          ToolChangeEnded();

        EnableButtons();
      });

      _toolChangingEvent.Set();
    }

    private void ToolChange(ToolDefinition tooldef)
    {
      if (ToolChanged != null)
        ToolChanged(tooldef);
    }

    private bool CanPlay()
    {
      return  _connected                          && 
             !string.IsNullOrEmpty(FileName.Text) && 
              File.Exists(FileName.Text)          && 
              _writeThread == null                && 
             !_toolChanging;
    }

    private bool CanPause()
    {
      return  _connected           && 
              _writeThread != null && 
             !_toolChanging;
    }

    private bool CanStop()
    {
      return  _connected           && 
              _writeThread != null && 
             ! PauseButton.Checked &&
             !_toolChanging;
    }

    private bool CanToolChange()
    {
      return !ConnectionExpander.SimulatorActive &&
              _connected    &&
             !_toolChanging;
    }

    private void WaitIdle()
    {
      while (!ConnectionExpander.IsCOMPortIdle)
        Thread.Sleep(100);

      _lastState = GRBLMachinePlugin.MachineState.Jog;

      while (_lastState != GRBLMachinePlugin.MachineState.Idle)
        Thread.Sleep(100);
    }

    private void WaitHold()
    {
      _lastState = GRBLMachinePlugin.MachineState.Jog;

      while (_lastState != GRBLMachinePlugin.MachineState.Hold)
        Thread.Sleep(100);
    }

    private void Message(string message)
    {
      string m = message + Path.GetFileName(_sendingFileName);
      GRBLMachinePlugin.Log          ("\n" + m);
      ThisApplication  .AddLogMessage(       m);
    }

    private void JogToToolChangePosition()
    {
      GRBLProps props = GRBLMachinePlugin.Props;

      WaitIdle();

      _preChangeX     = _lastX.GetWorkOrMachinePosition(_lastType, _WCO_X, PosType.WorkCoordinates, props.ReportInInches, props.ReportInInches);
      _preChangeY     = _lastY.GetWorkOrMachinePosition(_lastType, _WCO_Y, PosType.WorkCoordinates, props.ReportInInches, props.ReportInInches);
      _preChangeZ     = _lastZ.GetWorkOrMachinePosition(_lastType, _WCO_Z, PosType.WorkCoordinates, props.ReportInInches, props.ReportInInches);
      _preChangeSpeed = _lastSpeed;
      _preChangeType  = _lastType;

      ConnectionExpander.WriteCOMPort(_jogLeadIn + "G53 " + (props.ToolChangeUnits == InchMM.Inches ? "G20" : "G21") + " F4000 G90 Z" + props.ToolChangeZ.ToString(EN_US));
      WaitIdle();
      ConnectionExpander.WriteCOMPort("S0");
      ConnectionExpander.WriteCOMPort(_jogLeadIn + "G53 " + (props.ToolChangeUnits == InchMM.Inches ? "G20" : "G21") + " F4000 G90 X" + props.ToolChangeX.ToString(EN_US) + " Y" + props.ToolChangeY.ToString(EN_US));
    }

    private void JogFromToolChangePosition()
    {
      GRBLProps props = GRBLMachinePlugin.Props;

      if (_lastZ != props.ToolChangeZ)
      {
        WaitIdle();
        ConnectionExpander.WriteCOMPort(_jogLeadIn + "G53 " + (props.ToolChangeUnits == InchMM.Inches ? "G20" : "G21") + " F4000 G90 Z" + props.ToolChangeZ.ToString(EN_US));
      }

      WaitIdle();
      ConnectionExpander.WriteCOMPort(_jogLeadIn + (GRBLMachinePlugin.Props.ReportInInches == InchMM.Inches ? "G20" : "G21") + " F4000 G90 X" + _preChangeX.ToString(EN_US) + " Y" + _preChangeY.ToString(EN_US));
      WaitIdle();
      ConnectionExpander.WriteCOMPort("S" + _preChangeSpeed);
      WaitIdle();
      ConnectionExpander.WriteCOMPort(_jogLeadIn + (props.ReportInInches == InchMM.Inches ? "G20" : "G21") + " F" + 4000d                     .GetUnitsFromReportAndDrawing(InchMM.Millimeters,   props.ReportInInches).ToString("0.0000",EN_US) + " G90 Z" + (_preChangeZ + props.ToolChangePlungeDistance.GetUnitsFromReportAndDrawing(props.ToolChangeUnits,props.ReportInInches)).ToString("0.0000",EN_US));
      ConnectionExpander.WriteCOMPort(_jogLeadIn + (props.ReportInInches == InchMM.Inches ? "G20" : "G21") + " F" + props.ToolChangePlungeFeed.GetUnitsFromReportAndDrawing(props.ToolChangeUnits,props.ReportInInches).ToString("0.0000",EN_US) + " G90 Z" + (_preChangeZ.ToString("0.0000",EN_US)));

      WaitIdle();
    }

    private void DoToolChange(object line)
    {
      Message("ToolChange start... ");

      SystemSounds.Asterisk.Play();

      ToolChangeStart();

      JogToToolChangePosition();

      _toolChangerEvent.Reset();

      InvokeOnUI(() => 
      {
        ToolChanger toolChanger = new ToolChanger();

        toolChanger.SetToolFromGCODE(line as string);

        toolChanger.FormClosed += ToolChanger_FormClosed;
        toolChanger.Applied    += ToolChanger_Applied;

        SystemSounds.Asterisk.Play();

        Message("Change tool: " + line + " from ");

        toolChanger.Show(ThisApplication.TopWindow);
      });

      _toolChangerEvent.WaitOne();

      JogFromToolChangePosition();

      ToolChangeEnd();

      Message("ToolChange done... ");
    }

    private void WriteThread()
    {
      FileStream   fs = null;
      StreamReader sr = null; 
            
      try
      {
        JobActive = true;
        if (JobStarted != null)
          InvokeOnUI(() => { JobStarted(); });

        string s;
        int    i;

        fs = new FileStream  (_sendingFileName, FileMode.Open, FileAccess.Read, FileShare.Read);
        sr = new StreamReader(fs);

        Message("Parsing: ");

        while ((s = sr.ReadLine()) != null)
        {
          _pauseEvent       .WaitOne();

          GRBLMachinePlugin.Log("---" + s,true);

          bool removeComments = true;

          foreach (string nrc in _noRemoveCommentGCODEs)
          {
            if (s.ToUpper().StartsWith(nrc))
            {
              removeComments = false;
              break;
            }
          }

          if (removeComments)
          {
            StringBuilder line     = new StringBuilder();
            string[]      comments = s.Split('(',')',';','%');

            for (i = 1; i < comments.Length; i++)
              line.Append(comments[i]);

            if (line.Length != 0)
              GRBLMachinePlugin.Log("// " + line.ToString());

            if (comments.Length != 0)
            {
              line = new StringBuilder();

              string[] packed = comments[0].Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

              foreach (string p in packed)
              {
                if (_unsupportedGCODEs.Contains(p))
                  GRBLMachinePlugin.Log("!! Unsuppported GCODE '" + p + "' skipped");
                else
                  line.Append(p);
              }

              if (line.Length != 0)
                _fileData.AddLast(line.ToString());
            }
          }
          else
            _fileData.AddLast(s);

        }

        sr.Close();
        sr = null;

        fs.Close();
        fs = null;

        InvokeOnUI(() => 
        {
          LinesTotal.Text = _fileData.Count.ToString();
          LinesSent .Text = "0";
        });

        Message("Sending: ");

        i = 0;

        _lastState = GRBLMachinePlugin.MachineState.Jog;

        WaitIdle();
        if (ConnectionExpander.SimulatorActive)
          ConnectionExpander.BeginSimulatorJob(_fileData.Count);

        foreach (string line in _fileData)
        {
          _pauseEvent       .WaitOne();
          _toolChangingEvent.WaitOne();

          if (ConnectionExpander.SimulatorActive && line.ToUpper().Contains("M6"))
            GRBLMachinePlugin.Log("Simulator skipped tool change: " + line);
          else if (line.ToUpper().Contains("M6"))
            switch (GRBLMachinePlugin.Props.ToolChangeProcess)
            {
              case IgnoreProcessPassOn.Ignore:                                         break;
              case IgnoreProcessPassOn.Process: DoToolChange(line);                    break;
              case IgnoreProcessPassOn.PassOn:  ConnectionExpander.WriteJobGCode(line); break;
            }
          else
            ConnectionExpander.WriteJobGCode(line);

          InvokeOnUI(() => 
          {
            GRBLMachinePlugin.Log("=== " + line, true);

            LinesSent.Text = (++i).ToString();
          });
        }

        if (ConnectionExpander.SimulatorActive)
          ConnectionExpander.WaitForSimulatorJobToFinish();
      }
      catch (ThreadAbortException)       { GRBLMachinePlugin.Log("\naborted"); ConnectionExpander.WriteCOMPort((char)0x18); }
      catch (ThreadInterruptedException) { GRBLMachinePlugin.Log("\nstopped"); ConnectionExpander.WriteCOMPort((char)0x18); }
      catch (Exception ex)
      {
        InvokeOnUI(() => { MessageBox.Show(ex.ToString()); });
      }
      finally
      {
        if (sr != null)
          sr.Close();

        if (fs != null)
          fs.Close();

        ConnectionExpander.WriteCOMPort("M5");

        _writeThread = null;

        InvokeOnUI(() => 
        {
          JobActive = false;
          SystemSounds.Asterisk.Play();

          Message("Done:");

          EnableButtons();

          FileName        .Enabled = true;
          BrowseButton    .Enabled = true;
          _partSelector   .Enabled = true;
          RefreshPartSelector(false);
          _generatePartButton.Enabled = _partSelector.SelectedItem is PartChoice;

          _pauseEvent       .Set();
          _toolChangerEvent .Set();
          _toolChangingEvent.Set();

          if (JobStopped != null)
            JobStopped();

          if (!ToolChangeButton.Enabled)
            ToolChangeEnd();
        });
      }
    }

    #endregion // Privates

#region Eventhandlers


    private void ToolChanger_Applied(ToolChanger toolChanger, ToolDefinition tooldef)
    {
      ToolChange(toolChanger.SelectedToolDefinition);
    }

    private void ToolChanger_FormClosed(object sender, FormClosedEventArgs e)
    {
      ToolChanger toolChanger = sender as ToolChanger;

      toolChanger.FormClosed -= ToolChanger_FormClosed;
      toolChanger.Applied    -= ToolChanger_Applied;

      if (toolChanger.DialogResult == DialogResult.OK)
        ToolChange(toolChanger.SelectedToolDefinition);

      _toolChangerEvent.Set();

      toolChanger.Dispose();
    }

    private void TopWindow_FormClosing(object sender, FormClosingEventArgs e)
    {
      if (_writeThread != null)
        _writeThread.Interrupt();
    }

    private void GRBLMachinePlugin_State(GRBLMachinePlugin.MachineState state, int code)
    {
      _lastState = state;

      if (_jogLeadIn == null)
        _jogLeadIn = ConnectionExpander.GrblIsV1 ? V1JOG : string.Empty;
    }

    private void GRBLMachinePlugin_Speed(double feed, int rpm)
    {
      _lastSpeed = rpm;
    }

    private void GRBLMachinePlugin_Position(PosType type, double x, double y, double z)
    {
      if      (type == PosType.MachineCoordinates || 
               type == PosType.WorkCoordinates    )
      {
        _lastType = type;

        if (!double.IsNaN(x))
          _lastX = x;
        if (!double.IsNaN(y))
          _lastY = y;
        if (!double.IsNaN(z))
          _lastZ = z;
      }
      else if (type == PosType.WCO)
      {
        if (!double.IsNaN(x))
          _WCO_X = x;
        if (!double.IsNaN(y))
          _WCO_Y = y;
        if (!double.IsNaN(z))
          _WCO_Z = z;
      }

    }

    private void GRBLMachinePlugin_Responding(GRBLMachinePlugin.Response response, int code)
    {
      if ((response == GRBLMachinePlugin.Response.error || response == GRBLMachinePlugin.Response.alarm ) && _writeThread != null)
        _writeThread.Interrupt();
    }

    private void ConnectionExpander_Disconnected()
    {
      _connected               = false;

      UpdateSimulatorSpeedControls();

      EnableButtons();

      FileName        .Enabled = true;
      BrowseButton    .Enabled = true;
    }

    private void ConnectionExpander_Connected()
    {
      _connected               = true;

      UpdateSimulatorSpeedControls();

      EnableButtons();

      FileName        .Enabled = true;
      BrowseButton    .Enabled = true;
    }

    private void Application_Idle(object sender, EventArgs e)
    {
      try
      {
        CamBamUI ui = CamBamUI.MainUI;
        RefreshPartSelector(false);

        if (_writeThread == null)
        {
          // See if there is any outfile in the MachiningOptions
          if (!String.IsNullOrEmpty(_partOutputFile) && File.Exists(_partOutputFile))
          {
            if (!String.Equals(FileName.Text, _partOutputFile, StringComparison.OrdinalIgnoreCase))
            {
              _assigningPartOutput = true;
              try { FileName.Text = _partOutputFile; }
              finally { _assigningPartOutput = false; }
            }
          }
          else if      (!string.IsNullOrEmpty(ui.CADFileTree.CADFile.MachiningOptions.OutFile))
          {
            if (string.IsNullOrEmpty(FileName.Text) || !ui.CADFileTree.CADFile.MachiningOptions.OutFile.Equals(_lastOutfile))
            {
              FileName.Text = FileUtils.GetFullPath(ui.CADFileTree.CADFile,ui.CADFileTree.CADFile.MachiningOptions.OutFile);

              ToolChange(ui.CADFileTree.CADFile.ActiveTool());

              _lastOutfile = ui.CADFileTree.CADFile.MachiningOptions.OutFile;
            }
          }
          else
          {
            // try to find a MOPNCFile, which has it's outfile not in 'Outfile' :(
            foreach (CAMPart part in ui.CADFileTree.CADFile.Parts)
            {
              if (part.MachineOps != null)
              { 
                bool found = false;

                foreach (MachineOp mop in part.MachineOps)
                {
                  if (mop is MOPNCFile)
                  {
                    MOPNCFile mopnc = mop as MOPNCFile;

                    if (!string.IsNullOrEmpty(mopnc.SourceFile))
                    {
                      if (string.IsNullOrEmpty(FileName.Text) || !mopnc.SourceFile.Equals(_lastOutfile))
                        FileName.Text = FileUtils.GetFullPath(ui.CADFileTree.CADFile,mopnc.SourceFile);

                      _lastOutfile = mopnc.SourceFile;
                      found       = true;
                      break;
                    }
                  }

                  if (found)
                    break;
                }
              }
            }
          }
        }
      }
      catch (Exception) { }

      if (!PlayButton.Enabled && CanPlay())
        PlayButton.Enabled = true;
    }

    private void Browse_Click(object sender, EventArgs e)
    {
      OpenFileDialog ofd   = new OpenFileDialog();

      ofd.Filter           = "GCODE file|*.nc";
      ofd.FileName         = Path.GetFileName     (FileName.Text);
      try {      
      ofd.InitialDirectory = Path.GetDirectoryName(FileName.Text);
      }
      catch (Exception) { }
      ofd.RestoreDirectory = true;
      ofd.CheckFileExists  = true;
      ofd.DefaultExt       = ".nc";
      
      if (ofd.ShowDialog(this) == DialogResult.OK)
        FileName.Text = ofd.FileName;
    }

    private void FileName_TextChanged(object sender, EventArgs e)
    {
      if (!_assigningPartOutput && !String.Equals(FileName.Text, _partOutputFile, StringComparison.OrdinalIgnoreCase))
        _partOutputFile = null;
      PlayButton.Enabled = CanPlay();
      if (NCFileSelected != null)
        NCFileSelected(File.Exists(FileName.Text) ? FileName.Text : null);
    }

    private void PlayButton_Click(object sender, EventArgs e)
    {
      if (!CheckState())
        return;

      if (GRBLMachinePlugin.Props.ToolChangeProcess == IgnoreProcessPassOn.Process)
      {
        string post = CamBamUI.MainUI.ActiveView.CADFile.MachiningOptions.PostProcessor;
         
        if (!string.IsNullOrEmpty(post) && post != "GRBLMachine")
          switch (MessageBox.Show("The Post Processor in Machining Options is not 'GRBLMachine'.\r\n\r\nSet Post Processor to 'GRBLMachine' and regenerate GCODE ?","GRBLMachine - Play",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question))
          {
            case DialogResult.Yes:
              CamBamUI.MainUI.ActiveView.CADFile.MachiningOptions.PostProcessor = "GRBLMachine";
              CAMUtils.GenerateGCodeOutput(CamBamUI.MainUI.ActiveView);
              break;
            case DialogResult.Cancel:
              return;
            case DialogResult.No:
              break;
          }
      }

      if (GRBLMachinePlugin.Props.TrackMachine == EnabledDisabled.Enabled)
      {
        CamBamUI ui = CamBamUI.MainUI;

        // turn toolpaths et al on
        ui.ActiveView.CADFile.ShowToolpaths       = true;
        ui.ActiveView.CADFile.ShowRapids          = true;
        ui.ActiveView.CADFile.ShowDirectionVector = true;
        ui.ViewContextMenus.RefreshCheckedMenus();

        // runs in background, so just start it, we'll do something else in the meanwhile
        CAMUtils.GenerateToolpaths(ui.ActiveView);

        // wait until GenerateToolpaths is ready
        while (ui.ActiveView.IsThinking)
          Application.DoEvents();
      }

      _sendingFileName          = FileName.Text;
      _writeThread              = new Thread(WriteThread);
      _writeThread.Name         = "GCODE-SenderThread";
      _writeThread.IsBackground = true;

      PauseButton .Checked      = false;

      EnableButtons();

      FileName    .Enabled      = false;
      BrowseButton.Enabled      = false;
      _partSelector.Enabled     = false;
      _generatePartButton.Enabled = false;
      LinesSent   .Text         = LinesTotal.Text = "-";

      _pauseEvent .Set();
      _fileData   .Clear();

      _writeThread.Start();
    }

    private void StopButton_Click(object sender, EventArgs e)
    {
      if (_writeThread != null)
        _writeThread.Interrupt();
      else
      {
        EnableButtons();

        PauseButton     .Checked = false;
        FileName        .Enabled = true;
        BrowseButton    .Enabled = true;
          _partSelector   .Enabled = true;
          _generatePartButton.Enabled = _partSelector.SelectedItem is PartChoice;
      }
      _pauseEvent     .Set();
    }

    private void PauseButton_Click(object sender, EventArgs e)
    {
      if (PauseButton.Checked)
      {
        _pauseEvent.Reset();

        ConnectionExpander.WriteCOMPort('!');

        LinesTotal.Text = _fileData.Count.ToString();

        Message("Pausing... ");

        if (JobPaused != null)
          JobPaused();

        EnableButtons();
      }
      else
      {
        Message("Resuming... ");

        ConnectionExpander.WriteCOMPort('~');

        _pauseEvent.Set();

        if (JobResumed!= null)
          JobResumed();

        EnableButtons();
      }
    }

    private void ToolChangeButton_Click(object sender, EventArgs e)
    {
      if (ConnectionExpander.SimulatorActive)
      {
        MessageBox.Show("Tool changes are disabled in the simulator.", "Controller Simulator", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }

      if (!CheckState())
        return;

      ToolChangeButton.Enabled = false;
      new Thread(DoToolChange) { Name = "ToolChangeThread", IsBackground = true }.Start("T0 M6 (NoPart\\GRBLMachine Default)");
    }

    #endregion // Eventhandlers

  }
}
