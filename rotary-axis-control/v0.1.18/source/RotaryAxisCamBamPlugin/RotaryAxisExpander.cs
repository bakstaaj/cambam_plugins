using CamBam.UI;
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace GRBLMachine.UI
{
  public class RotaryAxisExpander : Expander
  {
    private readonly ComboBox _axis;
    private readonly Label _position;
    private readonly TextBox _jogStep;
    private readonly TextBox _target;
    private readonly Button _jogMinus;
    private readonly Button _jogPlus;
    private readonly Button _index;
    private readonly Button _zero;
    private readonly Label _clearanceNote;
    private bool _connected;
    private bool _idle;
    private bool _commandPending;
    private bool _jobActive;
    private bool _axisReported;
    private bool _updatingAxis;
    private double _machinePosition = double.NaN;
    private double _workPosition = double.NaN;
    private double _workOffset = double.NaN;

    private static readonly CultureInfo EnUs = new CultureInfo("en-US");

    public RotaryAxisExpander()
    {
      Text = "Rotary Axis";
      Size = new Size(391, 166);
      ContentPanel.Size = new Size(363, 130);

      Label axisLabel = new Label();
      axisLabel.Text = "Axis:";
      axisLabel.Location = new Point(12, 12);
      axisLabel.AutoSize = true;
      ContentPanel.Controls.Add(axisLabel);

      _axis = new ComboBox();
      _axis.DropDownStyle = ComboBoxStyle.DropDownList;
      _axis.Items.AddRange(new object[] { RotaryAxisLetter.A, RotaryAxisLetter.B, RotaryAxisLetter.C });
      _axis.Location = new Point(53, 9);
      _axis.Size = new Size(64, 24);
      _axis.SelectedIndex = (int)GRBLMachinePlugin.Props.RotaryAxis;
      _axis.SelectedIndexChanged += Axis_SelectedIndexChanged;
      ContentPanel.Controls.Add(_axis);

      _position = new Label();
      _position.Text = "Position: ----.---°";
      _position.Location = new Point(132, 12);
      _position.Size = new Size(215, 22);
      _position.TextAlign = ContentAlignment.MiddleRight;
      _position.Font = new Font("Microsoft Sans Serif", 9F, FontStyle.Bold);
      ContentPanel.Controls.Add(_position);

      Label jogLabel = new Label();
      jogLabel.Text = "Jog (degrees):";
      jogLabel.Location = new Point(12, 51);
      jogLabel.AutoSize = true;
      ContentPanel.Controls.Add(jogLabel);

      _jogStep = new TextBox();
      _jogStep.Text = GRBLMachinePlugin.Props.RotaryJogStep.ToString(EnUs);
      _jogStep.Location = new Point(105, 48);
      _jogStep.Size = new Size(55, 22);
      ContentPanel.Controls.Add(_jogStep);

      _jogMinus = new Button();
      _jogMinus.Text = "-";
      _jogMinus.Location = new Point(170, 45);
      _jogMinus.Size = new Size(42, 28);
      _jogMinus.Click += JogMinus_Click;
      ContentPanel.Controls.Add(_jogMinus);

      _jogPlus = new Button();
      _jogPlus.Text = "+";
      _jogPlus.Location = new Point(216, 45);
      _jogPlus.Size = new Size(42, 28);
      _jogPlus.Click += JogPlus_Click;
      ContentPanel.Controls.Add(_jogPlus);

      Label targetLabel = new Label();
      targetLabel.Text = "Index to (°):";
      targetLabel.Location = new Point(12, 89);
      targetLabel.AutoSize = true;
      ContentPanel.Controls.Add(targetLabel);

      _target = new TextBox();
      _target.Text = "0";
      _target.Location = new Point(105, 86);
      _target.Size = new Size(80, 22);
      ContentPanel.Controls.Add(_target);

      _index = new Button();
      _index.Text = "Index";
      _index.Location = new Point(193, 83);
      _index.Size = new Size(65, 28);
      _index.Click += Index_Click;
      ContentPanel.Controls.Add(_index);

      _zero = new Button();
      _zero.Text = "Set work zero";
      _zero.Location = new Point(270, 83);
      _zero.Size = new Size(92, 28);
      _zero.Click += Zero_Click;
      TheToolTip.SetToolTip(_zero, "Sets the selected axis work coordinate to zero. The machine coordinate stays the same; choose WPos in the top display to see the work zero.");
      ContentPanel.Controls.Add(_zero);

      _clearanceNote = new Label();
      _clearanceNote.Text = "Confirm the tool is clear before indexing. Firmware must support the selected axis.";
      _clearanceNote.Location = new Point(12, 119);
      _clearanceNote.Size = new Size(345, 30);
      _clearanceNote.ForeColor = Color.DarkRed;
      ContentPanel.Controls.Add(_clearanceNote);

      _connected = GRBLMachinePlugin.Connected;
      _idle = GRBLMachinePlugin.CurrentMachineState == GRBLMachinePlugin.MachineState.Idle;
      _axisReported = GRBLMachinePlugin.RotaryAxisAvailable;
      GRBLMachinePlugin_RotaryAxisAvailabilityChanged(_axisReported);

      ConnectionExpander.Connected += ConnectionExpander_Connected;
      ConnectionExpander.Disconnected += ConnectionExpander_Disconnected;
      ProductionExpander.JobStarted += ProductionExpander_JobStarted;
      ProductionExpander.JobStopped += ProductionExpander_JobStopped;
      GRBLMachinePlugin.State += GRBLMachinePlugin_State;
      GRBLMachinePlugin.RotaryPosition += GRBLMachinePlugin_RotaryPosition;
      GRBLMachinePlugin.RotaryAxisAvailabilityChanged += GRBLMachinePlugin_RotaryAxisAvailabilityChanged;
      GRBLMachinePlugin.Responding += GRBLMachinePlugin_Responding;
      GRBLMachinePlugin.PropertyChanged += GRBLMachinePlugin_PropertyChanged;
      UiTheme.Apply(this);
      _clearanceNote.ForeColor = Color.FromArgb(184, 105, 35);
      RefreshControls();
    }

    private char AxisLetter
    {
      get { return (char)('A' + (int)GRBLMachinePlugin.Props.RotaryAxis); }
    }

    private void Axis_SelectedIndexChanged(object sender, EventArgs e)
    {
      if (_updatingAxis || _axis.SelectedIndex < 0)
        return;

      GRBLMachinePlugin.Props.RotaryAxis = (RotaryAxisLetter)_axis.SelectedIndex;
      GRBLMachinePlugin_RotaryAxisAvailabilityChanged(false);
      _machinePosition = _workPosition = _workOffset = double.NaN;
      GRBLMachinePlugin.PropertyChange("RotaryAxis", GRBLMachinePlugin.Props.RotaryAxis);
      UpdatePositionDisplay();
      RefreshControls();
    }

    private void JogMinus_Click(object sender, EventArgs e)
    {
      Jog(-1);
    }

    private void JogPlus_Click(object sender, EventArgs e)
    {
      Jog(1);
    }

    private void Jog(int direction)
    {
      double step;
      if (!TryRead(_jogStep, out step) || step <= 0)
      {
        MessageBox.Show("Enter a positive rotary jog step in degrees.", "Rotary Axis", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }

      GRBLMachinePlugin.Props.RotaryJogStep = step;
      if (!IsValidFeed(GRBLMachinePlugin.Props.RotaryJogFeed))
      {
        MessageBox.Show("Set a positive rotary jog feed in degrees per minute in the plugin properties.", "Rotary Axis", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }
      Send(AxisLetter + (direction < 0 ? "-" : "+") + step.ToString("0.####", EnUs), true);
    }

    private void Index_Click(object sender, EventArgs e)
    {
      double target;
      if (!TryRead(_target, out target))
      {
        MessageBox.Show("Enter an index angle in degrees.", "Rotary Axis", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }

      if (!IsValidFeed(GRBLMachinePlugin.Props.RotaryIndexFeed))
      {
        MessageBox.Show("Set a positive rotary index feed in degrees per minute in the plugin properties.", "Rotary Axis", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }

      Send("G21 G90 G1 F" + GRBLMachinePlugin.Props.RotaryIndexFeed.ToString("0.####", EnUs) + " " + AxisLetter + target.ToString("0.####", EnUs), false);
    }

    private void Zero_Click(object sender, EventArgs e)
    {
      Send("G10 P0 L20 " + AxisLetter + "0", false);
    }

    private void Send(string axisMove, bool jog)
    {
      if (!_connected || !_idle || _commandPending || !ConnectionExpander.IsCOMPortIdle)
        return;

      string command = jog
        ? "$J=G21 G91 " + axisMove + " F" + GRBLMachinePlugin.Props.RotaryJogFeed.ToString("0.####", EnUs)
        : axisMove;

      _commandPending = true;
      RefreshControls();
      if (!ConnectionExpander.WriteCOMPort(command))
      {
        _commandPending = false;
        RefreshControls();
      }
    }

    private static bool TryRead(TextBox box, out double value)
    {
      return double.TryParse(box.Text, NumberStyles.Float, EnUs, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool IsValidFeed(double feed)
    {
      return feed > 0 && !double.IsNaN(feed) && !double.IsInfinity(feed);
    }

    private void ConnectionExpander_Connected()
    {
      _connected = true;
      RefreshControls();
    }

    private void ConnectionExpander_Disconnected()
    {
      _connected = false;
      _axisReported = false;
      _commandPending = false;
      _machinePosition = _workPosition = _workOffset = double.NaN;
      UpdatePositionDisplay();
      RefreshControls();
    }

    private void ProductionExpander_JobStarted()
    {
      _jobActive = true;
      RefreshControls();
    }

    private void ProductionExpander_JobStopped()
    {
      _jobActive = false;
      RefreshControls();
    }

    private void GRBLMachinePlugin_State(GRBLMachinePlugin.MachineState state, int code)
    {
      _idle = state == GRBLMachinePlugin.MachineState.Idle;
      RefreshControls();
    }

    private void GRBLMachinePlugin_Responding(GRBLMachinePlugin.Response response, int code)
    {
      if (_commandPending)
      {
        _commandPending = false;
        RefreshControls();
      }
    }

    private void GRBLMachinePlugin_PropertyChanged(string name, object newvalue)
    {
      if (name == "RotaryAxis")
      {
        _updatingAxis = true;
        _axis.SelectedIndex = (int)GRBLMachinePlugin.Props.RotaryAxis;
        _updatingAxis = false;
        GRBLMachinePlugin_RotaryAxisAvailabilityChanged(false);
        UpdatePositionDisplay();
        RefreshControls();
      }
      else if (name == "DisplayPosType")
        UpdatePositionDisplay();
    }

    private void GRBLMachinePlugin_RotaryAxisAvailabilityChanged(bool available)
    {
      _axisReported = available;
      _clearanceNote.Text = available
        ? "Confirm the tool is clear before indexing. Rotary units are degrees."
        : AxisLetter + " axis not reported. Verify firmware support; rotary controls are disabled.";
      _clearanceNote.ForeColor = available ? Color.DarkOrange : Color.DarkRed;
      RefreshControls();
    }

    private void GRBLMachinePlugin_RotaryPosition(PosType type, double position)
    {
      if (double.IsNaN(position))
        return;

      switch (type)
      {
        case PosType.MachineCoordinates:
          _machinePosition = position;
          if (!double.IsNaN(_workOffset))
            _workPosition = _machinePosition - _workOffset;
          break;
        case PosType.WorkCoordinates:
          _workPosition = position;
          if (!double.IsNaN(_workOffset))
            _machinePosition = _workPosition + _workOffset;
          break;
        case PosType.WCO:
          _workOffset = position;
          if (!double.IsNaN(_machinePosition))
            _workPosition = _machinePosition - _workOffset;
          else if (!double.IsNaN(_workPosition))
            _machinePosition = _workPosition + _workOffset;
          break;
      }

      UpdatePositionDisplay();
    }

    private void UpdatePositionDisplay()
    {
      double position = GRBLMachinePlugin.Props.DisplayPosType == PosType.WorkCoordinates
        ? _workPosition
        : _machinePosition;
      string coordinateType = GRBLMachinePlugin.Props.DisplayPosType == PosType.WorkCoordinates ? "WPos: " : "MPos: ";
      _position.Text = coordinateType + (double.IsNaN(position) ? "----.---" : position.ToString("+0.000;-0.000;0.000", EnUs)) + "°";
    }

    private void RefreshControls()
    {
      bool enabled = _connected && _idle && _axisReported && !_jobActive && !_commandPending && ConnectionExpander.IsCOMPortIdle;
      _axis.Enabled = _connected && _idle && !_jobActive && !_commandPending;
      _jogStep.Enabled = _connected && _idle && _axisReported && !_jobActive && !_commandPending;
      _target.Enabled = _jogStep.Enabled;
      _jogMinus.Enabled = _jogPlus.Enabled = _index.Enabled = _zero.Enabled = enabled;
    }
  }
}
