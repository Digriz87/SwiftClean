using System.Linq;
using System.Threading;
using System.Windows;
using SwiftClean.Services;

namespace SwiftClean
{
    /// <summary>Interaction logic for App.xaml.</summary>
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Scheduled task launches us with --autoclean: run a silent cleanup and exit.
            if (e.Args.Any(a => string.Equals(a, "--autoclean", System.StringComparison.OrdinalIgnoreCase)))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown; // no window — stay alive until done
                await RunAutoCleanAsync();
                Shutdown();
                return;
            }

            new MainWindow().Show();
        }

        // Scans and recycles every safe junk category without any UI.
        private static async System.Threading.Tasks.Task RunAutoCleanAsync()
        {
            try
            {
                var result = await new ScannerService().ScanAsync(null, CancellationToken.None);
                var targets = result.Categories.Where(c => c.IsSafeToDelete).ToList();
                if (targets.Count > 0)
                    await new CleanerService().CleanAsync(targets, null, CancellationToken.None);
            }
            catch (System.Exception)
            {
                // Best-effort background clean; never surface errors.
            }
        }
    }
}
