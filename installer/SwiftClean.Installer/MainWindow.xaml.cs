using System.Windows;
using System.Windows.Input;

namespace SwiftClean.Installer;

/// <summary>The installer window. Code-behind is window-chrome only; all logic is in the view model.</summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
