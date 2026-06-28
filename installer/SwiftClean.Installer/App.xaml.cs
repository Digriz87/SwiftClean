using System.Linq;
using System.Windows;
using SwiftClean.Installer.ViewModels;

namespace SwiftClean.Installer;

/// <summary>Interaction logic for App.xaml. No StartupUri — the mode (install/uninstall) is chosen here.</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool Has(string flag) => e.Args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

        var uninstall = Has("/uninstall") || Has("--uninstall");
        var silent = Has("/silent") || Has("--silent");

        var vm = new InstallerViewModel(uninstallMode: uninstall, silent: silent);

        if (uninstall && silent)
        {
            // Headless uninstall (QuietUninstallString): no window, exit when done.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            vm.RequestClose += _ => Shutdown();
            return;
        }

        var window = new MainWindow { DataContext = vm };
        vm.RequestClose += launch =>
        {
            if (launch && !uninstall)
                Services.InstallerService.LaunchApp(vm.TargetDir);
            window.Close();
        };
        window.Show();
    }
}
