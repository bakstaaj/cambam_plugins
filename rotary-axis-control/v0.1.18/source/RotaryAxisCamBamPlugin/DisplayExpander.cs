using CamBam.UI;
using CamBam.Extensions;
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GRBLMachine.Utils;
using GRBLMachine;
using System.Media;
using System.Globalization;

namespace GRBLMachine.UI
{
  public partial class DisplayExpander : Expander
  {
    private double                         _WCO_x;
    private double                         _WCO_y;
    private double                         _WCO_z;

    private double                         _last_x;
    private double                         _last_y;
    private double                         _last_z;
    private readonly double[]              _machineRotary = new double[3];
    private readonly double[]              _workRotary = new double[3];
    private readonly double[]              _rotaryOffset = new double[3];
    private readonly bool[]                _rotaryAvailable = new bool[3];
    private readonly Label[]               _rotaryAxisLabels = new Label[3];
    private readonly Label[]               _rotaryPositionLabels = new Label[3];

    private GRBLMachinePlugin.MachineState _lastState;
    private int                            _lastCode;

    private PosType                        _lastPosType;

    private bool                           _connected;

    private ToolTraceEditmode              _tracer;

    public DisplayExpander()
    {
      InitializeComponent();

      UiTheme.Apply(this);
      ContentPanel.BackgroundImage = null;
      ContentPanel.BackColor = Color.FromArgb(31, 41, 55);
      ContentPanel.ForeColor = Color.FromArgb(230, 237, 243);
      X_Axis.ForeColor = Y_Axis.ForeColor = Z_Axis.ForeColor = Color.FromArgb(193, 205, 216);
      X_Pos.ForeColor = Y_Pos.ForeColor = Z_Pos.ForeColor = Color.FromArgb(139, 233, 193);
      FeedLabel.ForeColor = RPMLabel.ForeColor = Color.FromArgb(139, 233, 193);
      FeedUnits.ForeColor = RPMUnits.ForeColor = Coordinates.ForeColor = Color.FromArgb(193, 205, 216);
      StatusLabel.ForeColor = Color.FromArgb(139, 233, 193);

      ContentPanel      .Font           = LCDFont.Load(20.0f);

      for (int axisIndex = 0; axisIndex < 3; axisIndex++)
      {
        Label axisLabel = new Label();
        axisLabel.AutoSize = true;
        axisLabel.BackColor = Color.Transparent;
        axisLabel.Font = new Font("Arial Rounded MT Bold", 23F, FontStyle.Regular);
        axisLabel.Location = new Point(7, 119);
        axisLabel.Text = ((char)('A' + axisIndex)).ToString();
        axisLabel.Visible = false;
        axisLabel.ForeColor = Color.FromArgb(193, 205, 216);
        _rotaryAxisLabels[axisIndex] = axisLabel;
        ContentPanel.Controls.Add(axisLabel);

        Label positionLabel = new Label();
        positionLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        positionLabel.BackColor = Color.Transparent;
        positionLabel.Font = ContentPanel.Font;
        positionLabel.Location = new Point(80, 124);
        positionLabel.Size = new Size(278, 40);
        positionLabel.Text = "----.---°";
        positionLabel.TextAlign = ContentAlignment.TopRight;
        positionLabel.Visible = false;
        positionLabel.ForeColor = Color.FromArgb(139, 233, 193);
        _rotaryPositionLabels[axisIndex] = positionLabel;
        ContentPanel.Controls.Add(positionLabel);
      }

      FeedLabel         .Font           = LCDFont.Load(10.0f);
      RPMLabel          .Font           = LCDFont.Load(10.0f);
      StatusLabel       .Font           = LCDFont.Load(22.0f);

      GRBLMachinePlugin.Position        += GRBLMachinePlugin_Position;
      GRBLMachinePlugin.RotaryPositionForAxis += GRBLMachinePlugin_RotaryPositionForAxis;
      GRBLMachinePlugin.RotaryAxisAvailabilityChangedForAxis += GRBLMachinePlugin_RotaryAxisAvailabilityChangedForAxis;
      GRBLMachinePlugin.State           += GRBLMachinePlugin_State;
      GRBLMachinePlugin.Speed           += GRBLMachinePlugin_Speed;
      GRBLMachinePlugin.PropertyChanged += GRBLMachinePlugin_PropertyChanged;
      GRBLMachinePlugin.Responding      += GRBLMachinePlugin_Responding;
      ProductionExpander.NCFileSelected += ProductionExpander_NCFileSelected;


      ConnectionExpander.Connected      += ConnectionExpander_Connected;
      ConnectionExpander.Disconnected   += ConnectionExpander_Disconnected;

      if (!DesignMode)
      {
        if (CamBamUI.MainUI != null && CamBamUI.MainUI.SysTabs != null)
          CamBamUI.MainUI.SysTabs.Tabs().SelectedIndexChanged += DisplayExpander_SelectedIndexChanged;

        GRBLMachinePlugin_State(GRBLMachinePlugin.MachineState.Idle,0);
        for (int axisIndex = 0; axisIndex < 3; axisIndex++)
          GRBLMachinePlugin_RotaryAxisAvailabilityChangedForAxis((RotaryAxisLetter)axisIndex, GRBLMachinePlugin.IsRotaryAxisAvailable((RotaryAxisLetter)axisIndex));
        SetCoordinates         (GRBLMachinePlugin.Props.DisplayPosType);
        SetFeedUnits           (GRBLMachinePlugin.Props.ReportInInches);

  #if DEBUG
        GRBLMachinePlugin_Position(PosType.WCO,               -10, -20, -30);
        GRBLMachinePlugin_Position(PosType.MachineCoordinates,  5,   6,   7);
  #endif
        if (CamBamUI.MainUI != null && CamBamUI.MainUI.ActiveView != null)
          _tracer = new ToolTraceEditmode(CamBamUI.MainUI.ActiveView);
      }
    }

#region Privates

    private string GetPositionString(PosType type, double pos, double offset)
    {
      double p = pos.GetWorkOrMachinePosition(type, offset, GRBLMachinePlugin.Props.DisplayPosType);

      return (p > 0 ? "+" : "") + p.ToString("0. 000");
    }

    private void SetCoordinates(PosType pos)
    {
      switch (pos)
      {
        case PosType.MachineCoordinates:
          WPos       .Checked        = false;
          WPos       .Enabled        = true;
          MPos       .Checked        = true;
          MPos       .Enabled        = false;
          Coordinates.Text           = "MPos";
          break;
        case PosType.WorkCoordinates:
          MPos       .Checked        = false;
          MPos       .Enabled        = true;
          WPos       .Checked        = true;
          WPos       .Enabled        = false;
          Coordinates.Text           = "WPos";
          break;
      }

      GRBLMachinePlugin_Position(_lastPosType, _last_x, _last_y, _last_z);
      UpdateAllRotaryPositions();
    }

    private void SetFeedUnits(InchMM unit)
    {
      switch (unit)
      {
        case InchMM.Millimeters: 
          FeedUnitInches     .Checked = false;
          FeedUnitInches     .Enabled = true;
          FeedUnitMillimeters.Checked = true;
          FeedUnitMillimeters.Enabled = false;
          FeedUnits          .Text    = "mm/min";
          break;
        case InchMM.Inches:
          FeedUnitMillimeters.Checked = false;
          FeedUnitMillimeters.Enabled = true;
          FeedUnitInches     .Checked = true;
          FeedUnitInches     .Enabled = false;
          FeedUnits          .Text    = "Inch/min";
          break;
        default:
          FeedUnitMillimeters.Checked = false;
          FeedUnitMillimeters.Enabled = true;
          FeedUnitInches     .Checked = false;
          FeedUnitInches     .Enabled = true;
          FeedUnits          .Text    = "???/min";
          break;
      }
    }

#endregion // Privates

#region Eventhandlers

    private void GRBLMachinePlugin_PropertyChanged(string name, object newvalue)
    {
      switch (name)
      {
        case "DisplayPosType":      SetCoordinates((PosType)newvalue);                      break;
        case "ReportInInches":      SetFeedUnits  ((InchMM) newvalue);                      break;
        case "TrackMachine":        CamBam.UI.CamBamUI.MainUI.ActiveView.RepaintEditMode(); break;
      }
    }

    private void GRBLMachinePlugin_Responding(GRBLMachinePlugin.Response response, int code)
    {
      switch (response)
      {
        case GRBLMachinePlugin.Response.alarm: 
          SystemSounds.Hand.Play();
          if (code > 0)
            AlarmTip.Show(ConnectionExpander.AlarmDict[code],this,0,     0,10000);
          break;
        case GRBLMachinePlugin.Response.error: 
          SystemSounds.Beep.Play();
          if (code > 0)
            ErrorTip.Show(ConnectionExpander.ErrorDict[code],this,0,Height,10000);
          break;
      }
    }

    private void ConnectionExpander_Connected()
    {
      _connected = true;

      X_AxisContextMenu   .Enabled   = true;
      Z_AxisContextMenu   .Enabled   = true;
      Y_AxisContextMenu   .Enabled   = true;
      FeedUnitsContextMenu.Enabled   = true;

      ContentPanel        .ForeColor = Color.Black;
    }

    private void ConnectionExpander_Disconnected()
    {
      _connected     = false;

      ContentPanel        .ForeColor = Color.Olive;

      X_AxisContextMenu   .Enabled   = false;
      Z_AxisContextMenu   .Enabled   = false;
      Y_AxisContextMenu   .Enabled   = false;
      FeedUnitsContextMenu.Enabled   = false;

      X_Pos    .Text = Y_Pos.Text = Z_Pos.Text = "----. ---";
      FeedLabel.Text =                                "----";
      RPMLabel .Text =                               "-----";
      for (int axisIndex = 0; axisIndex < 3; axisIndex++)
        _rotaryPositionLabels[axisIndex].Text = "----.---°";
    }

    private void GRBLMachinePlugin_Speed(double feed, int rpm)
    {
      FeedLabel.Text = feed.ToString();
      RPMLabel .Text = rpm .ToString();
    }

    private void GRBLMachinePlugin_State(GRBLMachinePlugin.MachineState state, int code)
    {
      if (_lastState != GRBLMachinePlugin.MachineState.Hold  && state == GRBLMachinePlugin.MachineState.Hold)
        System.Media.SystemSounds.Asterisk.Play();

      _lastState           = state;
      _lastCode            = code;

      StatusLabel.Text     = state.ToString().ToUpper();
      if (state == GRBLMachinePlugin.MachineState.Idle ||
          state == GRBLMachinePlugin.MachineState.Run ||
          state == GRBLMachinePlugin.MachineState.Hold)
        StatusLabel.ForeColor = Color.FromArgb(139, 233, 193);
      else if (state == GRBLMachinePlugin.MachineState.Alarm)
        StatusLabel.ForeColor = Color.FromArgb(255, 116, 108);
      else
        StatusLabel.ForeColor = Color.FromArgb(255, 211, 130);

      Hold  .Enabled       = state != GRBLMachinePlugin.MachineState.Hold;
      Resume.Enabled       = state == GRBLMachinePlugin.MachineState.Hold;
      Alarm .Enabled       = state == GRBLMachinePlugin.MachineState.Alarm;

      X_OriginZero.Enabled = Y_OriginZero.Enabled = Z_OriginZero.Enabled
                           = state == GRBLMachinePlugin.MachineState.Idle;

      if (state == GRBLMachinePlugin.MachineState.Idle || state == GRBLMachinePlugin.MachineState.Jog)
      {
        X_AxisContextMenu   .Enabled = true;
        Z_AxisContextMenu   .Enabled = true;
        Y_AxisContextMenu   .Enabled = true;
        FeedUnitsContextMenu.Enabled = true;
      }
      else
      {
        X_AxisContextMenu   .Enabled = false;
        Z_AxisContextMenu   .Enabled = false;
        Y_AxisContextMenu   .Enabled = false;
        FeedUnitsContextMenu.Enabled = false;
      }
    }

    private void GRBLMachinePlugin_Position(PosType type, double x, double y, double z)
    {
      if      (type == PosType.MachineCoordinates || 
               type == PosType.WorkCoordinates    )
      {
        _lastPosType = type;

        if (!double.IsNaN(x))
          X_Pos.Text = GetPositionString(type, _last_x = x, _WCO_x);

        if (!double.IsNaN(y))
          Y_Pos.Text = GetPositionString(type, _last_y = y, _WCO_y);

        if (!double.IsNaN(z))
          Z_Pos.Text = GetPositionString(type, _last_z = z, _WCO_z);
      }
      else if (type == PosType.WCO)
      {
        if (!double.IsNaN(x))
          _WCO_x = x;

        if (!double.IsNaN(y))
          _WCO_y = y;

        if (!double.IsNaN(z))
          _WCO_z = z;
      }
    }

    private void GRBLMachinePlugin_RotaryPositionForAxis(RotaryAxisLetter axis, PosType type, double position)
    {
      if (double.IsNaN(position))
        return;

      SetRotaryPosition((int)axis, type, position);
    }

    private void SetRotaryPosition(int axisIndex, PosType type, double position)
    {
      switch (type)
      {
        case PosType.MachineCoordinates:
          _machineRotary[axisIndex] = position;
          _workRotary[axisIndex] = _machineRotary[axisIndex] - _rotaryOffset[axisIndex];
          break;
        case PosType.WorkCoordinates:
          _workRotary[axisIndex] = position;
          _machineRotary[axisIndex] = _workRotary[axisIndex] + _rotaryOffset[axisIndex];
          break;
        case PosType.WCO:
          _rotaryOffset[axisIndex] = position;
          _workRotary[axisIndex] = _machineRotary[axisIndex] - _rotaryOffset[axisIndex];
          break;
      }

      UpdateRotaryPosition(axisIndex);
    }

    private void GRBLMachinePlugin_RotaryAxisAvailabilityChangedForAxis(RotaryAxisLetter axis, bool available)
    {
      int axisIndex = (int)axis;
      _rotaryAvailable[axisIndex] = available;
      _rotaryAxisLabels[axisIndex].Visible = available;
      _rotaryPositionLabels[axisIndex].Visible = available;
      LayoutRotaryRows();
      UpdateRotaryPosition(axisIndex);
    }

    private void UpdateRotaryPosition(int axisIndex)
    {
      if (!_rotaryAvailable[axisIndex])
        return;

      double position = GRBLMachinePlugin.Props.DisplayPosType == PosType.WorkCoordinates
        ? _workRotary[axisIndex]
        : _machineRotary[axisIndex];
      _rotaryPositionLabels[axisIndex].Text = (position > 0 ? "+" : "") + position.ToString("0.000", CultureInfo.InvariantCulture) + "°";
    }

    private void UpdateAllRotaryPositions()
    {
      for (int axisIndex = 0; axisIndex < 3; axisIndex++)
        UpdateRotaryPosition(axisIndex);
    }

    private void LayoutRotaryRows()
    {
      int row = 0;
      for (int axisIndex = 0; axisIndex < 3; axisIndex++)
      {
        if (!_rotaryAvailable[axisIndex])
          continue;

        _rotaryAxisLabels[axisIndex].Location = new Point(7, 119 + 38 * row);
        _rotaryPositionLabels[axisIndex].Location = new Point(80, 124 + 38 * row);
        row++;
      }

      int offset = 38 * row;
      StatusLabel.Location = new Point(StatusLabel.Left, 126 + offset);
      FeedLabel.Location = new Point(FeedLabel.Left, 126 + offset);
      FeedUnits.Location = new Point(FeedUnits.Left, 125 + offset);
      RPMUnits.Location = new Point(RPMUnits.Left, 146 + offset);
      RPMLabel.Location = new Point(RPMLabel.Left, 146 + offset);
      Coordinates.Location = new Point(Coordinates.Left, 146 + offset);
      ContentPanel.Size = new Size(ContentPanel.Width, 174 + offset);
      Size = new Size(Width, 210 + offset);
    }

    private void X_Axis_Click(object sender, EventArgs e)
    {
        X_AxisContextMenu.Show(Cursor.Position);
    }

    private void Y_Axis_Click(object sender, EventArgs e)
    {
        Y_AxisContextMenu.Show(Cursor.Position);

    }
    private void Z_Axis_Click(object sender, EventArgs e)
    {
        Z_AxisContextMenu.Show(Cursor.Position);
    }

    private void StatusLabel_Click(object sender, EventArgs e)
    {
        StateContextMenu.Show(Cursor.Position);
    }

    private void Hold_Click(object sender, EventArgs e)
    {
      if (_connected)
        ConnectionExpander.WriteCOMPort('!');
    }

    private void Resume_Click(object sender, EventArgs e)
    {
      if (_connected)
        ConnectionExpander.WriteCOMPort('~');
    }

    private void ResetXMenu_Click(object sender, EventArgs e)
    {
      if (_connected)
        ConnectionExpander.WriteCOMPort("G10 P0 L20 X0");
    }

    private void ResetYMenu_Click(object sender, EventArgs e)
    {
      if (_connected)
        ConnectionExpander.WriteCOMPort("G10 P0 L20 Y0");
    }

    private void ResetZMenu_Click(object sender, EventArgs e)
    {
      if (_connected)
        ConnectionExpander.WriteCOMPort("G10 P0 L20 Z0");
    }

    private void Alarm_Click(object sender, EventArgs e)
    {
      if (!_connected)
        return;

      ConnectionExpander.WriteCOMPort((char)0x18);
      Thread            .Sleep(250);
      ConnectionExpander.WriteCOMPort("$X");

      if (MessageBox.Show("GRBL has been reset,\r\nperforming a Homing Cycle and/or\r\nreset your WCS origin is highly recommended !\r\n\r\nMove to WCS origin (0.0.0) now ?","GRBLMachine", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
      {
        ConnectionExpander.WriteCOMPort("G91 G21 G0 Z" + GRBLMachinePlugin.Props.JogZPullup.ToString(new CultureInfo("en-US")));
        ConnectionExpander.WriteCOMPort("G90 G0 X0 Y0");
        ConnectionExpander.WriteCOMPort("G90 G0 Z0");
      }
    }

    private void WPos_Click(object sender, EventArgs e)
    {
      if (!_connected)
        return;

      GRBLMachinePlugin.PropertyChange("DisplayPosType",GRBLMachinePlugin.Props.DisplayPosType = PosType.WorkCoordinates);
      CamBamUI.MainUI.ObjectProperties.Refresh();
    }

    private void MPos_Click(object sender, EventArgs e)
    {
      if (!_connected)
        return;

      GRBLMachinePlugin.PropertyChange("DisplayPosType",GRBLMachinePlugin.Props.DisplayPosType = PosType.MachineCoordinates);
      CamBamUI.MainUI.ObjectProperties.Refresh();
    }

    private void FeedUnitMillimeters_Click(object sender, EventArgs e)
    {
      if (!_connected)
        return;

      ConnectionExpander.WriteCOMPort("$13=0");
      GRBLMachinePlugin.PropertyChange("ReportInInches",GRBLMachinePlugin.Props.ReportInInches = InchMM.Millimeters);
      CamBamUI.MainUI.ObjectProperties.Refresh();
    }

    private void FeedUnitInches_Click(object sender, EventArgs e)
    {
      if (!_connected)
        return;

      ConnectionExpander.WriteCOMPort("$13=1");
      GRBLMachinePlugin.PropertyChange("ReportInInches",GRBLMachinePlugin.Props.ReportInInches = InchMM.Inches);
      CamBamUI.MainUI.ObjectProperties.Refresh();
    }

    private void FeedUnits_Click(object sender, EventArgs e)
    {
      FeedUnitsContextMenu.Show(Cursor.Position);
    }

    private void Coordinates_Click(object sender, EventArgs e)
    {
      CoordinateContextMenu.Show(Cursor.Position);
    }

    private void TrackTimer_Tick(object sender, EventArgs e)
    {
      if (_tracer != null &&
          ((GRBLMachinePage.IsSelected && GRBLMachinePlugin.Props.TrackMachine == EnabledDisabled.Enabled) || _tracer.HasProgramPreview))
        _tracer.Draw();
    }

    private int _previewGeneration;

    private void ProductionExpander_NCFileSelected(string fileName)
    {
      int generation = ++_previewGeneration;
      if (_tracer == null) return;
      if (String.IsNullOrEmpty(fileName))
      {
        _tracer.SetProgramPreview(null);
        return;
      }

      _tracer.SetProgramPreview(null);

      CamBam.CAD.Units drawingUnits = CamBamUI.MainUI.ActiveView.CADFile.DrawingUnits;
      Thread parser = new Thread(delegate()
      {
        try
        {
          GCodePreview preview = GCodePreview.Load(fileName, drawingUnits);
          if (IsDisposed || !IsHandleCreated) return;
          BeginInvoke((MethodInvoker)delegate
          {
            if (generation != _previewGeneration || _tracer == null) return;
            _tracer.SetProgramPreview(preview);
            GRBLMachinePlugin.Log("NC preview: " + preview.MotionCount.ToString("N0") + " moves" +
              (preview.Truncated ? " (sampled for display)" : String.Empty) +
              (preview.ArcMovesSkipped > 0 ? "; " + preview.ArcMovesSkipped.ToString("N0") + " arc moves omitted" : String.Empty) + ".", false);
          });
        }
        catch (Exception ex)
        {
          if (IsDisposed || !IsHandleCreated) return;
          BeginInvoke((MethodInvoker)delegate
          {
            if (generation == _previewGeneration)
              GRBLMachinePlugin.Log("NC preview unavailable: " + ex.Message, false);
          });
        }
      });
      parser.IsBackground = true;
      parser.Name = "NC-Preview-Parser";
      parser.Start();
    }

    private void DisplayExpander_SelectedIndexChanged(object sender, EventArgs e)
    {
      CamBamUI.MainUI.ActiveView.RefreshView();
    }

    #endregion // Eventhandlers

    private void BlinkTimer_Tick(object sender, EventArgs e)
    {
      if (_lastState == GRBLMachinePlugin.MachineState.Alarm)
        StatusLabel.ForeColor = StatusLabel.ForeColor == Color.FromArgb(255, 116, 108)
          ? Color.FromArgb(105, 52, 52)
          : Color.FromArgb(255, 116, 108);
      else if (_lastState == GRBLMachinePlugin.MachineState.Idle ||
               _lastState == GRBLMachinePlugin.MachineState.Run ||
               _lastState == GRBLMachinePlugin.MachineState.Hold)
        StatusLabel.ForeColor = Color.FromArgb(139, 233, 193);
      else
        StatusLabel.ForeColor = Color.FromArgb(255, 211, 130);
    }
  }
}
