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
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Joystick state
        private bool isJoystickActive = false;
        private Point joystickCenter = new Point(90, 90);
        private const double joystickRadius = 65; // matches UI circle radius

        // Serial
        private SerialPort serialPort;

        public MainWindow()
        {
            InitializeComponent();

            // Wire up serial UI events
            RefreshPortsButton.Click += RefreshPortsButton_Click;
            ConnectButton.Checked += ConnectButton_Checked;
            ConnectButton.Unchecked += ConnectButton_Unchecked;
            startButton.Click += startButtonClick;

            // Compute joystick center after loaded (in case sizes change)
            Loaded += MainWindow_Loaded;

            // Initial population of ports and baud
            PopulateBaudRates();
            RefreshSerialPorts();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (JoystickCanvas != null)
            {
                joystickCenter = new Point(JoystickCanvas.ActualWidth / 2.0, JoystickCanvas.ActualHeight / 2.0);
                // center knob visually
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
            // vector from center
            double dx = position.X - joystickCenter.X;
            double dy = position.Y - joystickCenter.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance > joystickRadius)
            {
                double angle = Math.Atan2(dy, dx);
                dx = Math.Cos(angle) * joystickRadius;
                dy = Math.Sin(angle) * joystickRadius;
            }

            // move knob
            double knobLeft = joystickCenter.X + dx - (JoystickKnob.Width / 2.0);
            double knobTop = joystickCenter.Y + dy - (JoystickKnob.Height / 2.0);
            JoystickKnob.SetValue(Canvas.LeftProperty, knobLeft);
            JoystickKnob.SetValue(Canvas.TopProperty, knobTop);

            // normalized output (-1..1)
            double normalizedX = dx / joystickRadius;
            double normalizedY = -dy / joystickRadius; // invert Y for natural forward

            LogStatus($"Joystick: X={normalizedX:F2}, Y={normalizedY:F2}");

            SendJoystickCommand(normalizedX, normalizedY);
        }

        private void ResetJoystickPosition()
        {
            // return knob to center
            double knobLeft = joystickCenter.X - (JoystickKnob.Width / 2.0);
            double knobTop = joystickCenter.Y - (JoystickKnob.Height / 2.0);
            JoystickKnob.SetValue(Canvas.LeftProperty, knobLeft);
            JoystickKnob.SetValue(Canvas.TopProperty, knobTop);

            LogStatus("Joystick: reset");
            SendJoystickCommand(0, 0);
        }
        #endregion

        #region Serial / UART
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
            // Attempt to open serial port
            var portName = (PortComboBox.SelectedItem as string) ?? (PortComboBox.SelectedItem is ComboBoxItem cbi ? cbi.Content.ToString() : null);
            if (string.IsNullOrWhiteSpace(portName) && PortComboBox.Text != null) portName = PortComboBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(portName))
            {
                LogStatus("No port selected");
                ConnectButton.IsChecked = false;
                return;
            }

            int baud = 115200;
            try { baud = int.Parse((BaudComboBox.SelectedItem as string) ?? (BaudComboBox.SelectedItem is ComboBoxItem bci ? bci.Content.ToString() : BaudComboBox.Text)); }
            catch { }

            var parityStr = (ParityComboBox.SelectedItem as string) ?? (ParityComboBox.SelectedItem is ComboBoxItem pci ? pci.Content.ToString() : "None");
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
                ConnectionIndicator.Fill = connected ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)) : new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));
                ConnectionStatusText.Text = status;
            });
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                var sp = (SerialPort)sender;
                string line = sp.ReadLine();
                // Basic parsing: push to log and try to update sensor/odometry if formatted
                Dispatcher.Invoke(() => LogStatus($"RX: {line.Trim()}"));

                // Example parsing (customize to your STM32 protocol):
                // SENSOR:TEMP,SMOKE,FLAME,BATT
                // ODOM:X,Y,THETA
                var parts = line.Trim().Split(':');
                if (parts.Length >= 2)
                {
                    var tag = parts[0].ToUpperInvariant();
                    var payload = parts[1];
                    if (tag == "SENSOR")
                    {
                        var vals = payload.Split(',');
                        if (vals.Length >= 4)
                        {
                            if (double.TryParse(vals[0], out double temp) && int.TryParse(vals[3], out int batt))
                            {
                                var smoke = vals[1];
                                var flame = vals[2] == "1" || vals[2].Equals("yes", StringComparison.OrdinalIgnoreCase);
                                Dispatcher.Invoke(() => UpdateSensorReadings(temp, smoke, flame, batt));
                            }
                        }
                    }
                    else if (tag == "ODOM")
                    {
                        var vals = payload.Split(',');
                        if (vals.Length >= 3)
                        {
                            if (double.TryParse(vals[0], out double ox) && double.TryParse(vals[1], out double oy) && double.TryParse(vals[2], out double th))
                            {
                                Dispatcher.Invoke(() => UpdateOdometry(ox, oy, th));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => LogStatus($"Serial RX error: {ex.Message}"));
            }
        }

        private void SendJoystickCommand(double x, double y)
        {
            // Format command for your STM32 + nRF link. Keep simple: J:x,y\n
            string cmd = $"J:{x:F2},{y:F2}\n";
            try
            {
                if (serialPort != null && serialPort.IsOpen)
                {
                    serialPort.WriteLine(cmd);
                    LogStatus($"TX: {cmd.Trim()}");
                }
            }
            catch (Exception ex)
            {
                LogStatus($"Serial TX error: {ex.Message}");
            }
        }
        #endregion

        #region UI helpers
        private void LogStatus(string text)
        {
            Dispatcher.Invoke(() =>
            {
                if (StatusLogBox != null)
                {
                    StatusLogBox.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {text}");
                    while (StatusLogBox.Items.Count > 50) StatusLogBox.Items.RemoveAt(StatusLogBox.Items.Count - 1);
                }
            });
        }
        #endregion

        #region Public update methods
        // Call from your code when new sensor data arrives
        public void UpdateSensorReadings(double temperature, string smokeLevel, bool flameDetected, int battery)
        {
            TemperatureValue.Text = $"{temperature:F1} °C";
            SmokeLevelValue.Text = smokeLevel ?? "--";
            FlameDetectedValue.Text = flameDetected ? "Yes" : "No";
            BatteryValue.Text = $"{battery}%";
        }

        // Call from your code when odometry updates arrive
        public void UpdateOdometry(double x, double y, double theta)
        {
            PositionXValue.Text = $"{x:F2} m";
            PositionYValue.Text = $"{y:F2} m";
            PositionThetaValue.Text = $"{theta:F2}°";

            // Map to canvas coordinates (centered at 200,150 as in XAML)
            double canvasX = 200 + (x * 20);
            double canvasY = 150 - (y * 20);

            // Move robot indicator and add to trail
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
                    // limit trail length
                    if (MovementTrail.Points.Count > 500) MovementTrail.Points.RemoveAt(0);
                }
            }
        }

        private void startButtonClick(object sender, RoutedEventArgs e)
        {
            float testdata = 5000.0f;
            byte tag = 0xed;
            
            sendBinaryPacket(tag, testdata);

            // Placeholder for start button action
            LogStatus("Start button clicked");
        }

        private void sendBinaryPacket(byte tag, float data)
        {
            if (serialPort == null || !serialPort.IsOpen)
            {
                LogStatus("Error: Serial port not open.");
                return;
            }

            try
            {
                byte[] floatData = BitConverter.GetBytes(data);
                byte[] packet = new byte[5];

                packet[0] = tag;
                packet[1] = floatData[0];
                packet[2] = floatData[1];
                packet[3] = floatData[2];
                packet[4] = floatData[3];

                serialPort.Write(packet, 0, packet.Length);
            }
            catch (Exception ex)
            {
                LogStatus("Error sending binary: " + ex.Message);
            }
        }
        #endregion
    }
}
