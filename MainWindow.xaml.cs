using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SwiftClean.ViewModels;

namespace SwiftClean
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml. Code-behind is limited to
    /// window-chrome concerns (drag, minimize/maximize/close); all app state
    /// lives in <see cref="MainViewModel"/>.
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            SourceInitialized += OnSourceInitialized;
            StateChanged += OnStateChanged;
            Activated += (_, _) => (DataContext as MainViewModel)?.OnWindowActivated();
        }

        // Click on the support overlay scrim closes it; clicks inside the card are swallowed.
        private void SupportOverlay_Click(object sender, MouseButtonEventArgs e)
            => (DataContext as MainViewModel)?.CloseSupportCommand.Execute(null);

        private void SupportCard_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
            => ToggleMaximize();

        private void CloseButton_Click(object sender, RoutedEventArgs e)
            => Close();

        private void ToggleMaximize()
            => WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

        private void OnStateChanged(object? sender, EventArgs e)
        {
            // Swap the maximize/restore glyph (Segoe MDL2 Assets): о¤Ј = restore, о¤ў = maximize.
            MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
        }

        // ----- Keep a borderless maximized window from covering the taskbar -----

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        }

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;
            if (msg == WM_GETMINMAXINFO)
            {
                AdjustMaximizedBounds(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static void AdjustMaximizedBounds(IntPtr hwnd, IntPtr lParam)
        {
            const int MONITOR_DEFAULTTONEAREST = 0x00000002;
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info))
                return;

            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var work = info.rcWork;
            var bounds = info.rcMonitor;

            mmi.ptMaxPosition.X = work.Left - bounds.Left;
            mmi.ptMaxPosition.Y = work.Top - bounds.Top;
            mmi.ptMaxSize.X = work.Right - work.Left;
            mmi.ptMaxSize.Y = work.Bottom - work.Top;
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }
    }
}
