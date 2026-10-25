using CamBam.UI;
using GRBLMachine.Properties;
using GRBLMachine.Utils;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Management;
using System.Threading;
using System.Globalization;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.Text;

namespace GRBLMachine.UI
{
  public partial class ConnectionExpander : Expander
  {
    public static  readonly Dictionary<int,string> ErrorDict = new Dictionary<int, string>();
    public static  readonly Dictionary<int,string> AlarmDict = new Dictionary<int, string>();

    public static  string               GrblVersion          = "1.1";
    public static  bool                 GrblIsV1             = true;

    private const  int                  BUFMAX               = 100;
    private const  string               GUID_DEVCLASS_PORTS  = "{4d36e978-e325-11ce-bfc1-08002be10318}";

    private static SerialPort           _serialPort;
    private static TcpClient            _tcpClient;
    private static NetworkStream        _networkStream;
    private static ConnectionExpander   _this;
    private static Thread               _readThread;
    private static GRBLSemaphore        _writeSemaphore      = new GRBLSemaphore(BUFMAX);
    private static Queue<int>           _writeQueue          = new Queue<int>   (BUFMAX);
    private static char[]               _charBuf             = new char[1];
    private static bool                 _firstParse;
    private static CultureInfo          EN_US                = new CultureInfo("en-US");
    private static bool                 _simulatorActive;
    private static double[]             _simMachinePosition   = new double[6];
    private static double[]             _simWorkOffset        = new double[6];
    private static double[]             _simMoveStartPosition = new double[6];
    private static double[]             _simMoveTargetPosition = new double[6];
    private static ManualResetEvent     _simulatorMotionComplete = new ManualResetEvent(true);
    private static int                  _simulatorRotaryCount = 1;
    private static int                  _simMotionMode;
    private static double               _simFeedRate = 500;
    private static double               _simUnitScale = 1;
    private static double               _simReportedFeed = 3000;
    private static int                  _simSpindleCommandedSpeed;
    private static int                  _simReportedSpindleSpeed;
    private static bool                 _simSpindleEnabled;
    private static double               _simPlaybackMultiplier = 1;
    private static double               _simMoveDurationMs = 45;
    private static double               _simMoveElapsedMs;
    private static long                 _simMoveLastTickUtc;
    private static bool                 _simTimerResolutionRequested;
    private System.Windows.Forms.Timer   _simulatorMotionTimer;
    private ComboBox                    _simulatorProfile;
    private Button                      _simulatorButton;
    private TextBox                     _ethernetHost;
    private NumericUpDown               _ethernetPort;
    private Label                       _ethernetHostLabel;
    private Label                       _ethernetPortLabel;
    private Label                       _connectionModeLabel;
    private CheckBox                    _ethernetMode;

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint BeginTimerResolution(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint EndTimerResolution(uint milliseconds);


    static ConnectionExpander()
    {
      ErrorDict.Add( 1, "G-code words consist of a letter and a value.\r\nLetter was not found. ");
      ErrorDict.Add( 2, "Numeric value format is not valid\r\nor missing an expected value. ");
      ErrorDict.Add( 3, "GRBL '$' system command was not recognized or supported. ");
      ErrorDict.Add( 4, "Negative value received for an expected positive value. ");
      ErrorDict.Add( 5, "Homing cycle is not enabled via settings. ");
      ErrorDict.Add( 6, "Minimum step pulse time must be greater than 3usec ");
      ErrorDict.Add( 7, "EEPROM read failed.\r\nReset and restored to default values. ");
      ErrorDict.Add( 8, "GRBL '$' command cannot be used unless GRBL is IDLE.\r\nEnsures smooth operation during a job. ");
      ErrorDict.Add( 9, "G-code locked out during alarm or jog state ");
      ErrorDict.Add(10, "Soft limits cannot be enabled without homing also enabled. ");
      ErrorDict.Add(11, "Max characters per line exceeded.\r\nLine was not processed and executed. ");
      ErrorDict.Add(12, "(Compile Option) GRBL '$' setting value exceeds\r\nthe maximum step rate supported. ");
      ErrorDict.Add(13, "Safety door detected as opened and door state initiated. ");
      ErrorDict.Add(14, "(GRBL-Mega Only) Build info or startup line\r\nexceeded EEPROM line length limit. ");
      ErrorDict.Add(15, "Jog target exceeds machine travel.\r\nCommand ignored. ");
      ErrorDict.Add(16, "Jog command with no '=' or contains prohibited g-code. ");
      ErrorDict.Add(20, "Unsupported or invalid g-code command found in block. ");
      ErrorDict.Add(21, "More than one g-code command from\r\nsame modal group found in block. ");
      ErrorDict.Add(22, "Feed rate has not yet been set or is undefined. ");
      ErrorDict.Add(23, "G-code command in block requires an integer value. ");
      ErrorDict.Add(24, "Two G-code commands that both require the use\r\nof the  XYZ  axis words were detected in the block. ");
      ErrorDict.Add(25, "A G-code word was repeated in the block. ");
      ErrorDict.Add(26, "A G-code command implicitly or explicitly requires\r\nXYZ axis words in the block, but none were detected. ");
      ErrorDict.Add(27, "N  line number value is not within the valid range of  1  -  9,999,999 . ");
      ErrorDict.Add(28, "A G-code command was sent, but is missing some\r\nrequired  P  or  L  value words in the line. ");
      ErrorDict.Add(29, "GRBL supports six work coordinate systems  G54-G59.\r\nG59.1 ,  G59.2 , and  G59.3  are not supported. ");
      ErrorDict.Add(30, "The  G53  G-code command requires either\r\na G0 seek or G1 feed motion mode to be active.\r\nA different motion was active. ");
      ErrorDict.Add(31, "There are unused axis words in the block\r\nand G80 motion mode cancel is active. ");
      ErrorDict.Add(32, "A  G2  or  G3  arc was commanded but there are no\r\nXYZ  axis words in the selected plane to trace the arc. ");
      ErrorDict.Add(33, "The motion command has an invalid target.\r\nG2 ,  G3 , and  G38.2  generates this error,\r\nif the arc is impossible to generate or\r\nif the probe target is the current position. ");
      ErrorDict.Add(34, "A  G2  or  G3  arc, traced with the radius definition,\r\nhad a mathematical error when computing the arc geometry.\r\nTry either breaking up the arc into semi-circles or quadrants,\r\nor redefine them with the arc offset definition. ");
      ErrorDict.Add(35, "A  G2  or  G3  arc, traced with the offset definition, is missing the  IJK  offset word in the selected plane to trace the arc. ");
      ErrorDict.Add(36, "There are unused, leftover G-code words that aren't\r\nused by any command in the block. ");
      ErrorDict.Add(37, "The  G43.1  dynamic tool length offset command cannot\r\napply an offset to an axis other than its configured axis.\r\nThe GRBL default axis is the Z-axis.");

      AlarmDict.Add( 1, "Hard limit triggered.\r\n\r\nMachine position is likely lost due\r\nto sudden and immediate halt.\r\n\r\nRe-homing is highly recommended. ");
      AlarmDict.Add( 2, "G-code motion target exceeds machine travel.\r\n\r\nMachine position safely retained.\r\n\r\nAlarm may be unlocked. ");
      AlarmDict.Add( 3, "Reset while in motion.\r\n\r\nGRBL cannot guarantee position.\r\nLost steps are likely.\r\n\r\nRe-homing is highly recommended. ");
      AlarmDict.Add( 4, "Probe fail.\r\n\r\nThe probe is not in the expected initial state before starting probe cycle,\r\nwhere G38.2 and G38.3 is not triggered and G38.4 and G38.5 is triggered. ");
      AlarmDict.Add( 5, "Probe fail.\r\n\r\nProbe did not contact the workpiece within the programmed travel for G38.2 and G38.4. ");
      AlarmDict.Add( 6, "Homing fail.\r\n\r\nReset during active homing cycle. ");
      AlarmDict.Add( 7, "Homing fail.\r\n\r\nSafety door was opened during active homing cycle. ");
      AlarmDict.Add( 8, "Homing fail.\r\n\r\nCycle failed to clear limit switch when pulling off.\r\n\r\nTry increasing pull-off setting or check wiring. ");
      AlarmDict.Add( 9, "Homing fail.\r\n\r\nCould not find limit switch within search distance.\r\nDefined as  1.5 * max_travel  on search and  5 * pulloff  on locate phases. ");
    }

    public ConnectionExpander()
    {
      _this                                = this;

      InitializeComponent();

      // Reserve the first row for the serial/network selector and align both
      // connection types so switching modes does not move the rest of the panel.
      COMPort.Location = new Point(104, 44);
      RefreshButton.Location = new Point(75, 43);
      label1.Location = new Point(12, 48);
      label1.Text = "COM port:";
      Baudrate.Location = new Point(104, 74);
      label2.Location = new Point(12, 78);
      AutoConnect.Location = new Point(104, 104);
      ConnectButton.Location = new Point(104, 132);
      label3.Location = new Point(231, 140);
      VersionLabel.Location = new Point(316, 140);

      // Network mode uses the same raw TCP stream protocol as gSender:
      // newline-delimited controller text and unframed realtime command bytes.
      _connectionModeLabel = new Label();
      _connectionModeLabel.AutoSize = true;
      _connectionModeLabel.Location = new Point(12, 16);
      _connectionModeLabel.Text = "Ethernet:";
      ContentPanel.Controls.Add(_connectionModeLabel);

      _ethernetMode = new CheckBox();
      _ethernetMode.AutoSize = true;
      _ethernetMode.Location = new Point(104, 15);
      _ethernetMode.Text = "Use TCP/IP";
      _ethernetMode.Checked = GRBLMachinePlugin.Props.EthernetEnabled;
      _ethernetMode.CheckedChanged += EthernetMode_CheckedChanged;
      ContentPanel.Controls.Add(_ethernetMode);

      _ethernetHostLabel = new Label();
      _ethernetHostLabel.AutoSize = true;
      _ethernetHostLabel.Location = new Point(12, 48);
      _ethernetHostLabel.Text = "Host/IP:";
      ContentPanel.Controls.Add(_ethernetHostLabel);

      _ethernetHost = new TextBox();
      _ethernetHost.Location = new Point(104, 44);
      _ethernetHost.Size = new Size(121, 22);
      _ethernetHost.Text = GRBLMachinePlugin.Props.EthernetHost ?? string.Empty;
      TheToolTip.SetToolTip(_ethernetHost, "Controller IP address or host name (for example 192.168.5.1)");
      _ethernetHost.TextChanged += EthernetHost_TextChanged;
      ContentPanel.Controls.Add(_ethernetHost);

      _ethernetPortLabel = new Label();
      _ethernetPortLabel.AutoSize = true;
      _ethernetPortLabel.Location = new Point(12, 78);
      _ethernetPortLabel.Text = "TCP port:";
      ContentPanel.Controls.Add(_ethernetPortLabel);

      _ethernetPort = new NumericUpDown();
      _ethernetPort.Location = new Point(104, 74);
      _ethernetPort.Size = new Size(121, 22);
      _ethernetPort.Minimum = 1;
      _ethernetPort.Maximum = 65535;
      _ethernetPort.Value = Math.Max(1, Math.Min(65535, GRBLMachinePlugin.Props.EthernetPort));
      TheToolTip.SetToolTip(_ethernetPort, "Controller TCP port. GRBL network interfaces commonly use port 23.");
      _ethernetPort.ValueChanged += EthernetPort_ValueChanged;
      ContentPanel.Controls.Add(_ethernetPort);
      UpdateConnectionModeControls();

      _simulatorProfile = new ComboBox();
      _simulatorProfile.DropDownStyle = ComboBoxStyle.DropDownList;
      _simulatorProfile.Items.AddRange(new object[] { "XYZ (3-axis)", "XYZ + A (4-axis)", "XYZ + A/B (5-axis)", "XYZ + A/B/C (6-axis)" });
      _simulatorProfile.SelectedIndex = 1;
      _simulatorProfile.Location = new Point(231, 35);
      _simulatorProfile.Size = new Size(121, 24);
      _simulatorProfile.Name = "SimulatorProfile";
      TheToolTip.SetToolTip(_simulatorProfile, "Choose the axes reported by the simulated controller");
      ContentPanel.Controls.Add(_simulatorProfile);

      Label simulatorLabel = new Label();
      simulatorLabel.AutoSize = true;
      simulatorLabel.Location = new Point(231, 13);
      simulatorLabel.Text = "Simulator profile:";
      ContentPanel.Controls.Add(simulatorLabel);

      _simulatorButton = new Button();
      _simulatorButton.Location = new Point(231, 68);
      _simulatorButton.Size = new Size(121, 34);
      _simulatorButton.Text = "START SIM";
      _simulatorButton.Font = new Font("Microsoft Sans Serif", 8F, FontStyle.Bold);
      _simulatorButton.ForeColor = Color.DarkBlue;
      _simulatorButton.Click += SimulatorButton_Click;
      TheToolTip.SetToolTip(_simulatorButton, "Connect to a simulated controller. No serial or network data is sent.");
      ContentPanel.Controls.Add(_simulatorButton);

      Label simulatorNote = new Label();
      simulatorNote.AutoSize = true;
      simulatorNote.Location = new Point(12, 173);
      simulatorNote.ForeColor = Color.DimGray;
      simulatorNote.Text = "Simulator never sends data over serial or Ethernet.";
      ContentPanel.Controls.Add(simulatorNote);
      UiTheme.Apply(this);

      _simulatorMotionTimer = new System.Windows.Forms.Timer(components);
      // Publish position samples often enough for CamBam to animate the simulated move.
      _simulatorMotionTimer.Interval = 25;
      _simulatorMotionTimer.Tick += SimulatorMotionTimer_Tick;

      _serialPort                          = ThePort;

      COMPort.Items.Clear();
      COMPort.Items.AddRange(SerialPort.GetPortNames());
      COMPort.SelectedItem                 = GRBLMachinePlugin.Props.COMPort;

      Baudrate.SelectedItem                = GRBLMachinePlugin.Props.Baudrate.ToString();

      AutoConnect.Checked                  = GRBLMachinePlugin.Props.AutoConnect;

      WqlEventQuery          insertQuery   = new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity'");
      ManagementEventWatcher insertWatcher = new ManagementEventWatcher(insertQuery);

      insertWatcher.EventArrived          += new EventArrivedEventHandler(DeviceInsertedEvent);
      insertWatcher.Start();

      WqlEventQuery          removeQuery   = new WqlEventQuery("SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity'");
      
      ManagementEventWatcher removeWatcher = new ManagementEventWatcher(removeQuery);

      removeWatcher.EventArrived          += new EventArrivedEventHandler(DeviceRemovedEvent);
      removeWatcher.Start();

      Connected                           += ConnectionExpander_Connected;
      Disconnected                        += ConnectionExpander_Disconnected;

      GRBLMachinePlugin.PropertyChanged   += Props_PropertyChanged;

      AutoConnect.CheckedChanged          += AutoConnect_CheckedChanged;
    }

#region Publics

    public static bool IsCOMPortIdle
    {
      get
      {
        return _writeQueue.Count == 0;
      }
    }

    private static bool IsPhysicalConnectionOpen
    {
      get { return (_serialPort != null && _serialPort.IsOpen) || (_tcpClient != null && _tcpClient.Connected); }
    }

    public static bool SimulatorActive
    {
      get { return _simulatorActive; }
    }

    public static int SimulatorPlaybackSpeed
    {
      get { return (int)_simPlaybackMultiplier; }
    }

    public static void SetSimulatorPlaybackSpeed(int multiplier)
    {
      if (multiplier == 1 || multiplier == 2 || multiplier == 5 || multiplier == 10)
      {
        _simPlaybackMultiplier = multiplier;
        if (_simulatorActive && _this != null && _this._simulatorMotionTimer != null)
          _this._simulatorMotionTimer.Interval = Math.Max(1, 25 / multiplier);
      }
    }

    public static bool WriteJobGCode(string data)
    {
      bool waitForSimulator = _simulatorActive;
      bool written = WriteCOMPort(data);
      // Process simulator moves one at a time. Skipping waits lets a following
      // XY command replace the displayed target before a Z retract is drawn.
      if (written && waitForSimulator)
        _simulatorMotionComplete.WaitOne();
      return written;
    }

    public static void BeginSimulatorJob(int lineCount)
    {
      _simMotionMode = 0;
      _simFeedRate = 500;
      _simUnitScale = 1;
      _simSpindleCommandedSpeed = 0;
      _simReportedSpindleSpeed = 0;
      _simSpindleEnabled = false;
    }

    public static void WaitForSimulatorJobToFinish()
    {
      if (_simulatorActive)
        _simulatorMotionComplete.WaitOne();
    }

    public static bool WriteCOMPort(char data)
    {
      if (_simulatorActive)
      {
        if (_this.InvokeRequired)
          _this.BeginInvoke(new Action(() => SimulateRealtimeCommand(data)));
        else
          SimulateRealtimeCommand(data);

        return true;
      }

      try
      {
        if (IsPhysicalConnectionOpen)
        {
          _charBuf[0] = data;

          if (_tcpClient != null && _tcpClient.Connected && _networkStream != null)
          {
            byte[] bytes = new byte[] { (byte)data };
            lock (_networkStream) _networkStream.Write(bytes, 0, 1);
          }
          else
          {
            lock(_serialPort)
              _serialPort.Write(_charBuf,0,1);
          }

          return true;
        }

        return false;
      }
      catch (Exception)
      {
        return false;
      }
    }

    public static bool WriteCOMPort(string data)
    {
      if (_simulatorActive)
      {
        try
        {
          if (_this.InvokeRequired)
            _this.Invoke(new Action(() => SimulateCommand(data)));
          else
            SimulateCommand(data);

          return true;
        }
        catch (Exception)
        {
          return false;
        }
      }

      if (!_this.InvokeRequired)
      {
        new Thread(new ParameterizedThreadStart(WriteCOMPortAsync)) { Name = "WriteCOMPortAsync", IsBackground = true }.Start(data);

        return true;
      }

      try
      {
        if (IsPhysicalConnectionOpen)
        {
          if ((data.Length + 1) > BUFMAX)
          {
            GRBLMachinePlugin.Respond(GRBLMachinePlugin.Response.error,0);

            return false;
          }

          GRBLMachinePlugin.Log(">>> " + data);

          for (int i = 0; i != (data.Length + 1); i++)
            _writeSemaphore.Acquire();

          if (_tcpClient != null && _tcpClient.Connected && _networkStream != null)
          {
            byte[] bytes = Encoding.ASCII.GetBytes(data + "\n");
            lock (_networkStream) _networkStream.Write(bytes, 0, bytes.Length);
          }
          else
          {
            lock(_serialPort)
              _serialPort.Write(data + '\r');
          }

          lock (_writeQueue)
            _writeQueue.Enqueue(data.Length + 1);

          return true;
        }
        else
        {
          ClearWriteQueue();
          GRBLMachinePlugin.Log(data);
          return false;
        }
      }
      catch (ThreadInterruptedException)
      {
        ClearWriteQueue();
        throw;
      }
      catch (Exception)
      {
        ClearWriteQueue();
        return false;
      }
    }

    public        delegate void ConnectDisconnectHandler();
    public static event         ConnectDisconnectHandler Connected;
    public static event         ConnectDisconnectHandler Disconnected;

#endregion // Publics

#region Overrides

    protected override void OnLoad(EventArgs e)
    {
      base.OnLoad(e);

      if (GRBLMachinePlugin.Props.AutoConnect)
        OpenCOMPort();
      else
        Disconnect();
    }

#endregion // Publics

#region Privates

    private static bool OpenCOMPort(bool reportException = true)
    {
      if (_simulatorActive)
        return false;

      if (GRBLMachinePlugin.Props.EthernetEnabled)
        return OpenEthernetPort(reportException);

      if (string.IsNullOrEmpty(GRBLMachinePlugin.Props.COMPort))
        return false;

      try
      {
        if (_serialPort.IsOpen) { 
          _serialPort.Close();
          _serialPort.Dispose();

          _serialPort = new SerialPort();

          System.Threading.Thread.Sleep(100);
        }

        _serialPort.PortName     = GRBLMachinePlugin.Props.COMPort;
        _serialPort.DataBits     = 8;
        _serialPort.StopBits     = System.IO.Ports.StopBits.One;
        _serialPort.Parity       = System.IO.Ports.Parity.None;
        _serialPort.BaudRate     = GRBLMachinePlugin.Props.Baudrate;
        _serialPort.Handshake    = System.IO.Ports.Handshake.None;
        _serialPort.ReadTimeout  = -1;
        _serialPort.RtsEnable    = false;
        _serialPort.DtrEnable    = false;

        _serialPort.Open();
        _firstParse = true;

        GRBLMachinePlugin.Log("Opened: " + _serialPort.PortName);

        if (_readThread == null)
        {
          _readThread              = new System.Threading.Thread(ReadThread);
          _readThread.IsBackground = true;
          _readThread.Name         = "PortReader";
          _readThread.Start();
        }

        _serialPort.RtsEnable    = true;
        _serialPort.DtrEnable    = true;

        ClearWriteQueue();

        System.Threading.Thread.Sleep(10);

        Connect();

        return true;
      }
      catch (Exception e)
      {
        if (reportException)
          GRBLMachinePlugin.Log(e.ToString());

        Disconnect();
        return false;
      }
    }

    private static bool OpenEthernetPort(bool reportException = true)
    {
      if (_simulatorActive || string.IsNullOrEmpty(GRBLMachinePlugin.Props.EthernetHost) || GRBLMachinePlugin.Props.EthernetHost.Trim().Length == 0)
        return false;

      try
      {
        CloseNetworkPort(true);
        _tcpClient = new TcpClient();
        IAsyncResult pending = _tcpClient.BeginConnect(GRBLMachinePlugin.Props.EthernetHost.Trim(), GRBLMachinePlugin.Props.EthernetPort, null, null);
        System.Threading.WaitHandle connectWaitHandle = pending.AsyncWaitHandle;
        bool connected = connectWaitHandle.WaitOne(3000);
        connectWaitHandle.Close();
        if (!connected)
        {
          _tcpClient.Close();
          _tcpClient = null;
          throw new TimeoutException("TCP connection timed out after 3 seconds.");
        }
        _tcpClient.EndConnect(pending);
        _networkStream = _tcpClient.GetStream();
        _networkStream.ReadTimeout = Timeout.Infinite;
        GRBLMachinePlugin.Log("Opened TCP connection: " + GRBLMachinePlugin.Props.EthernetHost + ":" + GRBLMachinePlugin.Props.EthernetPort);

        if (_readThread == null)
        {
          _readThread = new Thread(ReadThread);
          _readThread.IsBackground = true;
          _readThread.Name = "NetworkPortReader";
          _readThread.Start();
        }

        _firstParse = true;
        ClearWriteQueue();
        Connect();
        // TCP does not toggle DTR like USB serial, so actively request firmware
        // identification and start status polling after the socket opens.
        _this.StatusTimer.Enabled = true;
        WriteCOMPort('?');
        WriteCOMPort("$I");
        return true;
      }
      catch (Exception e)
      {
        if (reportException)
          GRBLMachinePlugin.Log("Ethernet connection failed: " + e.Message);
        CloseNetworkPort(true);
        Disconnect();
        return false;
      }
    }

    private static bool CloseNetworkPort(bool immediate = false)
    {
      if (_this != null)
        _this.StatusTimer.Enabled = false;
      try
      {
        string endpoint = GRBLMachinePlugin.Props.EthernetHost + ":" + GRBLMachinePlugin.Props.EthernetPort;
        if (_networkStream != null) _networkStream.Close();
        if (_tcpClient != null) _tcpClient.Close();
        _networkStream = null;
        _tcpClient = null;
        if (!string.IsNullOrEmpty(endpoint))
          GRBLMachinePlugin.Log("Closed TCP connection: " + endpoint);
        if (!immediate) Thread.Sleep(2000);
        Disconnect();
        return true;
      }
      catch (Exception)
      {
        _networkStream = null;
        _tcpClient = null;
        Disconnect();
        return false;
      }
    }

    private static bool CloseCOMPort(bool immediate = false)
    {
      if (_tcpClient != null)
        return CloseNetworkPort(immediate);

      _this.StatusTimer.Enabled = false;

      try
      {
        _serialPort.DtrEnable = false;
        _serialPort.RtsEnable = false;

        _serialPort.Close();
        _serialPort.Dispose();

        GRBLMachinePlugin.Log("Closed: " + _serialPort.PortName);

        _serialPort = new SerialPort();

        if (!immediate)
          System.Threading.Thread.Sleep(2000);

        Disconnect();

        return true;
      }
      catch (Exception)
      {
        try
        {
          _serialPort.Close();
          _serialPort.Dispose();
        }
        catch (Exception) { }

        Disconnect();

        return false;
      }
    }

    private static void WriteCOMPortAsync(object data)
    {
      WriteCOMPort(data as string);
    }

    private static void Connect()
    {
      if (Connected != null)
        Connected();
    }

    private static void Disconnect()
    {
      GRBLMachinePlugin.SetRotaryAxisAvailability(RotaryAxisLetter.A, false);
      GRBLMachinePlugin.SetRotaryAxisAvailability(RotaryAxisLetter.B, false);
      GRBLMachinePlugin.SetRotaryAxisAvailability(RotaryAxisLetter.C, false);
      GRBLMachinePlugin.SetRotaryAxisAvailability(false);
      if (Disconnected != null)
        Disconnected();
    }

    private void SimulatorButton_Click(object sender, EventArgs e)
    {
      if (_simulatorActive)
      {
        if (ProductionExpander.JobActive)
        {
          MessageBox.Show("Stop the simulated job before disconnecting the simulator.", "Controller Simulator", MessageBoxButtons.OK, MessageBoxIcon.Information);
          return;
        }

        _simulatorMotionTimer.Stop();
        _simulatorMotionComplete.Set();
        ReleaseSimulatorTimerResolution();
        StatusTimer.Enabled = false;
        _simulatorActive = false;
        _simulatorProfile.Enabled = true;
        VersionLabel.Text = "-.-";
        label3.Visible = true;
        VersionLabel.Visible = true;
        _simulatorButton.Text = "START SIM";
        TheToolTip.SetToolTip(_simulatorButton, "Connect to a simulated controller. No serial data is sent.");
        GRBLMachinePlugin.Log("Simulator disconnected. No controller was connected.");
        GRBLMachinePlugin.Stat(GRBLMachinePlugin.MachineState.Idle);
        Disconnect();
        return;
      }

      if (IsPhysicalConnectionOpen || GRBLMachinePlugin.Connected)
      {
        MessageBox.Show("Close the physical controller connection before starting the simulator.", "Controller Simulator", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }

      _simulatorRotaryCount = Math.Max(0, _simulatorProfile.SelectedIndex);
      _simulatorMotionComplete.Set();
      _simMachinePosition = new double[6];
      _simWorkOffset = new double[6];
      _simulatorActive = true;
      RequestSimulatorTimerResolution();
      _simulatorMotionTimer.Interval = Math.Max(1, 25 / (int)_simPlaybackMultiplier);
      _firstParse = false;
      GRBLMachinePlugin.Props.ReportInInches = InchMM.Millimeters;
      GRBLMachinePlugin.PropertyChange("ReportInInches", InchMM.Millimeters);
      GrblVersion = "1.1h";
      GrblIsV1 = true;
      VersionLabel.Text = "SIM 1.1h";
      VersionLabel.ForeColor = Color.DarkOrange;
      label3.Visible = false;
      VersionLabel.Visible = false;
      TheToolTip.SetToolTip(_simulatorButton, "Stop the simulator (GRBL 1.1h profile). No serial data is sent.");
      _simulatorProfile.Enabled = false;
      _simulatorButton.Text = "STOP SIM";
      _simulatorButton.ForeColor = Color.DarkRed;
      GRBLMachinePlugin.Log("Simulator connected. Profile: " + _simulatorProfile.SelectedItem + ". No serial port is open.");
      Connect();
      GRBLMachinePlugin.Stat(GRBLMachinePlugin.MachineState.Idle);
      StatusTimer.Enabled = true;
      PublishSimulatorStatus();
    }

    private void SimulatorMotionTimer_Tick(object sender, EventArgs e)
    {
      if (!_simulatorActive)
      {
        _simulatorMotionTimer.Stop();
        _simulatorMotionComplete.Set();
        return;
      }

      long now = DateTime.UtcNow.Ticks;
      double elapsedMs = (now - _simMoveLastTickUtc) / (double)TimeSpan.TicksPerMillisecond;
      _simMoveLastTickUtc = now;
      _simMoveElapsedMs += Math.Max(0, elapsedMs) * _simPlaybackMultiplier;
      double progress = _simMoveDurationMs <= 0 ? 1 : Math.Min(1, _simMoveElapsedMs / _simMoveDurationMs);

      for (int i = 0; i < _simMachinePosition.Length; i++)
        _simMachinePosition[i] = _simMoveStartPosition[i] + ((_simMoveTargetPosition[i] - _simMoveStartPosition[i]) * progress);

      if (progress >= 1)
      {
        _simulatorMotionTimer.Stop();
        GRBLMachinePlugin.Stat(GRBLMachinePlugin.MachineState.Idle);
        PublishSimulatorStatus();
        _simulatorMotionComplete.Set();
      }
      else
        PublishSimulatorStatus();
    }

    private static void RequestSimulatorTimerResolution()
    {
      if (!_simTimerResolutionRequested)
      {
        BeginTimerResolution(1);
        _simTimerResolutionRequested = true;
      }
    }

    private static void ReleaseSimulatorTimerResolution()
    {
      if (_simTimerResolutionRequested)
      {
        EndTimerResolution(1);
        _simTimerResolutionRequested = false;
      }
    }

    private static void SimulateRealtimeCommand(char data)
    {
      if (!_simulatorActive)
        return;

      switch (data)
      {
        case '?':
          PublishSimulatorStatus();
          break;
        case '!':
          _this._simulatorMotionTimer.Stop();
          GRBLMachinePlugin.Stat(GRBLMachinePlugin.MachineState.Hold);
          PublishSimulatorStatus();
          _simulatorMotionComplete.Set();
          break;
        case '~':
          _this._simulatorMotionTimer.Stop();
          GRBLMachinePlugin.Stat(GRBLMachinePlugin.MachineState.Idle);
          PublishSimulatorStatus();
          _simulatorMotionComplete.Set();
          break;
        case (char)0x18:
          _this._simulatorMotionTimer.Stop();
          GRBLMachinePlugin.Stat(GRBLMachinePlugin.MachineState.Idle);
          GRBLMachinePlugin.Log("Simulator reset; coordinates retained for inspection.");
          PublishSimulatorStatus();
          _simulatorMotionComplete.Set();
          break;
      }
    }

    private static void SimulateCommand(string command)
    {
      if (!_simulatorActive || string.IsNullOrEmpty(command) || command.Trim().Length == 0)
        return;

      GRBLMachinePlugin.Log(">>> " + command);
      // NC files commonly place words together (for example M3S12000) and may
      // include inline comments. Remove comments before interpreting commands,
      // then recognize words by letter boundaries rather than whitespace.
      string normalized = Regex.Replace(command.ToUpperInvariant(), @"\([^)]*\)|;.*$", string.Empty);
      bool setsWorkOffset = normalized.Contains("G10") && normalized.Contains("L20");
      bool machineCoordinates = normalized.Contains("G53");
      bool relative = normalized.Contains("G91") || normalized.StartsWith("$J=");
      bool moved = false;
      double[] startPosition = (double[])_simMachinePosition.Clone();
      double[] targetPosition = (double[])_simMachinePosition.Clone();

      MatchCollection gCodes = Regex.Matches(normalized, @"(?:^|\s)G0*(\d+(?:\.\d*)?)");
      foreach (Match gCode in gCodes)
      {
        double g;
        if (!double.TryParse(gCode.Groups[1].Value, NumberStyles.Float, EN_US, out g))
          continue;
        if (Math.Abs(g - 0) < 0.0001) _simMotionMode = 0;
        else if (Math.Abs(g - 1) < 0.0001) _simMotionMode = 1;
        else if (Math.Abs(g - 20) < 0.0001) _simUnitScale = 25.4;
        else if (Math.Abs(g - 21) < 0.0001) _simUnitScale = 1;
      }

      Match feed = Regex.Match(normalized, @"(?:^|\s)F([-+]?(?:\d+(?:\.\d*)?|\.\d+))");
      double parsedFeed;
      if (feed.Success && double.TryParse(feed.Groups[1].Value, NumberStyles.Float, EN_US, out parsedFeed) && parsedFeed > 0)
        _simFeedRate = parsedFeed;

      Match spindle = Regex.Match(normalized, @"(?:^|[^A-Z])S([-+]?(?:\d+(?:\.\d*)?|\.\d+))");
      double parsedSpindle;
      if (spindle.Success && double.TryParse(spindle.Groups[1].Value, NumberStyles.Float, EN_US, out parsedSpindle) && parsedSpindle >= 0)
        _simSpindleCommandedSpeed = (int)Math.Round(parsedSpindle);

      if (Regex.IsMatch(normalized, @"(?:^|[^A-Z0-9_])M0*[34](?!\d)"))
        _simSpindleEnabled = true;
      if (Regex.IsMatch(normalized, @"(?:^|[^A-Z0-9_])M0*5(?!\d)") || Regex.IsMatch(normalized, @"(?:^|[^A-Z0-9_])M0*30(?!\d)"))
        _simSpindleEnabled = false;
      _simReportedSpindleSpeed = _simSpindleEnabled ? _simSpindleCommandedSpeed : 0;

      MatchCollection axes = Regex.Matches(normalized, @"([XYZABC])([+-]?(?:\d+(?:\.\d*)?|\.\d+))");
      foreach (Match axis in axes)
      {
        int index = "XYZABC".IndexOf(axis.Groups[1].Value[0]);
        double value;
        if (index < 0 || !double.TryParse(axis.Groups[2].Value, NumberStyles.Float, EN_US, out value))
          continue;

        if (setsWorkOffset)
          _simWorkOffset[index] = _simMachinePosition[index] - value;
        else if (machineCoordinates)
        {
          targetPosition[index] = value;
          moved = true;
        }
        else if (relative)
        {
          targetPosition[index] += value;
          moved = true;
        }
        else
        {
          targetPosition[index] = value + _simWorkOffset[index];
          moved = true;
        }
      }

      if (moved)
      {
        _this._simulatorMotionTimer.Stop();
        _simulatorMotionComplete.Reset();
        double dx = targetPosition[0] - startPosition[0];
        double dy = targetPosition[1] - startPosition[1];
        double dz = targetPosition[2] - startPosition[2];
        double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        double rapidRate = 3000 / _simUnitScale;
        _simReportedFeed = _simMotionMode == 0 ? rapidRate : _simFeedRate;
        double moveDuration = 45;
        if (distance > 0.000001)
        {
          // G0 is modeled at a conservative 3000 mm/min equivalent.
          double rate = _simReportedFeed > 0 ? _simReportedFeed : rapidRate;
          moveDuration = Math.Max(moveDuration, distance * 60000.0 / rate);
        }
        // Bound unusually long blocks while preserving a meaningful effect from
        // the speed multiplier. Position interpolation uses the selected speed
        // on every timer tick, including when it changes during a move.
        _simMoveDurationMs = Math.Min(20000, moveDuration);
        _simMoveElapsedMs = 0;
        _simMoveLastTickUtc = DateTime.UtcNow.Ticks;
        _simMoveStartPosition = startPosition;
        _simMoveTargetPosition = targetPosition;
        _this._simulatorMotionTimer.Interval = Math.Max(1, 25 / (int)_simPlaybackMultiplier);
        GRBLMachinePlugin.Stat(GRBLMachinePlugin.MachineState.Run);
        PublishSimulatorStatus();
        _this._simulatorMotionTimer.Start();
      }
      else
        PublishSimulatorStatus();

      Parse("ok");
    }

    private static void PublishSimulatorStatus()
    {
      if (!_simulatorActive)
        return;

      int axisCount = Math.Min(6, 3 + _simulatorRotaryCount);
      string machine = FormatSimulatorCoordinates(_simMachinePosition, axisCount);
      string offset = FormatSimulatorCoordinates(_simWorkOffset, axisCount);
      string state = GRBLMachinePlugin.CurrentMachineState.ToString();
      Parse("<" + state + "|MPos:" + machine + "|FS:" + _simReportedFeed.ToString("0.###", EN_US) + "," + _simReportedSpindleSpeed.ToString(EN_US) + "|WCO:" + offset + ">");
    }

    private static string FormatSimulatorCoordinates(double[] values, int count)
    {
      string result = string.Empty;
      for (int i = 0; i < count; i++)
      {
        if (i != 0)
          result += ",";
        result += values[i].ToString("0.000", EN_US);
      }
      return result;
    }

    private static void ReadThread()
    {
      _firstParse = true;

      while (true)
      {
        try
        {
          string incoming;
          if (_tcpClient != null && _tcpClient.Connected && _networkStream != null)
          {
            StringBuilder line = new StringBuilder();
            int value;
            while ((value = _networkStream.ReadByte()) >= 0 && value != '\n')
              line.Append((char)value);
            if (value < 0)
              throw new System.IO.IOException("Controller closed the TCP connection.");
            incoming = line.ToString().Trim(' ','\t','\r','\n');
          }
          else
            incoming = _serialPort.ReadLine().Trim(' ','\t','\r','\n');
          _this.InvokeOnUI(() => 
          {
            Parse(incoming);
          }); 
        }
        catch (System.IO.IOException)
        {
          _readThread = null;
          if (_tcpClient != null)
            _this.InvokeOnUI(() => CloseNetworkPort(true));
          return;
        }
        catch (InvalidOperationException)
        {
          _readThread = null;
          return;
        }
        catch (Exception e)
        {
          GRBLMachinePlugin.Log(e.ToString());
        }
      } 
    } 

    private static void ClearWriteQueue()
    {
      lock (_writeQueue)
      {
        _writeQueue    .Clear();
        _writeSemaphore.Reset(BUFMAX);
      }
    }

    private static void Parse(string message)
    {
      XPropertyGrid grid = CamBamUI.MainUI.ObjectProperties;

      GRBLMachinePlugin.Log(message, message.StartsWith("<"));

      if (_firstParse && (message.StartsWith("Grbl") || message.StartsWith("[VER:")))
      {
        string reportedVersion = string.Empty;
        if (message.StartsWith("Grbl"))
        {
          string[] greetz = message.Split(' ');
          if (greetz.Length > 1)
            reportedVersion = greetz[1];
        }
        else
        {
          Match versionMatch = Regex.Match(message, @"\[VER:([^:\]]+)");
          if (versionMatch.Success)
            reportedVersion = versionMatch.Groups[1].Value;
        }

        if (!string.IsNullOrEmpty(reportedVersion))
        {
          GrblVersion = reportedVersion;
          GrblIsV1 = !GrblVersion.StartsWith("0.");
          _this.VersionLabel.Text = GrblVersion;
          _this.VersionLabel.ForeColor = GrblIsV1 ? Color.LimeGreen : (GrblVersion.CompareTo("0.8") >= 0 ? Color.Orange : Color.Red);
        }

        _firstParse               = false;

        WriteCOMPort("$$");
        WriteCOMPort("$N");

        _this.StatusTimer.Enabled = true;
      }

      if (message.StartsWith("<") && message.EndsWith(">"))
      {
        if (!GrblIsV1)
        {
          //Version < 1.1 ==> "<Idle,MPos:0.000,0.000,0.000,WPos:0.000,0.000,0.000>"
          char[] charmessage = message.ToCharArray();

          charmessage[message.IndexOf(",MPos:")] = '|';
          charmessage[message.IndexOf(",WPos:")] = '|';

          message = new string(charmessage);
        }

        string[] report = message.Split('<','|','>');


        if (report.Length > 2)
        {
          string[] states = report[1].Split(':');

          GRBLMachinePlugin.Stat((GRBLMachinePlugin.MachineState)Enum.Parse(typeof(GRBLMachinePlugin.MachineState),states[0],true), states.Length > 1 ? int.Parse(states[1]) : -1);

          if (report.Length > 3)
          {
            string[] pos = report[2].Split(':',',');

            PosType type = PosType.Unknown;
            double  x    = double.NaN;
            double  y    = double.NaN;
            double  z    = double.NaN;
            double  rotary = double.NaN;

            if (pos.Length > 0)
              switch (pos[0].ToUpper())
              {
                case "MPOS": type = PosType.MachineCoordinates; break;
                case "WPOS": type = PosType.WorkCoordinates; break;
              }
            if (pos.Length > 1)
              x = double.Parse(pos[1], EN_US);
            if (pos.Length > 2)
              y = double.Parse(pos[2], EN_US);
            if (pos.Length > 3)
              z = double.Parse(pos[3], EN_US);
            int rotaryIndex = 4 + (int)GRBLMachinePlugin.Props.RotaryAxis;
            GRBLMachinePlugin.SetRotaryAxisAvailability(pos.Length > rotaryIndex);
            for (int axisIndex = 0; axisIndex < 3; axisIndex++)
            {
              RotaryAxisLetter axis = (RotaryAxisLetter)axisIndex;
              bool available = pos.Length > 4 + axisIndex;
              GRBLMachinePlugin.SetRotaryAxisAvailability(axis, available);
              if (available)
                GRBLMachinePlugin.RotaryPos(axis, type, double.Parse(pos[4 + axisIndex], EN_US));
            }
            if (pos.Length > rotaryIndex)
              rotary = double.Parse(pos[rotaryIndex], EN_US);

            GRBLMachinePlugin.Pos(type,x,y,z);
            GRBLMachinePlugin.RotaryPos(type,rotary);

            if (report.Length > 4)
            {
              string[] speeds = report[3].Split(':',',');

              switch (speeds[0].ToUpper())
              {
                case "F":
                  GRBLMachinePlugin.Speeds(double.Parse(speeds[1], EN_US),0);
                  break;
                case "FS":
                  GRBLMachinePlugin.Speeds(double.Parse(speeds[1], EN_US),int.Parse(speeds[2], EN_US));
                  break;
                case "WPOS":
                  {
                    double  wx = double.NaN;
                    double  wy = double.NaN;
                    double  wz = double.NaN;
                    double  wr = double.NaN;

                    if (speeds.Length > 1)
                      wx = double.Parse(speeds[1], EN_US);
                    if (speeds.Length > 2)
                      wy = double.Parse(speeds[2], EN_US);
                    if (speeds.Length > 3)
                      wz = double.Parse(speeds[3], EN_US);
                    if (speeds.Length > rotaryIndex)
                      wr = double.Parse(speeds[rotaryIndex], EN_US);

                    if (!double.IsNaN(wx) && !double.IsNaN(x))
                      wx = (x - wx);
                    if (!double.IsNaN(wy) && !double.IsNaN(y))
                      wy = (y - wy);
                    if (!double.IsNaN(wz) && !double.IsNaN(z))
                      wz = (z - wz);

                    GRBLMachinePlugin.Pos(PosType.WCO,wx,wy,wz);
                    GRBLMachinePlugin.RotaryPos(PosType.WCO,wr);
                  }
                  break;
              }

              if (report.Length > 5)
              {
                pos = report[4].Split(':',',');

                switch (pos[0].ToUpper())
                {
                  case "WCO":
                    type = PosType.Unknown;
                    x    = double.NaN;
                    y    = double.NaN;
                    z    = double.NaN;
                    rotary = double.NaN;

                    if (pos.Length > 0)
                      type = (PosType)Enum.Parse(typeof(PosType),pos[0],true);
                    if (pos.Length > 1)
                      x = double.Parse(pos[1], EN_US);
                    if (pos.Length > 2)
                      y = double.Parse(pos[2], EN_US);
                    if (pos.Length > 3)
                      z = double.Parse(pos[3], EN_US);
                    if (pos.Length > rotaryIndex)
                      rotary = double.Parse(pos[rotaryIndex], EN_US);
                    for (int axisIndex = 0; axisIndex < 3; axisIndex++)
                    {
                      if (pos.Length > 4 + axisIndex)
                        GRBLMachinePlugin.RotaryPos((RotaryAxisLetter)axisIndex, type, double.Parse(pos[4 + axisIndex], EN_US));
                    }

                    GRBLMachinePlugin.Pos(type,x,y,z);
                    GRBLMachinePlugin.RotaryPos(type,rotary);
                    break;
                  case "OV":
                    break;
                }
              }
            }
          }
        }
      }
      else if (message.Equals    ("ok",   StringComparison.OrdinalIgnoreCase))
      {
        lock (_writeQueue)
        {
          if (_writeQueue.Count == 0)
            _writeSemaphore.Reset(BUFMAX);
          else
          {
            int n = _writeQueue.Dequeue();

            for (int i = 0; i != n; i++)
              _writeSemaphore.Release();
          }
        }

        GRBLMachinePlugin.Respond(GRBLMachinePlugin.Response.ok);
      }
      else if (message.StartsWith("error",StringComparison.OrdinalIgnoreCase))
      {
        string[] error = message.Split(':');
        int      code  = (error.Length > 1 && GrblIsV1) ? int.Parse(error[1]) : 0;

        if (code > 0)
          GRBLMachinePlugin.Log(ErrorDict[code]);

        ClearWriteQueue();

        GRBLMachinePlugin.Respond(GRBLMachinePlugin.Response.error, code);
      }
      else if (message.StartsWith("alarm",StringComparison.OrdinalIgnoreCase))
      {
        string[] alarm = message.Split(':');
        int      code  = (alarm.Length > 1 && GrblIsV1) ? int.Parse(alarm[1]) : 0;

        if (code > 0)
          GRBLMachinePlugin.Log(AlarmDict[code]);

        ClearWriteQueue();

        GRBLMachinePlugin.Respond(GRBLMachinePlugin.Response    .alarm, code);
        GRBLMachinePlugin.Stat   (GRBLMachinePlugin.MachineState.Alarm, code);
      }
      else if (message.StartsWith("$N"))
      {
        string[] dollar = message.Split('=');

        switch (dollar[0])
        {
          case "$N0":  GRBLMachinePlugin.Props.Startup1                    =                  dollar[1];break; //Startup Block 0
          case "$N1":  GRBLMachinePlugin.Props.Startup2                    =                  dollar[1];break; //Startup Block 0
        }

        if (grid.SelectedObject == GRBLMachinePlugin.Props)
          grid.Refresh();
      }
      else if (message.StartsWith("$"))
      {
        string[] dollar = message.Split('=',';','(');
        double   val    = double.Parse(dollar[1], EN_US);

        switch (dollar[0])
        {
          case "$0":   GRBLMachinePlugin.Props.StepPulseTime               = (int)            val;      break; //(Step pulse time, microseconds)
          case "$1":   GRBLMachinePlugin.Props.StepIdleDelay               = (int)            val;      break; //(Step idle delay, milliseconds)
          case "$2":   GRBLMachinePlugin.Props.StepPulseInvert             = (InvertMask)     val;      break; //(Step pulse invert, mask)
          case "$3":   GRBLMachinePlugin.Props.StepDirectionInvert         = (InvertMask)     val;      break; //(Step direction invert, mask)
          case "$4":   GRBLMachinePlugin.Props.InvertStepEnablePin         =                  val != 0; break; //(Invert step enable pin, boolean)
          case "$5":   GRBLMachinePlugin.Props.InvertLimitPins             =                  val != 0; break; //(Invert limit pins, boolean)
          case "$6":   GRBLMachinePlugin.Props.InvertProbePin              =                  val != 0; break; //(Invert probe pin, boolean)
          case "$10":  GRBLMachinePlugin.Props.StatusReportOptions         = (int)            val;      break; //(Status report options, mask)
          case "$11":  GRBLMachinePlugin.Props.JunctionDeviation           =                  val;      break; //(Junction deviation, millimeters)
          case "$12":  GRBLMachinePlugin.Props.ArcTolerance                =                  val;      break; //(Arc tolerance, millimeters)
          case "$13":  GRBLMachinePlugin.Props.ReportInInches              = (InchMM)         val;      GRBLMachinePlugin.PropertyChange("ReportInInches",(InchMM)val);
                                                                                                        break; //(Report in inches, boolean)
          case "$20":  GRBLMachinePlugin.Props.SoftLimitsEnable            = (EnabledDisabled)val;      break; //(Soft limits enable, boolean)
          case "$21":  GRBLMachinePlugin.Props.HardLimitsEnable            = (EnabledDisabled)val;      break; //(Hard limits enable, boolean)
          case "$22":  GRBLMachinePlugin.Props.HomingCycleEnable           = (EnabledDisabled)val;      GRBLMachinePlugin.PropertyChange("HomingCycleEnable",(EnabledDisabled)val);
                                                                                                        break; //(Homing cycle enable, boolean)
          case "$23":  GRBLMachinePlugin.Props.HomingDirectionInvert       = (InvertMask)     val;      break; //(Homing direction invert, mask)
          case "$24":  GRBLMachinePlugin.Props.HomingLocateFeedRate        =                  val;      break; //(Homing locate feed rate, mm/min)
          case "$25":  GRBLMachinePlugin.Props.HomingSearchSeekRate        =                  val;      break; //(Homing search seek rate, mm/min)
          case "$26":  GRBLMachinePlugin.Props.HomingSwitchDebounceDelay   = (int            )val;      break; //(Homing switch debounce delay, milliseconds)
          case "$27":  GRBLMachinePlugin.Props.HomingSwitchPullOffDistance =                  val;      break; //(Homing switch pull-off distance, millimeters)
          case "$30":  GRBLMachinePlugin.Props.MaximumSpindleSpeed         = (int)            val;      break; //(Maximum spindle speed, RPM)
          case "$31":  GRBLMachinePlugin.Props.MinimumSpindleSpeed         = (int)            val;      break; //(Minimum spindle speed, RPM)
          case "$32":  GRBLMachinePlugin.Props.LaserModeEnable             = (EnabledDisabled)val;      GRBLMachinePlugin.PropertyChange("LaserModeEnable",(EnabledDisabled)val);
                                                                                                        break; //(Laser-mode enable, boolean)
          case "$100": GRBLMachinePlugin.Props.X.Resolution                =                  val;      break; //(X-axis travel resolution, step/mm)
          case "$101": GRBLMachinePlugin.Props.Y.Resolution                =                  val;      break; //(Y-axis travel resolution, step/mm)
          case "$102": GRBLMachinePlugin.Props.Z.Resolution                =                  val;      break; //(Z-axis travel resolution, step/mm)
          case "$110": GRBLMachinePlugin.Props.X.MaximumRate               =                  val;      break; //(X-axis maximum rate, mm/min)
          case "$111": GRBLMachinePlugin.Props.Y.MaximumRate               =                  val;      break; //(Y-axis maximum rate, mm/min)
          case "$112": GRBLMachinePlugin.Props.Z.MaximumRate               =                  val;      break; //(Z-axis maximum rate, mm/min)
          case "$120": GRBLMachinePlugin.Props.X.Acceleration              =                  val;      break; //(X-axis acceleration, mm/sec^2)
          case "$121": GRBLMachinePlugin.Props.Y.Acceleration              =                  val;      break; //(Y-axis acceleration, mm/sec^2)
          case "$122": GRBLMachinePlugin.Props.Z.Acceleration              =                  val;      break; //(Z-axis acceleration, mm/sec^2)
          case "$130": GRBLMachinePlugin.Props.X.AxisMaximumTravel         =                  val;      GRBLMachinePlugin.PropertyChange("AxisMaximumTravel",val);
                                                                                                        break; //(X-axis maximum travel, millimeters)
          case "$131": GRBLMachinePlugin.Props.Y.AxisMaximumTravel         =                  val;      GRBLMachinePlugin.PropertyChange("AxisMaximumTravel",val);
                                                                                                        break; //(Y-axis maximum travel, millimeters)
          case "$132": GRBLMachinePlugin.Props.Z.AxisMaximumTravel         =                  val;      GRBLMachinePlugin.PropertyChange("AxisMaximumTravel",val);
                                                                                                        break; //(Z-axis maximum travel, millimeters)
          default:
            break;
        }

        if (grid.SelectedObject == GRBLMachinePlugin.Props)
          grid.Refresh();
      }
    }
    
#endregion // Privates

#region Eventhandlers

    private void Props_PropertyChanged(string name, object newvalue)
    {
      switch (name)
      {
        case "COMPort":     COMPort.SelectedItem  = newvalue;             break;
        case "Baudrate":    Baudrate.SelectedItem = newvalue.ToString();  break;
        case "AutoConnect": AutoConnect.Checked   = (bool)newvalue;       break;
        case "EthernetEnabled": _ethernetMode.Checked = (bool)newvalue; UpdateConnectionModeControls(); break;
        case "EthernetHost": _ethernetHost.Text = newvalue as string ?? string.Empty; break;
        case "EthernetPort": _ethernetPort.Value = Math.Max(1, Math.Min(65535, Convert.ToDecimal(newvalue))); break;
      }
    }

    private void EthernetMode_CheckedChanged(object sender, EventArgs e)
    {
      GRBLMachinePlugin.Props.EthernetEnabled = _ethernetMode.Checked;
      UpdateConnectionModeControls();
      if (CamBamUI.MainUI.ObjectProperties.SelectedObject == GRBLMachinePlugin.Props)
        CamBamUI.MainUI.ObjectProperties.Refresh();
    }

    private void EthernetHost_TextChanged(object sender, EventArgs e)
    {
      GRBLMachinePlugin.Props.EthernetHost = _ethernetHost.Text.Trim();
    }

    private void EthernetPort_ValueChanged(object sender, EventArgs e)
    {
      GRBLMachinePlugin.Props.EthernetPort = (int)_ethernetPort.Value;
    }

    private void UpdateConnectionModeControls()
    {
      if (_ethernetMode == null)
        return;

      bool ethernet = _ethernetMode.Checked;
      label1.Visible = COMPort.Visible = RefreshButton.Visible = !ethernet;
      label2.Visible = Baudrate.Visible = !ethernet;
      _ethernetHostLabel.Visible = _ethernetHost.Visible = ethernet;
      _ethernetPortLabel.Visible = _ethernetPort.Visible = ethernet;
      bool connected = IsPhysicalConnectionOpen || _simulatorActive;
      _ethernetMode.Enabled = !connected;
      _ethernetHost.Enabled = !connected;
      _ethernetPort.Enabled = !connected;
    }

    private void DeviceInsertedEvent(object sender, EventArrivedEventArgs e)
    {
      ManagementBaseObject instance  = (ManagementBaseObject)e.NewEvent["TargetInstance"];
      string               classGUID = instance["ClassGuid"] as string;
        

      if (GUID_DEVCLASS_PORTS.Equals(classGUID, StringComparison.OrdinalIgnoreCase))
      {
        InvokeOnUI(() => 
        {
          RefreshButton_Click(this, EventArgs.Empty);
        });
      }
    }

    private void DeviceRemovedEvent(object sender, EventArrivedEventArgs e)
    {
      ManagementBaseObject instance  = (ManagementBaseObject)e.NewEvent["TargetInstance"];
      string               classGUID = instance["ClassGuid"] as string;
        

      if (GUID_DEVCLASS_PORTS.Equals(classGUID, StringComparison.OrdinalIgnoreCase))
      {
        string name = instance["Name"] as string;

        if (!_simulatorActive && !GRBLMachinePlugin.Props.EthernetEnabled && name != null && name.Contains(GRBLMachinePlugin.Props.COMPort))
          CloseCOMPort(true);

        InvokeOnUI(() => 
        {
          RefreshButton_Click(this, EventArgs.Empty);
        });
      }
    }

    private void RefreshButton_Click(object sender, EventArgs e)
    {
      string oldPort = COMPort.SelectedItem as string;

      COMPort.Items.Clear();
      COMPort.Items.AddRange(SerialPort.GetPortNames());
      COMPort.SelectedItem = oldPort;

      if (COMPort.SelectedItem == null)
        CloseCOMPort(true);
    }

    private void COMPort_SelectionChangeCommitted(object sender, EventArgs e)
    {
      GRBLMachinePlugin.Props.COMPort = COMPort.SelectedItem as string;

      if (CamBamUI.MainUI.ObjectProperties.SelectedObject == GRBLMachinePlugin.Props)
        CamBamUI.MainUI.ObjectProperties.Refresh();
    }

    private void Baudrate_SelectionChangeCommitted(object sender, EventArgs e)
    {
      GRBLMachinePlugin.Props.Baudrate = int.Parse(Baudrate.SelectedItem as string);

      if (CamBamUI.MainUI.ObjectProperties.SelectedObject == GRBLMachinePlugin.Props)
        CamBamUI.MainUI.ObjectProperties.Refresh();
    }

    private void AutoConnect_CheckedChanged(object sender, EventArgs e)
    {
      GRBLMachinePlugin.Props.AutoConnect = AutoConnect.Checked;

      if (CamBamUI.MainUI.ObjectProperties.SelectedObject == GRBLMachinePlugin.Props)
        CamBamUI.MainUI.ObjectProperties.Refresh();
    }

    private void ConnectButton_Click(object sender, EventArgs e)
    {
      if (_simulatorActive)
        return;

      if (IsPhysicalConnectionOpen)
        CloseCOMPort(true);
      else if ((GRBLMachinePlugin.Props.EthernetEnabled && !string.IsNullOrEmpty(GRBLMachinePlugin.Props.EthernetHost) && GRBLMachinePlugin.Props.EthernetHost.Trim().Length > 0 && GRBLMachinePlugin.Props.EthernetPort > 0)
        || (!GRBLMachinePlugin.Props.EthernetEnabled && !string.IsNullOrEmpty(GRBLMachinePlugin.Props.COMPort) && GRBLMachinePlugin.Props.Baudrate != 0))
        OpenCOMPort(true);
    }

    private void StatusTimer_Tick(object sender, EventArgs e)
    {
      WriteCOMPort('?');
    }

    private void ConnectionExpander_Disconnected()
    {
      StatusTimer  .Enabled         = false;
      COMPort      .Enabled         = true;
      RefreshButton.Enabled         = true;
      Baudrate     .Enabled         = true;
      AutoConnect  .Enabled         = true;
      ConnectButton.Text            = "CONNECT !";
      ConnectButton.BackgroundImage = Resources.media_stop;
      ConnectButton.Enabled         = true;
      _simulatorProfile.Enabled     = true;
      _simulatorButton.Text         = "START SIM";
      _simulatorButton.ForeColor    = Color.DarkBlue;
      _simulatorButton.Enabled      = true;
      UpdateConnectionModeControls();
      VersionLabel.Text             = "-.-";
      VersionLabel.ForeColor        = SystemColors.ControlText;
    }

    private void ConnectionExpander_Connected()
    {
      COMPort      .Enabled         = false;
      RefreshButton.Enabled         = false;
      Baudrate     .Enabled         = false;
      ConnectButton.Text            = "CLOSE !";
      ConnectButton.BackgroundImage = Resources.media_stop_red;
      ConnectButton.Enabled         = !_simulatorActive;
      AutoConnect.Enabled           = !_simulatorActive;
      _simulatorProfile.Enabled     = false;
      _simulatorButton.Text         = _simulatorActive ? "STOP SIM" : "START SIM";
      _simulatorButton.ForeColor    = _simulatorActive ? Color.DarkRed : Color.DarkBlue;
      _simulatorButton.Enabled      = _simulatorActive || !IsPhysicalConnectionOpen;
      UpdateConnectionModeControls();
    }

    #endregion  // privates

  }
}
