using System;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;   
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
        // Hand-raise sound mute flag
        private bool _handSoundMuted = false;
        private string? _currentRoomUrl;
        // Toast-style hand raise overlays (stacked)
        private readonly List<HandRaiseOverlayWindow> _toastWindows = new();
        // For message deduplication (same snackbar text repeated)
        private readonly Dictionary<string, DateTime> _recentToastMessages = new();
        private DateTime _lastToastTime = DateTime.MinValue;
        private static readonly TimeSpan ToastDedupWindow = TimeSpan.FromSeconds(5);

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
        private void RepositionToasts(HandRaiseOverlayWindow? newlyAdded)
        {
            if (_toastWindows.Count == 0)
                return;

            double margin = 12;
            double bottom = SystemParameters.WorkArea.Bottom - margin;
            double right = SystemParameters.WorkArea.Right - margin;

            // stack from bottom-right upwards
            foreach (var toast in _toastWindows.AsEnumerable().Reverse())
            {
                toast.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double height = toast.DesiredSize.Height;

                double targetTop = bottom - height;
                double targetLeft = right - toast.DesiredSize.Width;

                toast.Left = targetLeft;

                if (toast == newlyAdded)
                {
                    // slide up from just below targetTop
                    var anim = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = targetTop + 30,
                        To = targetTop,
                        Duration = TimeSpan.FromMilliseconds(180),
                        EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                        {
                            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                        }
                    };
                    toast.BeginAnimation(Window.TopProperty, anim);
                }
                else
                {
                    toast.BeginAnimation(Window.TopProperty, null); // stop anim
                    toast.Top = targetTop;
                }

                bottom = targetTop - margin;
            }
        }

        private void RestoreDropdownSelectionFromSettings()
        {
            var lastVenue = Properties.Settings.Default.LastVenue;
            var lastRoomId = Properties.Settings.Default.LastRoomId;

            if (string.IsNullOrWhiteSpace(lastVenue) || string.IsNullOrWhiteSpace(lastRoomId))
                return;

            if (_rooms.Count == 0)
                return;

            // Set the School combo – this will also repopulate TeacherComboBox via SelectionChanged
            if (!string.IsNullOrWhiteSpace(lastVenue))
            {
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
        // return bool as before: true = actually showed a new toast
        /// <summary>
        /// Shows a toast. Returns true if a *new* toast was actually shown
        /// (used to decide if we play a sound).
        /// </summary>
        private bool ShowToast(string message, bool showEmoji)
        {
            var now = DateTime.Now;

            // Very small anti-spam throttle for DOM spam
            if ((now - _lastToastTime).TotalMilliseconds < 120)
                return false;

            _lastToastTime = now;

            // Dedup: same text within window → ignore
            if (_recentToastMessages.TryGetValue(message, out var lastShown) &&
                (now - lastShown) < ToastDedupWindow)
            {
                return false;
            }
            _recentToastMessages[message] = now;

            // Clean up old entries
            foreach (var key in _recentToastMessages.Keys.ToList())
            {
                if ((now - _recentToastMessages[key]) > ToastDedupWindow * 2)
                    _recentToastMessages.Remove(key);
            }

            const int maxToasts = 5;
            if (_toastWindows.Count >= maxToasts)
            {
                var oldest = _toastWindows[0];
                try { oldest.FadeOutAndClose(); } catch { }
                _toastWindows.RemoveAt(0);
            }

            var toast = new HandRaiseOverlayWindow(message, showEmoji)
            {
                Topmost = true,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            toast.Loaded += (_, __) => RepositionToasts(toast);
            toast.Closed += (_, __) =>
            {
                _toastWindows.Remove(toast);
                RepositionToasts(null);
            };

            _toastWindows.Add(toast);
            toast.Show();

            // Auto-dismiss
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                try { toast.FadeOutAndClose(); } catch { }
            };
            timer.Start();

            return true;
        }



        private void HideAllToasts()
        {
            foreach (var toast in _toastWindows.ToArray())
            {
                try { toast.FadeOutAndClose(); } catch { }
            }
            _toastWindows.Clear();
        }

        private void PlayParticipantLeftSound()
        {
            if (_handSoundMuted)
                return;

            try
            {
                var uri = new Uri("pack://application:,,,/Sounds/participant-left.wav");
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

        private void PlayHandRaiseSound()
        {
            if (_handSoundMuted)
                return;

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
        private void HandSoundCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // Checked = sound ON
            _handSoundMuted = false;
            Properties.Settings.Default.HandRaiseMuted = false;
            Properties.Settings.Default.Save();
        }

        private void HandSoundCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            // Unchecked = sound OFF
            _handSoundMuted = true;
            Properties.Settings.Default.HandRaiseMuted = true;
            Properties.Settings.Default.Save();
        }


        // --------------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------------
        public MainWindow()
        {
            CommunicationsDuckingHelper.EnsureDoNothing();
            InitializeComponent();
            // 🔊 Load saved mute state
            _handSoundMuted = Properties.Settings.Default.HandRaiseMuted;
            if (HandSoundCheckBox != null)
            {
                // Checked = sound ON
                HandSoundCheckBox.IsChecked = !_handSoundMuted;
            }
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
                        await InjectParticipantCountObserverAsync();
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
          // Install only once
          if (window.__egSnackbarObserverInstalled) return;
          window.__egSnackbarObserverInstalled = true;

          function postToHost(payload) {
            try {
              if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
                window.chrome.webview.postMessage(payload);
              }
            } catch (e) {}
          }

          let lastHandText = '';
          let lastHandTs   = 0;
          let lastChatSig  = '';

          function processSnackbar() {
            const presenter = document.querySelector('.snackbar-presenter');
            if (!presenter) return;

            // ANY visible snackbar (hand, chat, etc.)
            const snackbar = presenter.querySelector('.snackbar.success.visible, .snackbar.info.visible');
            if (!snackbar) return;

            const textContainer = snackbar.querySelector('.text');
            if (!textContainer) return;

            const fullText = (textContainer.textContent || '').trim();
            if (!fullText) return;

            const now = Date.now();

            // -------- HAND RAISE / LOWER --------
            if (fullText.includes('raised their hand') || fullText.includes('raised their hands')) {

              // Collapse DOM noise (same text a few times quickly)
              if (fullText === lastHandText && (now - lastHandTs) < 300) {
                return;
              }
              lastHandText = fullText;
              lastHandTs   = now;

              postToHost({
                type: 'handSnackbar',
                text: fullText,
                ts: now
              });
              return;
            }

            // -------- CHAT MESSAGE SNACKBAR --------
            // Structure is usually:
            // <div class=""text"">
            //   <strong>NAME</strong>
            //   <span>MESSAGE</span>
            // </div>
            const strong = textContainer.querySelector('strong');
            const span   = textContainer.querySelector('span');

            const author = strong ? (strong.textContent || '').trim() : 'Student';
            const msg    = span   ? (span.textContent   || '').trim() : '';

            if (!msg) return;

            const sig = author + '|' + msg;
            if (sig === lastChatSig) {
              return;  // avoid duplicates for the same popup
            }
            lastChatSig = sig;

            postToHost({
              type: 'chatMessage',
              from: author,
              text: msg,
              ts: now
            });
          }

          const observer = new MutationObserver(function () {
            try { processSnackbar(); } catch (e) { }
          });

          observer.observe(document.body, {
            childList: true,
            subtree: true,
            characterData: true
          });

          // Initial check
          processSnackbar();
        })();
        ");
        }

        private async Task InjectParticipantCountObserverAsync()
        {
            if (DailyWebView?.CoreWebView2 == null)
                return;

            await DailyWebView.CoreWebView2.ExecuteScriptAsync(@"
            (function () {
              if (window.__egParticipantCountObserverInstalled) return;
              window.__egParticipantCountObserverInstalled = true;

              function post(msg) {
                if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
                  window.chrome.webview.postMessage(msg);
                }
              }

              let lastCount = -1;

              function readCount() {
                // Look through p/div/span for either 'people in call' or 'Waiting for others to join'
                const nodes = Array.from(document.querySelectorAll('p, div, span'));

                // Case 1: 'X people in call'
                const peopleEl = nodes.find(el => el.textContent && el.textContent.includes('people in call'));
                if (peopleEl) {
                  const match = peopleEl.textContent.match(/(\d+)/);
                  if (match) {
                    return parseInt(match[1], 10);  // X
                  }
                }

                // Case 2: 'Waiting for others to join' => treat as 1 (only you)
                const waitingEl = nodes.find(el => el.textContent && el.textContent.includes('Waiting for others to join'));
                if (waitingEl) {
                  return 1;
                }

                return null;
              }

              function check() {
                const count = readCount();
                if (count == null) return;

                // First time we see a valid count
                if (lastCount === -1) {
                  // If more than 1, someone else is already here
                  if (count > 1) {
                    post({
                      type: 'participantJoined',
                      count: count,
                      ts: Date.now()
                    });
                  }
                  lastCount = count;
                  return;
                }

                // Subsequent changes
                if (count > lastCount) {
                  post({
                    type: 'participantJoined',
                    count: count,
                    ts: Date.now()
                  });
                } else if (count < lastCount) {
                  post({
                    type: 'participantLeft',
                    count: count,
                    ts: Date.now()
                  });
                }

                lastCount = count;
              }

              const observer = new MutationObserver(() => {
                try { check(); } catch(e) {}
              });

              observer.observe(document.body, {
                childList: true,
                subtree: true,
                characterData: true
              });

              check();
            })();
            ");
        }



        private class WebMessagePayload
        {
            public string? type { get; set; }   // "handSnackbar", "chatMessage", "participantJoined", ...
            public string? text { get; set; }   // message text
            public string? from { get; set; }   // chat sender
            public int? count { get; set; }     // participant number
            public long? ts { get; set; }       // timestamp
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

                var msg = JsonSerializer.Deserialize<WebMessagePayload>(raw);
                if (msg == null || string.IsNullOrWhiteSpace(msg.type))
                    return;

                // =======================
                // 1) Hand raise snackbar
                // =======================
                if (string.Equals(msg.type, "handSnackbar", StringComparison.OrdinalIgnoreCase))
                {
                    var text = msg.text;
                    if (string.IsNullOrWhiteSpace(text))
                        text = "A student raised their hand.";

                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = text;

                        if (ShowToast(text, showEmoji: true))
                            PlayHandRaiseSound();
                    });

                    return;
                }

                // =======================
                // 2) Chat message
                // =======================
                if (string.Equals(msg.type, "chatMessage", StringComparison.OrdinalIgnoreCase))
                {
                    var author = msg.from ?? "Student";
                    var chatText = msg.text ?? string.Empty;
                    var toastText = $"{author}:\n{chatText}";

                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = toastText;
                        if (ShowToast(toastText, showEmoji: false))
                            PlayHandRaiseSound();   // same sound for now
                    });

                    return;
                }

                // =======================
                // 3) Participant joined
                // =======================
                if (msg.type == "participantJoined")
                {
                    Dispatcher.Invoke(() =>
                    {
                        ShowToast("A participant joined the call.", showEmoji: false);
                    });
                    return;
                }

                // =======================
                // 4) Participant left
                // =======================
                if (msg.type == "participantLeft")
                {
                    Dispatcher.Invoke(() =>
                    {
                        var text = "A participant left the call.";
                        if (ShowToast(text, showEmoji: false))
                        {
                            PlayParticipantLeftSound();  // 🔊 UNIQUE LEAVE SOUND
                        }
                    });
                    return;
                }

                // =======================
                // 5) Fallback
                // =======================
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
