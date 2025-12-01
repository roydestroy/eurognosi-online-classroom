using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Media;
using System.Windows.Threading;

namespace DailyDesktopApp
{
    public partial class MainWindow : Window
    {
        // IMPORTANT: change to the real domain (NO trailing slash)
        private const string ApiBaseUrl = "https://www.eurognosi-fni.com";

        // Use a single HttpClient with a browser-like User-Agent
        private static readonly HttpClient Http;

        // Static ctor to configure HttpClient once
        static MainWindow()
        {
            var handler = new HttpClientHandler();
            Http = new HttpClient(handler);

            // Make our app look like a normal browser – this can bypass some filters
            Http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/120.0.0.0 Safari/537.36");

            Http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/json, */*");
        }
        private void UpdateEmptyState(bool hasRoom)
        {
            if (EmptyStateGrid == null || DailyWebView == null)
                return;

            if (hasRoom)
            {
                EmptyStateGrid.Visibility = Visibility.Collapsed;
                DailyWebView.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyStateGrid.Visibility = Visibility.Visible;
                DailyWebView.Visibility = Visibility.Collapsed;
            }
        }

        private List<OnlineRoom> _rooms = new();
        private string? _currentRoomUrl;
        private HandRaiseOverlayWindow? _currentHandOverlay;
        private DateTime _lastOverlayTime = DateTime.MinValue;
        private void SetConnectedState(bool isConnected)
        {
            if (isConnected)
            {
                ReconnectButton.Content = "Disconnect";
                ReconnectButton.Style = (Style)FindResource("DisconnectButtonStyle");
            }
            else
            {
                ReconnectButton.Content = "Reconnect";
                ReconnectButton.Style = (Style)FindResource("SecondaryButtonStyle");
                ReconnectButton.IsEnabled = false;
            }
        }

        // --------------------------------------------------------------------
        // Overlay helpers
        // --------------------------------------------------------------------
        private void ShowLoadingOverlay(string? message = null)
        {
            if (LoadingOverlay != null)
                LoadingOverlay.Visibility = Visibility.Visible;

            if (!string.IsNullOrWhiteSpace(message))
                StatusText.Text = message;
        }

        private void RestoreDropdownSelectionFromSettings()
        {
            var lastVenue = Properties.Settings.Default.LastVenue;
            var lastRoomId = Properties.Settings.Default.LastRoomId;

            if (string.IsNullOrWhiteSpace(lastVenue) || string.IsNullOrWhiteSpace(lastRoomId))
                return;

            if (_rooms == null || _rooms.Count == 0)
                return;

            // Set the School combo – this will also repopulate TeacherComboBox via SelectionChanged
            if (VenueComboBox.ItemsSource != null &&
                VenueComboBox.Items.Contains(lastVenue))
            {
                VenueComboBox.SelectedItem = lastVenue;
            }
            else
            {
                // if binding is list<string>, safer to force-set:
                VenueComboBox.SelectedItem = lastVenue;
            }

            // Now try to select the correct teacher
            var teacherList = TeacherComboBox.ItemsSource as IEnumerable<OnlineRoom>;
            if (teacherList == null) return;

            var match = teacherList.FirstOrDefault(r => r.Id == lastRoomId);
            if (match != null)
            {
                TeacherComboBox.SelectedItem = match;
            }
        }

        private void HideLoadingOverlay(string? message = null)
        {
            if (LoadingOverlay != null)
                LoadingOverlay.Visibility = Visibility.Collapsed;

            if (!string.IsNullOrWhiteSpace(message))
                StatusText.Text = message;
        }
        private void ShowHandRaiseOverlay(string namesText, int count)
        {
            // simple throttle: max 1 new overlay every 1.5 seconds
            if ((DateTime.Now - _lastOverlayTime).TotalSeconds < 1.5)
                return;

            _lastOverlayTime = DateTime.Now;

            // Close any existing overlay
            if (_currentHandOverlay != null)
            {
                try { _currentHandOverlay.Close(); } catch { }
                _currentHandOverlay = null;
            }

            var overlay = new HandRaiseOverlayWindow(namesText, count)
            {
                Topmost = true,
                ShowInTaskbar = false
            };

            overlay.WindowStartupLocation = WindowStartupLocation.Manual;

            overlay.Loaded += (_, __) =>
            {
                var work = SystemParameters.WorkArea;
                overlay.Left = work.Right - overlay.ActualWidth - 16;
                overlay.Top = work.Bottom - overlay.ActualHeight - 16;
            };

            _currentHandOverlay = overlay;
            overlay.Show();

            // Auto-close after 5 seconds
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                if (_currentHandOverlay == overlay)
                {
                    try { overlay.Close(); } catch { }
                    _currentHandOverlay = null;
                }
            };
            timer.Start();
        }

        private void HideHandRaiseOverlay()
        {
            if (_currentHandOverlay != null)
            {
                try { _currentHandOverlay.Close(); } catch { }
                _currentHandOverlay = null;
            }
        }

        private void PlayHandRaiseSound()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/Sounds/handraise.wav");
                var stream = Application.GetResourceStream(uri).Stream;
                var player = new System.Media.SoundPlayer(stream);
                player.Play();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Sound error: " + ex.Message);
                SystemSounds.Asterisk.Play();
            }
        }


        // --------------------------------------------------------------------
        // ctor
        // --------------------------------------------------------------------
        public MainWindow()
        {
            CommunicationsDuckingHelper.EnsureDoNothing();
            InitializeComponent();
            UpdateEmptyState(false);
            // Keep maximized window within the working area (above the taskbar)
            MaxHeight = SystemParameters.WorkArea.Height +8;

            // Restore window size & position
            Width = Properties.Settings.Default.WindowWidth;
            Height = Properties.Settings.Default.WindowHeight;
            Left = Properties.Settings.Default.WindowLeft;
            Top = Properties.Settings.Default.WindowTop;

            // Safety reset if window was saved off-screen
            if (Left < 0 || Top < 0 ||
                Left > SystemParameters.VirtualScreenWidth - 100 ||
                Top > SystemParameters.VirtualScreenHeight - 100)
            {
                Left = 100;
                Top = 100;
            }

            // Restore last room for reconnect
            var lastUrl = Properties.Settings.Default.LastRoomUrl;
            if (!string.IsNullOrWhiteSpace(lastUrl))
            {
                _currentRoomUrl = lastUrl;

                ReconnectButton.Content = "Reconnect";
                ReconnectButton.Style = (Style)FindResource("SecondaryButtonStyle");
                ReconnectButton.IsEnabled = true;
            }
            else
            {
                SetConnectedState(false); // no last room: keep disabled
            }
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeWebViewAsync();
            await LoadRoomsAsync();
        }

        // --------------------------------------------------------------------
        // WebView initialization
        // --------------------------------------------------------------------
        private async Task InitializeWebViewAsync()
        {
            try
            {
                StatusText.Text = "Initializing browser…";

                await DailyWebView.EnsureCoreWebView2Async(null);
                var core = DailyWebView.CoreWebView2;
                core.WebMessageReceived += CoreWebView2_WebMessageReceived;
                core.Settings.AreDevToolsEnabled = true;
                core.Settings.AreDefaultScriptDialogsEnabled = false; // no “changes may not be saved”

                // hide scrollbars
                await core.AddScriptToExecuteOnDocumentCreatedAsync(@"
                    const s = document.createElement('style');
                    s.innerHTML = `
                        ::-webkit-scrollbar { display:none !important; }
                        html,body { overflow:hidden !important; }
                    `;
                    document.head.appendChild(s);
                ");
                core.NavigationStarting += (_, __) =>
                {
                    ShowLoadingOverlay("Preparing your classroom…");
                };

                core.NavigationCompleted += async (_, eArgs) =>
                {
                    if (eArgs.IsSuccess)
                    {
                        HideLoadingOverlay("Connected.");

                        // Inject hand-raise observer script
                        await InjectHandObserverScriptAsync();
                    }
                    else
                    {
                        HideLoadingOverlay($"Navigation failed: {eArgs.WebErrorStatus}");
                    }
                };


                StatusText.Text = "Browser ready.";
            }
            catch (Exception ex)
            {
                HideLoadingOverlay();
                StatusText.Text = $"WebView error: {ex.Message}";
            }
        }
        private async Task InjectHandObserverScriptAsync()
        {
            if (DailyWebView?.CoreWebView2 == null)
                return;

            await DailyWebView.CoreWebView2.ExecuteScriptAsync(@"
          (function () {
            if (window.__egHandObserverInstalled) return;
            window.__egHandObserverInstalled = true;

            function getHandsInfo() {
              // All hand icons
              const handDivs = document.querySelectorAll('.hand-status');
              const infos = [];

              handDivs.forEach(hand => {
                // Find the tile container
                const tile = hand.closest('.tile-info');
                if (!tile) return;

                // Find name element inside the same tile
                const nameEl = tile.querySelector('.name');
                let name = null;
                if (nameEl && nameEl.textContent) {
                  name = nameEl.textContent.trim();
                }

                infos.push({ name: name });
              });

              return infos;
            }

            let lastSignature = '__none__';

            function notifyHands() {
              const infos = getHandsInfo() || [];
              const count = infos.length;

              if (count === 0) {
                // If we previously had some hands, notify that they are cleared
                if (lastSignature !== '__none__' &&
                    window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
                  window.chrome.webview.postMessage({
                    type: 'handRaisedDom',
                    count: 0,
                    names: [],
                    cleared: true,
                    ts: Date.now()
                  });
                }
                lastSignature = '__none__';
                return;
              }

              const names = infos.map(i => i.name).filter(Boolean);
              const sig = JSON.stringify(names);

              // Avoid resending the same set of names
              if (sig === lastSignature) return;
              lastSignature = sig;

              if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
                window.chrome.webview.postMessage({
                  type: 'handRaisedDom',
                  count: count,
                  names: names,
                  cleared: false,
                  ts: Date.now()
                });
              }
            }


            const observer = new MutationObserver(() => {
              try { notifyHands(); } catch (e) {}
            });

            observer.observe(document.body, { childList: true, subtree: true });

            // Initial check
            notifyHands();
          })();
        ");
        }
        private class HandRaiseMessage
        {
            public string? type { get; set; }
            public int? count { get; set; }
            public long? ts { get; set; }
            public string[]? names { get; set; }
            public bool? cleared { get; set; }
        }


        private void CoreWebView2_WebMessageReceived(
            object? sender,
            Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string raw = e.WebMessageAsJson;

                Console.WriteLine("JS → .NET message received:");
                Console.WriteLine(raw);

                // Try to parse it as a hand-raise message
                var msg = JsonSerializer.Deserialize<HandRaiseMessage>(raw);
                if (msg == null)
                    return;

                if (string.Equals(msg.type, "handRaisedDom", StringComparison.OrdinalIgnoreCase))
                {
                    var count = msg.count ?? 0;

                    if (count == 0)
                    {
                        // All hands are down
                        Console.WriteLine("Hands cleared.");

                        Dispatcher.Invoke(() =>
                        {
                            StatusText.Text = "No raised hands.";
                            HideHandRaiseOverlay();
                        });

                        return;
                    }

                    // Some hands are raised
                    var namesText = (msg.names != null && msg.names.Length > 0)
                        ? string.Join(", ", msg.names)
                        : "Unknown student";

                    Console.WriteLine($"Hands raised: {count} — {namesText}");

                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = $"Hand raised: {namesText} (total {count})";
                        ShowHandRaiseOverlay(namesText, count);
                        PlayHandRaiseSound();
                    });

                    return;
                }

                // Other messages (if we add any in future)
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = $"JS message: {raw}";
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error processing JS message: " + ex.Message);
            }
        }

        // --------------------------------------------------------------------
        // Load rooms from Wix
        // --------------------------------------------------------------------
        private async Task LoadRoomsAsync()
        {
            try
            {
                StatusText.Text = "Loading classrooms…";

                var url = $"{ApiBaseUrl}/_functions/appOnlineRooms";
                var response = await Http.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();

                // EXTRA DEBUG for the classroom PC
                if (!response.IsSuccessStatusCode)
                {
                    MessageBox.Show(
                        $"URL: {response.RequestMessage?.RequestUri}\n" +
                        $"Status: {(int)response.StatusCode} {response.StatusCode}\n\n" +
                        $"Body (first 500 chars):\n" +
                        (body.Length > 500 ? body.Substring(0, 500) : body),
                        "Error loading rooms");

                    StatusText.Text = $"Error loading rooms: {(int)response.StatusCode} {response.StatusCode}";
                    return;
                }

                var json = body;

                _rooms = JsonSerializer.Deserialize<List<OnlineRoom>>(json,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                         ?? new List<OnlineRoom>();

                if (_rooms.Count == 0)
                {
                    StatusText.Text = "No classrooms found.";
                    return;
                }

                var venues = _rooms
                    .Select(r => r.Venue)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                VenueComboBox.ItemsSource = venues;
                StatusText.Text = "Select a school and teacher.";
                // ✅ Try to restore last used venue/teacher, if any
                RestoreDropdownSelectionFromSettings();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading rooms: {ex.Message}";
            }
        }

        // --------------------------------------------------------------------
        // Venue selection
        // --------------------------------------------------------------------
        private void VenueComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var venue = VenueComboBox.SelectedItem as string;

            TeacherComboBox.ItemsSource = null;
            TeacherComboBox.SelectedItem = null;

            // Disable by default
            TeacherComboBox.IsEnabled = false;

            if (string.IsNullOrWhiteSpace(venue))
                return;

            var teachers = _rooms
                .Where(r => r.Venue == venue)
                .OrderBy(r =>
                {
                    var name = r.TeacherName?.ToLower() ?? "";
                    return name.Contains("study lab") ? "zzz" + name : name;
                })
                .ToList();

            TeacherComboBox.ItemsSource = teachers;
            TeacherComboBox.DisplayMemberPath = "DisplayName";
            TeacherComboBox.SelectedValuePath = "Id";

            if (teachers.Count > 0)
            {
                TeacherComboBox.SelectedIndex = 0;
                TeacherComboBox.IsEnabled = true;   // <-- NOW ENABLED
            }
        }

        // --------------------------------------------------------------------
        // Connect
        // --------------------------------------------------------------------
        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            var venue = VenueComboBox.SelectedItem as string;
            var room = TeacherComboBox.SelectedItem as OnlineRoom;

            if (string.IsNullOrWhiteSpace(venue) || room == null)
            {
                StatusText.Text = "Please select both a school and a teacher.";
                return;
            }

            try
            {
                StatusText.Text = "Requesting secure link…";
                ShowLoadingOverlay("Preparing your classroom…");

                var requestObj = new
                {
                    roomId = room.Id,
                    appKey = "th3fukingkeyf0rtheapptowork"  // replace with your real secret
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(requestObj),
                    Encoding.UTF8,
                    "application/json");

                var response = await Http.PostAsync($"{ApiBaseUrl}/_functions/appDailyRoomUrl", content);
                var respJson = await response.Content.ReadAsStringAsync();

                // EXTRA DEBUG if POST fails
                if (!response.IsSuccessStatusCode)
                {
                    MessageBox.Show(
                        $"URL: {response.RequestMessage?.RequestUri}\n" +
                        $"Status: {(int)response.StatusCode} {response.StatusCode}\n\n" +
                        $"Body (first 500 chars):\n" +
                        (respJson.Length > 500 ? respJson.Substring(0, 500) : respJson),
                        "Error requesting Daily URL");

                    HideLoadingOverlay($"Connection error: {(int)response.StatusCode} {response.StatusCode}");
                    return;
                }

                var tokenResp = JsonSerializer.Deserialize<DailyRoomResponse>(respJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (tokenResp == null || string.IsNullOrWhiteSpace(tokenResp.RoomUrl))
                {
                    HideLoadingOverlay("Invalid server response.");
                    return;
                }

                _currentRoomUrl = tokenResp.RoomUrl;

                // save for future reconnects
                Properties.Settings.Default.LastRoomUrl = _currentRoomUrl;
                Properties.Settings.Default.LastVenue = venue ?? "";
                Properties.Settings.Default.LastRoomId = room.Id ?? "";
                Properties.Settings.Default.Save();

                DailyWebView.Source = new Uri(_currentRoomUrl);

                SetConnectedState(true);   // handle text + style
                ReconnectButton.IsEnabled = true;
                UpdateEmptyState(true);

                // NavigationCompleted handler will hide overlay & update text
            }
            catch (Exception ex)
            {
                HideLoadingOverlay();
                StatusText.Text = $"Connection error: {ex.Message}";
            }
        }

        // --------------------------------------------------------------------
        // Reconnect
        // --------------------------------------------------------------------
        private void ReconnectButton_Click(object sender, RoutedEventArgs e)
        {
            // 1) If the button is in "Disconnect" mode, use it to leave the room
            var btnText = (ReconnectButton.Content as string) ?? ReconnectButton.Content?.ToString();

            if (string.Equals(btnText, "Disconnect", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    // Leave Daily room by navigating away
                    DailyWebView.Source = new Uri("about:blank");
                }
                catch
                {
                    // Fallback – in case about:blank Uri throws for any reason
                    DailyWebView.Source = null;
                }

                // Show logo / empty state again
                UpdateEmptyState(false);

                StatusText.Text = "Disconnected.";

                // Turn the button back into a disabled 'Reconnect'
                ReconnectButton.Content = "Reconnect";
                ReconnectButton.Style = (Style)FindResource("SecondaryButtonStyle");
                ReconnectButton.IsEnabled = true;

                return;
            }

            // 2) Normal RECONNECT behaviour (your original code)
            if (string.IsNullOrWhiteSpace(_currentRoomUrl))
            {
                var last = Properties.Settings.Default.LastRoomUrl;
                if (!string.IsNullOrWhiteSpace(last))
                {
                    _currentRoomUrl = last;
                }
            }

            if (string.IsNullOrWhiteSpace(_currentRoomUrl))
            {
                StatusText.Text = "No previous session.";
                return;
            }

            // ✅ Restore dropdowns to match the session we’re reconnecting to
            RestoreDropdownSelectionFromSettings();

            try
            {
                ShowLoadingOverlay("Reconnecting to your classroom…");
                DailyWebView.Source = new Uri(_currentRoomUrl);
                UpdateEmptyState(true);

                SetConnectedState(true);
                // overlay will be hidden in NavigationCompleted
            }
            catch (Exception ex)
            {
                HideLoadingOverlay();
                StatusText.Text = $"Reconnect failed: {ex.Message}";
            }
        }


        // --------------------------------------------------------------------
        // Home (clear UI but KEEP last session)
        // --------------------------------------------------------------------
        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            // We intentionally DO NOT clear _currentRoomUrl or LastRoomUrl
            // so that Reconnect still knows where to go.

            DailyWebView.Source = new Uri("about:blank");
            UpdateEmptyState(false);
            SetConnectedState(false);
            ReconnectButton.IsEnabled = true; // allow reconnect to last room
            VenueComboBox.SelectedItem = null;
            TeacherComboBox.ItemsSource = null;
            TeacherComboBox.SelectedItem = null;

            StatusText.Text = "Selection cleared. Use Reconnect to return to last classroom.";
        }

        // --------------------------------------------------------------------
        // Reload current room
        // --------------------------------------------------------------------
        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DailyWebView.Source != null &&
                    DailyWebView.Source.ToString() != "about:blank")
                {
                    ShowLoadingOverlay("Reloading classroom…");
                    DailyWebView.Reload();
                }
                else
                {
                    StatusText.Text = "Nothing to reload.";
                }
            }
            catch (Exception ex)
            {
                HideLoadingOverlay();
                StatusText.Text = $"Reload failed: {ex.Message}";
            }
        }

        // --------------------------------------------------------------------
        // Custom title bar (drag + system buttons)
        // --------------------------------------------------------------------
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { /* ignore */ }
            }
        }

        private void MinButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaxRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                MaxRestoreIcon.Text = "\uE922"; // maximize icon
            }
            else
            {
                WindowState = WindowState.Maximized;
                MaxRestoreIcon.Text = "\uE923"; // restore icon
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // --------------------------------------------------------------------
        // Save window state
        // --------------------------------------------------------------------
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);

            Properties.Settings.Default.WindowWidth = Width;
            Properties.Settings.Default.WindowHeight = Height;
            Properties.Settings.Default.WindowLeft = Left;
            Properties.Settings.Default.WindowTop = Top;
            Properties.Settings.Default.Save();
        }
    }

    // ------------------------------------------------------------------------
    // Data classes
    // ------------------------------------------------------------------------
    public class OnlineRoom
    {
        public string Id { get; set; } = "";
        public string Venue { get; set; } = "";
        public string TeacherName { get; set; } = "";
        public string RoomName { get; set; } = "";
        public string DailySubdomain { get; set; } = "";

        public string DisplayName =>
            string.IsNullOrWhiteSpace(TeacherName) ? RoomName : TeacherName;

        public override string ToString()
        {
            // This is what the ComboBox will show
            return DisplayName;
        }

    }


    public class DailyRoomResponse
    {
        public string RoomUrl { get; set; } = "";
    }
}
