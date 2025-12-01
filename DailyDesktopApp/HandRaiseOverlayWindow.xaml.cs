using System.Windows;
using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DailyDesktopApp
{
    public partial class HandRaiseOverlayWindow : Window
    {
        // Win32 constants
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080; // optional: hide from Alt+Tab

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public HandRaiseOverlayWindow(string namesText, int count)
        {
            InitializeComponent();

            if (count <= 1)
            {
                DetailsText.Text = $"{namesText} has raised their hand.";
            }
            else
            {
                DetailsText.Text = $"{namesText} have raised their hands.";
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            MakeClickThrough();
        }

        private void MakeClickThrough()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

            // Make window transparent to mouse + treat as tool window (no Alt+Tab)
            exStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;

            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        }
    }
}

