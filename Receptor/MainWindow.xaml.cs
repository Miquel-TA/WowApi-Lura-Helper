using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Speech.Synthesis;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ListenerApp
{
    public partial class MainWindow : Window
    {
        private ClientWebSocket _ws;
        private readonly SpeechSynthesizer _synth = new SpeechSynthesizer();
        private const string WsUrl = "wss://wowapi-lura.rinconplacas.com/ws";

        private string _lastWordsState = string.Empty;
        private CancellationTokenSource _ttsCts;
        private readonly List<TextBlock> _symbolElements = new List<TextBlock>();

        private readonly Dictionary<string, string> _emojiMap = new Dictionary<string, string>
        {
            {"té", "T"}, {"círculo", "🔴"}, {"triángulo", "🔺"}, {"equis", "❌"}, {"rombo", "🔷"}
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

            _ = StartWebSocketListenerAsync();
        }

        private async Task StartWebSocketListenerAsync()
        {
            while (true)
            {
                try
                {
                    _ws = new ClientWebSocket();
                    await _ws.ConnectAsync(new Uri(WsUrl), CancellationToken.None);

                    var buffer = new byte[1024 * 4];

                    while (_ws.State == WebSocketState.Open)
                    {
                        var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                        if (result.MessageType == WebSocketMessageType.Close) break;

                        string response = Encoding.UTF8.GetString(buffer, 0, result.Count);

                        if (response != _lastWordsState)
                        {
                            _lastWordsState = response;
                            var words = JsonSerializer.Deserialize<List<string>>(response) ?? new List<string>();

                            Dispatcher.Invoke(() =>
                            {
                                UpdateUI(words);
                                ManageTtsState(words);
                            });
                        }
                    }
                }
                catch (Exception)
                {
                    // Wait before attempting to reconnect
                    await Task.Delay(2000);
                }
            }
        }

        private void UpdateUI(List<string> words)
        {
            foreach (var element in _symbolElements)
            {
                MainCanvas.Children.Remove(element);
            }
            _symbolElements.Clear();

            if (words.Count > 0)
            {
                BackgroundPanel.Opacity = 0.85;
                BossCircle.Opacity = 1.0;
                BossGlow.BlurRadius = 30;
                BossGlow.Opacity = 0.8;
            }
            else
            {
                BackgroundPanel.Opacity = 0.1;
                BossCircle.Opacity = 0.2;
                BossGlow.BlurRadius = 15;
                BossGlow.Opacity = 0.3;
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
                        Opacity = 0.9,
                        Effect = new DropShadowEffect { Color = Colors.White, BlurRadius = 15, ShadowDepth = 0, Opacity = 1 }
                    };

                    Canvas.SetLeft(textBlock, _pentagonPoints[i].X - 21);
                    Canvas.SetTop(textBlock, _pentagonPoints[i].Y - 21);

                    MainCanvas.Children.Add(textBlock);
                    _symbolElements.Add(textBlock);
                }
            }
        }

        private void ManageTtsState(List<string> words)
        {
            if (words.Count == 5 && TtsButton.IsChecked == true)
                StartTtsLoop(words);
            else
                StopTtsLoop();
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
                Dispatcher.Invoke(() =>
                {
                    for (int i = 0; i < _symbolElements.Count; i++) SetSymbolHighlight(i, false);
                });
            }
        }

        private void SetSymbolHighlight(int index, bool isHighlighted)
        {
            if (index >= _symbolElements.Count) return;

            var element = _symbolElements[index];
            element.Opacity = isHighlighted ? 1.0 : 0.9;

            if (element.Effect is DropShadowEffect shadow)
            {
                shadow.BlurRadius = isHighlighted ? 40 : 15;
            }
        }

        private void StopTtsLoop()
        {
            if (_ttsCts != null)
            {
                _ttsCts.Cancel();
                _synth.SpeakAsyncCancelAll();
            }
        }

        private void TtsButton_Click(object sender, RoutedEventArgs e)
        {
            TtsButton.Content = TtsButton.IsChecked == true ? "🔊" : "🔇";

            var words = JsonSerializer.Deserialize<List<string>>(_lastWordsState ?? "[]") ?? new List<string>();
            ManageTtsState(words);
        }

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
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
            Dispatcher.Invoke(() =>
            {
                for (int i = 0; i < _symbolElements.Count; i++) SetSymbolHighlight(i, false);
            });
        }

        private void DragHandle_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left) this.DragMove();
        }
    }
}