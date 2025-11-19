using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;


namespace DailyDesktopApp
{
    public partial class MainWindow : Window
    {
        // URLs for Greek & English pages
        private const string GreekStartUrl = "https://www.eurognosi-fni.com/online-classroom-app?source=desktop";
        private const string EnglishStartUrl = "https://www.eurognosi-fni.com/en/online-classroom-app?source=desktop";

        private readonly string _windowStatePath;
        private bool _isDarkTheme = true;
        private bool _isEnglish = false; // default Greek
        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left)
            {
                // Double-click to maximize / restore
                ToggleMaxRestore();
            }
            else if (e.ButtonState == MouseButtonState.Pressed)
            {
                // Drag the window
                try
                {
                    DragMove();
                }
                catch
                {
                    // ignore small race conditions when clicking buttons
                }
            }
        }

        private void MinButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaxButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaxRestore();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleMaxRestore()
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                if (MaxButtonIcon != null)
                {
                    // Maximize icon
                    MaxButtonIcon.Text = "\uE922"; // square
                }
            }
            else
            {
                WindowState = WindowState.Maximized;
                if (MaxButtonIcon != null)
                {
                    // Restore icon
                    MaxButtonIcon.Text = "\uE923"; // overlapping squares
                }
            }
        }

        private string CurrentStartUrl => _isEnglish ? EnglishStartUrl : GreekStartUrl;

        public MainWindow()
        {
            InitializeComponent();

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appData, "EurognosiOnlineClassroom");
            _windowStatePath = Path.Combine(appFolder, "window-state.json");

            _isDarkTheme = true;
            _isEnglish = false;

            LoadWindowState();
            UpdateLanguageUi();

            Loaded += MainWindow_Loaded;
        }

        // ================================================
        //  RUN UPDATE CHECK *AFTER* WINDOW IS RENDERED
        // ================================================
        protected override async void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            try
            {
                await UpdateService.CheckForUpdatesAsync();
            }
            catch
            {
                // silently ignore update errors
            }
        }

        // ================================================
        //  INITIALIZE WEBVIEW + LOAD CSS
        // ================================================
        private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "Initializing browser…";

                await DailyWebView.EnsureCoreWebView2Async(null);

                await DailyWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                    (function () {
                        function injectNoScroll() {
                            try {
                                if (document.querySelector('style[data-eg-no-scroll]'))
                                    return;

                                var css = `
                                    html, body {
                                        overflow: hidden !important;
                                        height: 100% !important;
                                    }
                                    #SITE_CONTAINER,
                                    #site-root,
                                    #SITE_BACKGROUND,
                                    #SITE_ROOT,
                                    .SITE_ROOT {
                                        overflow: hidden !important;
                                    }
                                    *::-webkit-scrollbar {
                                        width: 0 !important;
                                        height: 0 !important;
                                        display: none !important;
                                    }
                                `;

                                var style = document.createElement('style');
                                style.setAttribute('data-eg-no-scroll', '1');
                                style.appendChild(document.createTextNode(css));
                                (document.head || document.documentElement).appendChild(style);

                            } catch (e) { console.error(e); }
                        }

                        if (document.readyState === 'loading')
                            document.addEventListener('DOMContentLoaded', injectNoScroll);
                        else
                            injectNoScroll();

                        var mo = new MutationObserver(injectNoScroll);
                        mo.observe(document.documentElement, {
                            childList: true,
                            subtree: true,
                            attributes: true,
                            attributeFilter: ['style','class']
                        });
                    })();
                ");

                DailyWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                DailyWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                DailyWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

                ShowLoading();
                NavigateToHome();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"WebView2 initialization failed: {ex.Message}";
            }
        }

        // ============================
        // NAVIGATION EVENTS
        // ============================

        private void NavigateToHome()
        {
            try
            {
                var url = CurrentStartUrl;

                if (string.IsNullOrWhiteSpace(url))
                {
                    StatusText.Text = "No start URL configured.";
                    return;
                }

                DailyWebView.Source = new Uri(url);
                StatusText.Text = "Loading home page…";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Invalid start URL: {ex.Message}";
            }
        }

        private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            StatusText.Text = $"Navigating to: {e.Uri}";
            ShowLoading();
        }

        private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                StatusText.Text = "Ready.";
                HideLoading();
            }
            else
            {
                StatusText.Text = $"Navigation failed: {e.WebErrorStatus}";
            }
        }

        // ============================
        // LOADING ANIMATIONS
        // ============================

        private void ShowLoading()
        {
            if (LoadingOverlay == null) return;

            LoadingOverlay.Visibility = Visibility.Visible;

            var overlayFadeIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(150) };
            LoadingOverlay.BeginAnimation(OpacityProperty, overlayFadeIn);

            var webFadeOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(150) };
            DailyWebView.BeginAnimation(OpacityProperty, webFadeOut);
        }

        private void HideLoading()
        {
            if (LoadingOverlay == null) return;

            var overlayFadeOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(250) };
            overlayFadeOut.Completed += (_, _) => LoadingOverlay.Visibility = Visibility.Collapsed;
            LoadingOverlay.BeginAnimation(OpacityProperty, overlayFadeOut);

            var webFadeIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
            DailyWebView.BeginAnimation(OpacityProperty, webFadeIn);
        }

        // ============================
        // UI BUTTONS
        // ============================

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            ShowLoading();
            NavigateToHome();
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowLoading();
                DailyWebView.Reload();
                StatusText.Text = "Reloading…";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Reload failed: {ex.Message}";
            }
        }

        private void LanguageToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isEnglish = !_isEnglish;
            UpdateLanguageUi();
            SaveWindowState();

            ShowLoading();
            NavigateToHome();
        }

        // ============================
        // THEME SYSTEM
        // ============================

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyTheme(!_isDarkTheme);
            SaveWindowState();
        }

        private void ApplyTheme(bool dark)
        {
            _isDarkTheme = dark;

            if (dark)
            {
                Resources["AppBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#020617"));
                Resources["TopBarBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#020617"));
                Resources["CardBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#020617"));
                Resources["CardBorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
                Resources["StatusBarBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#020617"));
                Resources["TopBarTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E7EB"));
                Resources["StatusTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
                Resources["IconForegroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E7EB"));
                Resources["IconHoverBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111827"));
                Resources["IconPressedBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
            }
            else
            {
                Resources["AppBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F4F6"));
                Resources["TopBarBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                Resources["CardBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                Resources["CardBorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1D5DB"));
                Resources["StatusBarBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F9FAFB"));
                Resources["TopBarTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111827"));
                Resources["StatusTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4B5563"));
                Resources["IconForegroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4B5563"));
                Resources["IconHoverBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E7EB"));
                Resources["IconPressedBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1D5DB"));
            }

            if (Resources["AppBackgroundBrush"] is Brush bg)
                Background = bg;

            if (ThemeToggleIcon != null)
                ThemeToggleIcon.Text = dark ? "\uE706" : "\uE708";
        }

        // ============================
        // LANGUAGE UI
        // ============================

        private void UpdateLanguageUi()
        {
            if (LanguageToggleLabel != null)
                LanguageToggleLabel.Text = _isEnglish ? "EN" : "ΕΛ";

            if (LanguageToggleButton != null)
                LanguageToggleButton.ToolTip = _isEnglish ? "Switch to Greek" : "Switch to English";

            if (TitleText != null)
                TitleText.Text = _isEnglish
                    ? "EUROGNOSI™ Online Classroom"
                    : "ΕΥΡΩΓΝΩΣΗ™ Διαδικτυακή Τάξη";
        }

        // ============================
        // WINDOW STATE SAVE / LOAD
        // ============================

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            SaveWindowState();
        }

        private void LoadWindowState()
        {
            try
            {
                if (!File.Exists(_windowStatePath))
                    return;

                var json = File.ReadAllText(_windowStatePath);
                var state = JsonSerializer.Deserialize<SavedWindowState>(json);
                if (state == null) return;

                if (state.Width > 400 && state.Height > 300)
                {
                    Width = state.Width;
                    Height = state.Height;
                }

                if (!double.IsNaN(state.Left) && !double.IsNaN(state.Top))
                {
                    Left = state.Left;
                    Top = state.Top;
                }

                if (Left < 0 || Top < 0 ||
                    Left > SystemParameters.VirtualScreenWidth - 100 ||
                    Top > SystemParameters.VirtualScreenHeight - 100)
                {
                    Left = 100;
                    Top = 100;
                }

                if (state.IsDarkTheme.HasValue)
                    _isDarkTheme = state.IsDarkTheme.Value;

                _isEnglish = state.Language == "en";

                ApplyTheme(_isDarkTheme);
            }
            catch
            {
                // Ignore
            }
        }

        private void SaveWindowState()
        {
            try
            {
                var state = new SavedWindowState
                {
                    Width = Width,
                    Height = Height,
                    Left = Left,
                    Top = Top,
                    IsDarkTheme = _isDarkTheme,
                    Language = _isEnglish ? "en" : "el"
                };

                var folder = Path.GetDirectoryName(_windowStatePath);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder!);  // the ! tells the compiler “I know this isn’t null”
                }

                var json = JsonSerializer.Serialize(state);
                File.WriteAllText(_windowStatePath, json);
            }
            catch
            {
                // Ignore
            }
        }

        private class SavedWindowState
        {
            public double Width { get; set; }
            public double Height { get; set; }
            public double Left { get; set; }
            public double Top { get; set; }
            public bool? IsDarkTheme { get; set; }
            public string? Language { get; set; }   // <- add ?
        }
    }
}
