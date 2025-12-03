using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;   // 👈 add this

namespace DailyDesktopApp
{
    public partial class HandRaiseOverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private bool _isClosing = false;

        public string Message
        {
            get => DetailsText.Text;
            set => DetailsText.Text = value;
        }

        public HandRaiseOverlayWindow(string message)
        {
            InitializeComponent();

            DetailsText.Text = message.Trim();
            this.SizeToContent = SizeToContent.WidthAndHeight;
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
            exStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        }

        private void FadeIn()
        {
            var anim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200)
            };
            BeginAnimation(Window.OpacityProperty, anim);
        }

        public void FadeOutAndClose()
        {
            if (_isClosing) return;
            _isClosing = true;

            var anim = new DoubleAnimation
            {
                From = Opacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200)
            };
            anim.Completed += (_, __) =>
            {
                try { Close(); } catch { }
            };
            BeginAnimation(Window.OpacityProperty, anim);
        }
    }
}
