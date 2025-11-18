using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Web.WebView2.Core;

namespace DailyDesktopApp
{
    public partial class MainWindow : Window
    {
        // URLs for Greek & English app pages
        private const string GreekStartUrl = "https://www.eurognosi-fni.com/online-classroom-app?source=desktop";
        private const string EnglishStartUrl = "https://www.eurognosi-fni.com/en/online-classroom-app?source=desktop";

        private readonly string _windowStatePath;
        private bool _isDarkTheme = true;
        private bool _isEnglish = false;  // default: Greek

        // Picks the correct start URL based on current language
        private string CurrentStartUrl => _isEnglish ? EnglishStartUrl : GreekStartUrl;

        public MainWindow()
        {
            InitializeComponent();

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appData, "EurognosiOnlineClassroom");
            _windowStatePath = Path.Combine(appFolder, "window-state.json");

            // Defaults before loading from disk
            _isDarkTheme = true;
            _isEnglish = false;

            // Load saved size/theme/language (if file exists)
            LoadWindowState();

            // Make sure UI texts match current language
            UpdateLanguageUi();

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "Initializing browser…";

                await DailyWebView.EnsureCoreWebView2Async(null);

                // Inject CSS to remove scrollbars & lock overflow (Wix + Daily)
                await DailyWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                    (function () {
                        function injectNoScroll() {
                            try {
                                if (document.querySelector('style[data-eg-no-scroll]')) {
                                    return;
                                }

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
                                style.type = 'text/css';
                                style.appendChild(document.createTextNode(css));
                                (document.head || document.documentElement).appendChild(style);

                                ['html', 'body', '#SITE_CONTAINER', '#site-root',
                                 '#SITE_BACKGROUND', '#SITE_ROOT'].forEach(function (sel) {
                                    document.querySelectorAll(sel).forEach(function (el) {
                                        el.style.overflow = 'hidden';
                                    });
                                });
                            } catch (e) {
                                console.error('EG no-scroll inject error', e);
                            }
                        }

                        if (document.readyState === 'loading') {
                            document.addEventListener('DOMContentLoaded', injectNoScroll);
                        } else {
                            injectNoScroll();
                        }

                        var mo = new MutationObserver(function () {
                            injectNoScroll();
                        });
                        mo.observe(document.documentElement, {
                            childList: true,
                            subtree: true,
                            attributes: true,
                            attributeFilter: ['style', 'class']
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

        // ---------- Navigation + loading animations ----------

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
                // keep the overlay visible so teacher knows something went wrong
            }
        }

        private void ShowLoading()
        {
            if (LoadingOverlay == null) return;

            LoadingOverlay.Visibility = Visibility.Visible;

            var overlayFadeIn = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            LoadingOverlay.BeginAnimation(OpacityProperty, overlayFadeIn);

            var webFadeOut = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            DailyWebView.BeginAnimation(OpacityProperty, webFadeOut);
        }

        private void HideLoading()
        {
            if (LoadingOverlay == null) return;

            var overlayFadeOut = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            overlayFadeOut.Completed += (_, _) =>
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            };
            LoadingOverlay.BeginAnimation(OpacityProperty, overlayFadeOut);

            var webFadeIn = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            DailyWebView.BeginAnimation(OpacityProperty, webFadeIn);
        }

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
            _isEnglish = !_isEnglish;   // flip language
            UpdateLanguageUi();         // update button text/tooltip
            SaveWindowState();          // persist choice

            // Reload homepage in the new language
            ShowLoading();
            NavigateToHome();
        }

        // ---------- Theme toggle ----------

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyTheme(!_isDarkTheme);
            SaveWindowState(); // persist theme choice too
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

                // Softer hover/pressed in light mode
                Resources["IconHoverBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E7EB"));
                Resources["IconPressedBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1D5DB"));
            }

            // Apply to the Window itself
            if (Resources["AppBackgroundBrush"] is Brush bg)
            {
                Background = bg;
            }

            if (ThemeToggleIcon != null)
            {
                ThemeToggleIcon.Text = dark ? "\uE706" : "\uE708";
            }
        }

        // ---------- Language UI helpers ----------

        private void UpdateLanguageUi()
        {
            if (LanguageToggleLabel != null)
            {
                // Show the current language code
                LanguageToggleLabel.Text = _isEnglish ? "EN" : "ΕΛ";
            }

            if (LanguageToggleButton != null)
            {
                LanguageToggleButton.ToolTip = _isEnglish
                    ? "Switch to Greek"
                    : "Switch to English";
            }
            if (TitleText != null)
            {
                TitleText.Text = _isEnglish
                    ? "EUROGNOSI™ Online Classroom"
                    : "ΕΥΡΩΓΝΩΣΗ™ Διαδικτυακή Τάξη";
            }
        }

        // ---------- Window size / position persistence ----------

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

                // Load theme preference (default to dark if missing)
                if (state.IsDarkTheme.HasValue)
                {
                    _isDarkTheme = state.IsDarkTheme.Value;
                }
                else
                {
                    _isDarkTheme = true;
                }

                // Load language preference (default Greek if missing)
                if (!string.IsNullOrEmpty(state.Language))
                {
                    _isEnglish = state.Language.Equals("en", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    _isEnglish = false;
                }

                // Apply theme after loading it
                ApplyTheme(_isDarkTheme);
            }
            catch
            {
                // ignore, keep defaults
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
                    Directory.CreateDirectory(folder);
                }

                var json = JsonSerializer.Serialize(state);
                File.WriteAllText(_windowStatePath, json);
            }
            catch
            {
                // ignore save errors
            }
        }

        private class SavedWindowState
        {
            public double Width { get; set; }
            public double Height { get; set; }
            public double Left { get; set; }
            public double Top { get; set; }

            // nullable so old JSON files still deserialize
            public bool? IsDarkTheme { get; set; }

            // "en" or "el" (nullable for old JSONs)
            public string Language { get; set; }
        }
    }
}
