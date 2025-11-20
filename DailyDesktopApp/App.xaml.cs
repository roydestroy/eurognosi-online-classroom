using System.Threading.Tasks;
using System.Windows;

namespace DailyDesktopApp
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Fire and forget – if it fails, the app still starts
            _ = UpdateService.CheckForUpdatesAsync();

            // then show your main window
            var main = new MainWindow();
            main.Show();
        }
    }
}
