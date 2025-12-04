using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace DailyDesktopApp
{
    public partial class HandRaiseOverlayWindow : Window
    {
        // Win32 constants
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080; // hide from Alt+Tab

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private bool _isClosing = false;

        // Default: show emoji (hand-raise)
        public HandRaiseOverlayWindow(string message)
            : this(message, true)
        {
        }

        // Generic ctor: can hide emoji for chat
        public HandRaiseOverlayWindow(string message, bool showEmoji)
        {
            InitializeComponent();

            // start transparent so FadeIn looks smooth
            Opacity = 0;

            DetailsText.Text = message?.Trim() ?? string.Empty;
            EmojiImage.Visibility = showEmoji ? Visibility.Visible : Visibility.Collapsed;

            // also set in XAML, but safe here too
            SizeToContent = SizeToContent.WidthAndHeight;

            // run fade-in once the window is properly laid out
            Loaded += (_, __) => FadeIn();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            MakeClickThrough();
        }

        private void MakeClickThrough()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        }

        private void FadeIn()
        {
            try
            {
                var anim = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(200),
                    FillBehavior = FillBehavior.Stop
                };

                // when animation finishes, leave opacity at 1
                anim.Completed += (_, __) => Opacity = 1;

                BeginAnimation(Window.OpacityProperty, anim);
            }
            catch
            {
                // if anything goes wrong, just jump to fully visible
                Opacity = 1;
            }
        }

        public void FadeOutAndClose()
        {
            if (_isClosing) return;
            _isClosing = true;

            try
            {
                var anim = new DoubleAnimation
                {
                    From = Opacity,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(200),
                    FillBehavior = FillBehavior.Stop
                };

                anim.Completed += (_, __) =>
                {
                    try { Close(); } catch { }
                };

                BeginAnimation(Window.OpacityProperty, anim);
            }
            catch
            {
                try { Close(); } catch { }
            }
        }
    }
}
