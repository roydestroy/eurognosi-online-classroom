using System;
using System.Windows;
using Velopack;

namespace DailyDesktopApp
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Let Velopack handle install/update events (shortcuts, uninstall, etc.)
            VelopackApp.Build()
                // Optional first-run hook:
                // .WithFirstRun(v => MessageBox.Show("Thanks for installing EUROGNOSI Online Classroom!"))
                .Run();

            // Start the normal WPF app
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
