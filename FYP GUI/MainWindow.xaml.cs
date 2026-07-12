using System;
using System.IO.Ports;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FYP_GUI
{
    public partial class MainWindow : Window
    {
        private bool isJoystickActive = false;
        private Point joystickCenter = new Point(90, 90);
        private const double joystickRadius = 65;
        float max_wheel_velocity = 70000.00f;

        private SerialPort serialPort;

        private const byte TAG_NRF_CMD = 0xAA; 
        private const byte TAG_NRF_FB = 0xCC;  
        private const byte TAG_LORA_TELEM = 0xBB; 
        private const byte TAG_LORA_STOP = 0xE0; 
        private const byte TAG_LORA_RECAL = 0xE1;  
        private const byte TAG_LORA_INIT = 0xE2; 

        // Init ACK state
        private bool _waitingForInitAck = false;

        public MainWindow()
        {
            InitializeComponent();

            RefreshPortsButton.Click += RefreshPortsButton_Click;
            ConnectButton.Checked += ConnectButton_Checked;
            ConnectButton.Unchecked += ConnectButton_Unchecked;
            startButton.Click += startButtonClick;

            // Supervisory buttons
            EmergencyStopButton.Click += EmergencyStopButton_Click;
            RecalibrateButton.Click += RecalibrateButton_Click;
            ReadInitButton.Click += ReadInitButton_Click;

            Loaded += MainWindow_Loaded;

            PopulateBaudRates();
            RefreshSerialPorts();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (JoystickCanvas != null)
            {
                joystickCenter = new Point(JoystickCanvas.ActualWidth / 2.0, JoystickCanvas.ActualHeight / 2.0);
                JoystickKnob.SetValue(Canvas.LeftProperty, joystickCenter.X - (JoystickKnob.Width / 2.0));
                JoystickKnob.SetValue(Canvas.TopProperty, joystickCenter.Y - (JoystickKnob.Height / 2.0));
            }
        }

        #region Joystick Handlers
        private void JoystickCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            isJoystickActive = true;
            JoystickCanvas.CaptureMouse();
            UpdateJoystickPosition(e.GetPosition(JoystickCanvas));
        }

        private void JoystickCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isJoystickActive) return;
            UpdateJoystickPosition(e.GetPosition(JoystickCanvas));
        }

        private void JoystickCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!isJoystickActive) return;
            isJoystickActive = false;
            JoystickCanvas.ReleaseMouseCapture();
            ResetJoystickPosition();
        }

        private void JoystickCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!isJoystickActive) return;
            isJoystickActive = false;
            JoystickCanvas.ReleaseMouseCapture();
            ResetJoystickPosition();
        }

        private void UpdateJoystickPosition(Point position)
        {
            double dx = position.X - joystickCenter.X;
            double dy = position.Y - joystickCenter.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance > joystickRadius)
            {
                double angle = Math.Atan2(dy, dx);
                dx = Math.Cos(angle) * joystickRadius;
                dy = Math.Sin(angle) * joystickRadius;
            }

            double knobLeft = joystickCenter.X + dx - (JoystickKnob.Width / 2.0);
            double knobTop = joystickCenter.Y + dy - (JoystickKnob.Height / 2.0);
            JoystickKnob.SetValue(Canvas.LeftProperty, knobLeft);
            JoystickKnob.SetValue(Canvas.TopProperty, knobTop);

            // Normalised -1..1, Y inverted for natural forward
            double normalizedX = dx / joystickRadius;
            double normalizedY = -dy / joystickRadius;

            LogStatus($"Joystick: X={normalizedX:F2}, Y={normalizedY:F2}");
            SendJoystickCommand(normalizedX, normalizedY);
        }

        private void ResetJoystickPosition()
        {
            double knobLeft = joystickCenter.X - (JoystickKnob.Width / 2.0);
            double knobTop = joystickCenter.Y - (JoystickKnob.Height / 2.0);
            JoystickKnob.SetValue(Canvas.LeftProperty, knobLeft);
            JoystickKnob.SetValue(Canvas.TopProperty, knobTop);

            LogStatus("Joystick: reset");
            SendJoystickCommand(0, 0);
        }
        #endregion

        #region Serial
        private void PopulateBaudRates()
        {
            if (BaudComboBox != null)
            {
                BaudComboBox.Items.Clear();
                var bauds = new[] { "9600", "19200", "38400", "57600", "115200" };
                foreach (var b in bauds) BaudComboBox.Items.Add(b);
                BaudComboBox.SelectedIndex = 4; // 115200 default
            }

            if (ParityComboBox != null)
            {
                ParityComboBox.Items.Clear();
                ParityComboBox.Items.Add("None");
                ParityComboBox.Items.Add("Odd");
                ParityComboBox.Items.Add("Even");
                ParityComboBox.SelectedIndex = 0;
            }
        }

        private void RefreshPortsButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshSerialPorts();
        }

        private void RefreshSerialPorts()
        {
            try
            {
                var ports = SerialPort.GetPortNames().OrderBy(p => p).ToArray();
                PortComboBox.Items.Clear();
                foreach (var p in ports) PortComboBox.Items.Add(p);
                if (ports.Length > 0) PortComboBox.SelectedIndex = 0;
                LogStatus($"Found ports: {string.Join(",", ports)}");
            }
            catch (Exception ex)
            {
                LogStatus($"Error enumerating ports: {ex.Message}");
            }
        }

        private void ConnectButton_Checked(object sender, RoutedEventArgs e)
        {
            var portName = (PortComboBox.SelectedItem as string)
                        ?? (PortComboBox.SelectedItem is ComboBoxItem cbi ? cbi.Content.ToString() : null);
            if (string.IsNullOrWhiteSpace(portName) && PortComboBox.Text != null)
                portName = PortComboBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(portName))
            {
                LogStatus("No port selected");
                ConnectButton.IsChecked = false;
                return;
            }

            int baud = 115200;
            try
            {
                baud = int.Parse(
                    (BaudComboBox.SelectedItem as string)
                    ?? (BaudComboBox.SelectedItem is ComboBoxItem bci ? bci.Content.ToString() : BaudComboBox.Text));
            }
            catch { }

            var parityStr = (ParityComboBox.SelectedItem as string)
                         ?? (ParityComboBox.SelectedItem is ComboBoxItem pci ? pci.Content.ToString() : "None");
            Parity parity = Parity.None;
            if (parityStr != null)
            {
                if (parityStr.Equals("Odd", StringComparison.OrdinalIgnoreCase)) parity = Parity.Odd;
                else if (parityStr.Equals("Even", StringComparison.OrdinalIgnoreCase)) parity = Parity.Even;
            }

            try
            {
                serialPort = new SerialPort(portName, baud, parity);
                serialPort.ReadTimeout = 500;
                serialPort.WriteTimeout = 500;
                serialPort.DataReceived += SerialPort_DataReceived;
                serialPort.Open();
                UpdateConnectionUI(true, $"Connected to {portName} @ {baud}");
            }
            catch (Exception ex)
            {
                LogStatus($"Failed to open {portName}: {ex.Message}");
                ConnectButton.IsChecked = false;
                UpdateConnectionUI(false, "Disconnected");
            }
        }

        private void ConnectButton_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (serialPort != null)
                {
                    serialPort.DataReceived -= SerialPort_DataReceived;
                    if (serialPort.IsOpen) serialPort.Close();
                    serialPort.Dispose();
                    serialPort = null;
                }
            }
            catch (Exception ex)
            {
                LogStatus($"Error closing serial: {ex.Message}");
            }

            UpdateConnectionUI(false, "Disconnected");
        }

        private void UpdateConnectionUI(bool connected, string status)
        {
            Dispatcher.Invoke(() =>
            {
                ConnectionIndicator.Fill = connected
                    ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                    : new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));
                ConnectionStatusText.Text = status;
            });
        }
        #endregion

        #region RX Packet Parser
        private byte[] _rxBuf = new byte[256];
        private int _rxCount = 0;

        private const int SIZE_NRF_FB = 6;   // 0xCC
        private const int SIZE_LORA_TELEM = 9;  // 0xBB

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                var sp = (SerialPort)sender;
                int available = sp.BytesToRead;
                if (available <= 0) return;

                byte[] incoming = new byte[available];
                sp.Read(incoming, 0, available);

                foreach (byte b in incoming)
                {
                    if (_rxCount == 0)
                    {
                        if (b != TAG_NRF_FB && b != TAG_LORA_TELEM)
                            continue;
                    }

                    _rxBuf[_rxCount++] = b;

                    int expected = _rxBuf[0] == TAG_NRF_FB ? SIZE_NRF_FB : SIZE_LORA_TELEM;

                    if (_rxCount == expected)
                    {
                        byte tag = _rxBuf[0];
                        byte[] frame = new byte[expected];
                        Array.Copy(_rxBuf, frame, expected);
                        _rxCount = 0;

                        Dispatcher.Invoke(() => ParsePacket(tag, frame));
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => LogStatus($"Serial RX error: {ex.Message}"));
            }
        }

        private void ParsePacket(byte tag, byte[] frame)
        {
            switch (tag)
            {
                case TAG_NRF_FB:
                    {
                        short deltaL = BitConverter.ToInt16(frame, 1);
                        short deltaR = BitConverter.ToInt16(frame, 3);
                        byte status = frame[5];

                        UpdateNrfFeedback(deltaL, deltaR, status);
                        LogStatus($"RX nRF FB: dL={deltaL} dR={deltaR} status=0x{status:X2}");

                        // Decode status register bits
                        if ((status & 0x80) != 0) LogStatus("  >> STALL: Left motor");
                        if ((status & 0x40) != 0) LogStatus("  >> STALL: Right motor");
                        if ((status & 0x20) != 0) LogStatus("  >> FAULT: Encoder");
                        if ((status & 0x10) != 0) LogStatus("  >> Recalibration requested");
                        break;
                    }

                case TAG_LORA_TELEM:
                    {
                        float odomX = BitConverter.ToSingle(frame, 1);
                        float odomY = BitConverter.ToSingle(frame, 5);

                        if (_waitingForInitAck)
                        {
                            _waitingForInitAck = false;
                            LogStatus("Init ACK received.");
                        }

                        UpdateOdometry(odomX, odomY, 0);
                        LogStatus($"RX LoRa Telem: odomX={odomX:F2} odomY={odomY:F2}");
                        break;
                    }
            }
        }
        #endregion

        #region TX Packets
        private void SendJoystickCommand(double x, double y)
        {
            float velL = (float)((y + x) * max_wheel_velocity);
            float velR = (float)((y - x) * max_wheel_velocity);

            velL = Math.Max(-max_wheel_velocity, Math.Min(max_wheel_velocity, velL));
            velR = Math.Max(-max_wheel_velocity, Math.Min(max_wheel_velocity, velR));

            SendCmdPacket(TAG_NRF_CMD, velL, velR);
        }

        private void SendCmdPacket(byte tag, float velL, float velR)
        {
            if (serialPort == null || !serialPort.IsOpen)
            {
                LogStatus("Error: Serial port not open.");
                return;
            }

            try
            {
                byte[] packet = new byte[9];
                packet[0] = tag;
                Array.Copy(BitConverter.GetBytes(velL), 0, packet, 1, 4);
                Array.Copy(BitConverter.GetBytes(velR), 0, packet, 5, 4);

                serialPort.Write(packet, 0, packet.Length);
                LogStatus($"TX nRF CMD: tag=0x{tag:X2} velL={velL:F0} velR={velR:F0}");
            }
            catch (Exception ex)
            {
                LogStatus($"Error sending CMD packet: {ex.Message}");
            }
        }
        private void SendLoraCommand(byte tag)
        {
            if (serialPort == null || !serialPort.IsOpen)
            {
                LogStatus("Error: Serial port not open.");
                return;
            }

            try
            {
                serialPort.Write(new byte[] { tag }, 0, 1);
                LogStatus($"TX LoRa CMD: tag=0x{tag:X2}");
            }
            catch (Exception ex)
            {
                LogStatus($"Error sending LoRa command: {ex.Message}");
            }
        }
        #endregion

        #region Supervisory Button Handlers
        private void EmergencyStopButton_Click(object sender, RoutedEventArgs e)
        {
            SendCmdPacket(TAG_NRF_CMD, 0f, 0f);
            SendLoraCommand(TAG_LORA_STOP);
            LogStatus("EMERGENCY STOP sent.");
        }

        private void RecalibrateButton_Click(object sender, RoutedEventArgs e)
        {
            SendLoraCommand(TAG_LORA_RECAL);
            LogStatus("Recalibrate command sent.");
        }

        private void ReadInitButton_Click(object sender, RoutedEventArgs e)
        {
            _waitingForInitAck = true;
            SendLoraCommand(TAG_LORA_INIT);
            LogStatus("Read Init sent — waiting for ACK...");
        }
        #endregion

        #region UI Helpers
        private void LogStatus(string text)
        {
            Dispatcher.Invoke(() =>
            {
                if (StatusLogBox != null)
                {
                    StatusLogBox.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {text}");
                    while (StatusLogBox.Items.Count > 50)
                        StatusLogBox.Items.RemoveAt(StatusLogBox.Items.Count - 1);
                }
            });
        }
        #endregion

        #region Public Update Methods
        public void UpdateOdometry(double x, double y, double theta)
        {
            PositionXValue.Text = $"{x:F2} m";
            PositionYValue.Text = $"{y:F2} m";
            PositionThetaValue.Text = $"{theta:F2}°";

            double canvasX = 200 + (x * 20);
            double canvasY = 150 - (y * 20);

            RobotIndicator.SetValue(Canvas.LeftProperty, canvasX - 10);
            RobotIndicator.SetValue(Canvas.TopProperty, canvasY - 10);
            RobotIndicator.RenderTransform = new RotateTransform(theta, 10, 10);

            if (MovementTrail != null)
            {
                if (MovementTrail.Points.Count == 0 ||
                    Math.Abs(MovementTrail.Points[MovementTrail.Points.Count - 1].X - canvasX) > 2 ||
                    Math.Abs(MovementTrail.Points[MovementTrail.Points.Count - 1].Y - canvasY) > 2)
                {
                    MovementTrail.Points.Add(new Point(canvasX, canvasY));
                    if (MovementTrail.Points.Count > 500)
                        MovementTrail.Points.RemoveAt(0);
                }
            }
        }

        public void UpdateNrfFeedback(short deltaL, short deltaR, byte status)
        {
            NrfDeltaLValue.Text = deltaL.ToString();
            NrfDeltaRValue.Text = deltaR.ToString();

            bool stallL = (status & 0x80) != 0;
            bool stallR = (status & 0x40) != 0;
            bool encFault = (status & 0x20) != 0;
            bool recalib = (status & 0x10) != 0;

            StallLeftIndicator.Fill = stallL
                ? new SolidColorBrush(Colors.Red)
                : new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));
            StallRightIndicator.Fill = stallR
                ? new SolidColorBrush(Colors.Red)
                : new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));
            EncoderFaultIndicator.Fill = encFault
                ? new SolidColorBrush(Colors.Orange)
                : new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));
            RecalibIndicator.Fill = recalib
                ? new SolidColorBrush(Colors.Yellow)
                : new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));
        }

        private void startButtonClick(object sender, RoutedEventArgs e)
        {
            SendCmdPacket(TAG_NRF_CMD, 50.0f, 50.0f);
            LogStatus("Start clicked — test packet sent: L=50 R=50");
        }
        #endregion
    }
}