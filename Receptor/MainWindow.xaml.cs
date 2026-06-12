using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ListenerApp
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;

        private bool _isClickThrough = false;
        private DispatcherTimer _overlayTimer;

        private string _channelId;
        private ClientWebSocket _ws;
        private readonly SpeechSynthesizer _synth = new SpeechSynthesizer();
        private const string WsUrl = "wss://localhost:7007/ws";

        private string _lastWordsState = string.Empty;
        private CancellationTokenSource _ttsCts;
        private readonly List<TextBlock> _symbolElements = new List<TextBlock>();

        private readonly Dictionary<string, string> _emojiMap = new Dictionary<string, string>
        {
            {"té", "T"}, {"círculo", "🔴"}, {"triángulo", "🔻"}, {"equis", "❌"}, {"rombo", "🔷"}
        };

        private readonly Point[] _pentagonPoints = new Point[]
        {
            new Point(240, 50), new Point(260, 190), new Point(150, 250), new Point(40, 190), new Point(60, 50)
        };

        public MainWindow()
        {
            InitializeComponent();

            this.Left = (SystemParameters.PrimaryScreenWidth - this.Width) / 2;
            this.Top = 10;

            _synth.SetOutputToDefaultAudioDevice();
            _synth.Rate = 2;
            _synth.SpeakProgress += Synth_SpeakProgress;
            _synth.SpeakCompleted += Synth_SpeakCompleted;

            this.Loaded += MainWindow_Loaded;

            _ = StartWebSocketListenerAsync();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _overlayTimer.Tick += OverlayTimer_Tick;
            _overlayTimer.Start();
        }

        private void OverlayTimer_Tick(object sender, EventArgs e)
        {
            if (!GetCursorPos(out POINT p)) return;

            try
            {
                Point mousePos = new Point(p.X, p.Y);
                Point windowPos = this.PointFromScreen(mousePos);

                if (windowPos.X >= 0 && windowPos.X <= this.Width &&
                    windowPos.Y >= 0 && windowPos.Y <= this.Height)
                {
                    bool isInteractive = false;
                    HitTestResult result = VisualTreeHelper.HitTest(this, windowPos);

                    if (result?.VisualHit is DependencyObject element)
                    {
                        while (element != null)
                        {
                            if (element is Button || element is ToggleButton || (element as FrameworkElement)?.Name == "DragBorder")
                            {
                                isInteractive = true;
                                break;
                            }
                            element = VisualTreeHelper.GetParent(element);
                        }
                    }

                    if (isInteractive) MakeClickable();
                    else MakeClickThrough();
                }
                else
                {
                    MakeClickThrough();
                }
            }
            catch { }
        }

        private void MakeClickThrough()
        {
            if (_isClickThrough) return;
            var hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
            _isClickThrough = true;
        }

        private void MakeClickable()
        {
            if (!_isClickThrough) return;
            var hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
            _isClickThrough = false;
        }

        private string PromptForPassword()
        {
            return Application.Current.Dispatcher.Invoke(() =>
            {
                string input = string.Empty;
                var promptWindow = new Window
                {
                    Width = 350,
                    Height = 160,
                    Title = "Autenticación",
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Topmost = true,
                    ResizeMode = ResizeMode.NoResize,
                    Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                    Foreground = Brushes.White
                };

                var panel = new StackPanel { Margin = new Thickness(15) };
                panel.Children.Add(new TextBlock { Text = "Ingresa un canal:", Foreground = Brushes.White });
                var txtPassword = new TextBox { Margin = new Thickness(0, 10, 0, 10), Padding = new Thickness(3) };
                panel.Children.Add(txtPassword);
                var btnConnect = new Button { Content = "Conectar", Width = 90, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(5) };
                btnConnect.Click += (s, e) => { input = txtPassword.Text; promptWindow.DialogResult = true; };
                panel.Children.Add(btnConnect);

                promptWindow.Content = panel;
                promptWindow.ShowDialog();
                return input;
            });
        }

        private async Task StartWebSocketListenerAsync()
        {
            while (true)
            {
                if (string.IsNullOrEmpty(_channelId))
                {
                    _channelId = PromptForPassword();
                    if (string.IsNullOrEmpty(_channelId)) { Dispatcher.Invoke(Close); return; }
                }

                try
                {
                    _ws = new ClientWebSocket();
                    var uri = new Uri($"{WsUrl}?token={_channelId}");
                    await _ws.ConnectAsync(uri, CancellationToken.None);

                    using (var cts = new CancellationTokenSource())
                    {
                    var heartbeatTask = HeartbeatLoopAsync(cts.Token);

                    var buffer = new byte[1024 * 4];

                    while (_ws.State == WebSocketState.Open)
                    {
                        var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        if (result.MessageType == WebSocketMessageType.Close) break;

                        string response = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        var words = JsonSerializer.Deserialize<List<string>>(response);

                        if (words != null && words.Count == 1 && words[0] == "pong") continue;

                        if (response != _lastWordsState && words != null)
                        {
                            _lastWordsState = response;
                            Dispatcher.Invoke(() =>
                            {
                                UpdateUI(words);
                                ManageTtsState(words);
                            });
                        }
                    }
                    cts.Cancel();
                    }
                }
                catch (Exception)
                {
                    await Task.Delay(2000);
                }
                _channelId = null;
            }
        }

        private async Task HeartbeatLoopAsync(CancellationToken token)
        {
            var pingMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { "ping" }));
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(10000, token);
                if (_ws?.State == WebSocketState.Open)
                {
                    try { await _ws.SendAsync(new ArraySegment<byte>(pingMsg), WebSocketMessageType.Text, true, token); }
                    catch { break; }
                }
            }
        }

        private void UpdateUI(List<string> words)
        {
            foreach (var element in _symbolElements) MainCanvas.Children.Remove(element);
            _symbolElements.Clear();

            if (words.Count > 0)
            {
                BackgroundPanel.Opacity = 0.85; BossCircle.Opacity = 1.0;
                BossGlow.BlurRadius = 30; BossGlow.Opacity = 0.8;
            }
            else
            {
                BackgroundPanel.Opacity = 0.1; BossCircle.Opacity = 0.2;
                BossGlow.BlurRadius = 15; BossGlow.Opacity = 0.3;
            }

            for (int i = 0; i < words.Count && i < 5; i++)
            {
                string word = words[i].ToLower();
                if (_emojiMap.TryGetValue(word, out string symbol))
                {
                    string fontFamily = (symbol == "T") ? "Arial Black" : "Segoe UI Emoji";
                    var textBlock = new TextBlock
                    {
                        Text = symbol,
                        FontSize = 42,
                        FontFamily = new FontFamily(fontFamily),
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        Opacity = 0.8,
                        IsHitTestVisible = false
                    };

                    Canvas.SetLeft(textBlock, _pentagonPoints[i].X - 21);
                    Canvas.SetTop(textBlock, _pentagonPoints[i].Y - 21);
                    MainCanvas.Children.Add(textBlock);
                    _symbolElements.Add(textBlock);
                }
            }
        }

        private void SetSymbolHighlight(int index, bool isHighlighted)
        {
            if (index >= _symbolElements.Count) return;
            var element = _symbolElements[index];

            element.Foreground = isHighlighted
                ? new SolidColorBrush(Color.FromRgb(255, 180, 0))
                : Brushes.White;
        }

        private void ManageTtsState(List<string> words)
        {
            if (words.Count == 5 && TtsButton.IsChecked == true) StartTtsLoop(words);
            else StopTtsLoop();
        }

        private async void StartTtsLoop(List<string> words)
        {
            if (_ttsCts != null) return;
            _ttsCts = new CancellationTokenSource();
            var token = _ttsCts.Token;
            string sentence = string.Join(", ", words);
            _synth.Rate = 2;
            int repetitions = 0;

            try
            {
                while (!token.IsCancellationRequested && repetitions < 3)
                {
                    await Task.Run(() => { _synth.Speak(sentence); }, token);
                    repetitions++;
                    if (repetitions < 2) await Task.Delay(1000, token);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _ttsCts = null;
                Dispatcher.Invoke(() => { for (int i = 0; i < _symbolElements.Count; i++) SetSymbolHighlight(i, false); });
            }
        }

        private void StopTtsLoop()
        {
            if (_ttsCts != null) { _ttsCts.Cancel(); _synth.SpeakAsyncCancelAll(); }
        }

        private void TtsButton_Click(object sender, RoutedEventArgs e)
        {
            TtsButton.Content = TtsButton.IsChecked == true ? "🔊" : "🔇";
            var words = JsonSerializer.Deserialize<List<string>>(_lastWordsState ?? "[]") ?? new List<string>();
            ManageTtsState(words);
        }

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            StopTtsLoop();
            if (_ws != null && _ws.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            this.Close();
        }

        private void Synth_SpeakProgress(object sender, SpeakProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                for (int i = 0; i < _symbolElements.Count; i++) SetSymbolHighlight(i, false);
                var words = JsonSerializer.Deserialize<List<string>>(_lastWordsState ?? "[]") ?? new List<string>();
                int index = words.FindIndex(w => w.Equals(e.Text, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) SetSymbolHighlight(index, true);
            });
        }

        private void Synth_SpeakCompleted(object sender, SpeakCompletedEventArgs e)
        {
            Dispatcher.Invoke(() => { for (int i = 0; i < _symbolElements.Count; i++) SetSymbolHighlight(i, false); });
        }

        private void DragHandle_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left) this.DragMove();
        }
    }
}