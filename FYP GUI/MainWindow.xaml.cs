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
        public MainWindow()
        {
            InitializeComponent();
        }

        // Joystick event handler stubs - implement logic yourself
        private void JoystickCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // TODO: handle joystick press
        }

        private void JoystickCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            // TODO: handle joystick move
        }

        private void JoystickCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // TODO: handle joystick release
        }

        private void JoystickCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            // TODO: handle joystick leave
        }

    }
}
