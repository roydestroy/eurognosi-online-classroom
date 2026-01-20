using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace DailyDesktopApp
{
    public partial class App : Application
    {
        private const string MutexName = "Global\\DailyDesktopApp_SingleInstance";
        private static Mutex? _mutex;

        protected override async void OnStartup(StartupEventArgs e)
        {
            // ---- Single-instance gate ----
            bool createdNew;
            _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out createdNew);

            if (!createdNew)
            {
                // Another instance is already running -> exit quietly
                Shutdown();
                return;
            }
            // ------------------------------

            base.OnStartup(e);

            // Optional: background update check
            _ = Task.Run(async () =>
            {
                try { await UpdateService.CheckForUpdatesAsync(); }
                catch { /* swallow errors so app still starts */ }
            });

            var main = new MainWindow();
            main.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
            }
            catch
            {
                // ignore
            }

            base.OnExit(e);
        }
    }
}
