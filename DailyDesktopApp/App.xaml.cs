using System.Threading.Tasks;
using System.Windows;

namespace DailyDesktopApp
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
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
    }
}
