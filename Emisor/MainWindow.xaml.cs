using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Speech.Recognition;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace EmisorApp
{
    public partial class MainWindow : Window
    {
        private readonly List<string> _words = new List<string>();
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private ClientWebSocket _ws = new ClientWebSocket();
        private readonly SpeechRecognitionEngine _recognizer;

        private string _channelId;

        private const string WsUrl = "wss://localhost:7007/ws";
        private readonly string[] _keywords = new[] { "té", "círculo", "triángulo", "equis", "rombo", "borrar" };

        public MainWindow()
        {
            InitializeComponent();

            _timer.Interval = TimeSpan.FromSeconds(15);
            _timer.Tick += async (s, e) => await ResetAndSendAsync();

            _recognizer = new SpeechRecognitionEngine(new System.Globalization.CultureInfo("es-ES"));
            _recognizer.LoadGrammar(new Grammar(new GrammarBuilder(new Choices(_keywords))));
            _recognizer.SpeechRecognized += Recognizer_SpeechRecognized;
            _recognizer.SetInputToDefaultAudioDevice();

            this.Left = SystemParameters.PrimaryScreenWidth * 0.7;
            this.Top = 20;

            _ = ConnectWebSocketAsync();
            Log("Sistema iniciado.");
        }

        private string PromptForPassword()
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
            panel.Children.Add(new TextBlock { Text = "Ingrese un canal:", Foreground = Brushes.White });
            var txtPassword = new TextBox { Margin = new Thickness(0, 10, 0, 10), Padding = new Thickness(3) };
            panel.Children.Add(txtPassword);
            var btnConnect = new Button { Content = "Conectar", Width = 90, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(5) };
            btnConnect.Click += (s, e) => { input = txtPassword.Text; promptWindow.DialogResult = true; };
            panel.Children.Add(btnConnect);

            promptWindow.Content = panel;
            promptWindow.ShowDialog();
            return input;
        }

        private async Task ConnectWebSocketAsync()
        {
            if (string.IsNullOrEmpty(_channelId))
                _channelId = PromptForPassword();
            if (string.IsNullOrEmpty(_channelId)) { this.Close(); return; }

            try
            {
                if (_ws.State == WebSocketState.Open) return;

                _ws = new ClientWebSocket();
                var uri = new Uri($"{WsUrl}?token={_channelId}");
                await _ws.ConnectAsync(uri, CancellationToken.None);

                Log("Conectado.");
            }
            catch (WebSocketException ex)
            {
                Log($"Error: {ex.Message}");
                MessageBox.Show($"Fallo de conexión.\n\n{ex.Message}", "Error de Conexión", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                Log($"Error de red: {ex.Message}");
            }
        }

        private void VoiceSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (VoiceSwitch.IsChecked == true)
            {
                VoiceSwitch.Content = "Escuchando...";
                VoiceSwitch.Background = Brushes.DarkRed;
                _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                Log("Micrófono ON.");
            }
            else
            {
                VoiceSwitch.Content = "Micrófono OFF";
                VoiceSwitch.Background = new SolidColorBrush(Color.FromRgb(68, 68, 68));
                _recognizer.RecognizeAsyncCancel();
                Log("Micrófono OFF.");
            }
        }

        private async void ManualBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string word) await ProcessKeywordAsync(word);
        }

        private void Recognizer_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            string word = e.Result.Text.ToLower();
            Application.Current.Dispatcher.Invoke(async () =>
            {
                Log($"Detectado: {word}");
                await ProcessKeywordAsync(word);
            });
        }

        private async Task ProcessKeywordAsync(string word)
        {
            _timer.Stop();
            _timer.Start();

            if (word == "borrar")
            {
                Log("Borrado");
                await ResetAndSendAsync();
                return;
            }

            if (_words.Contains(word) || _words.Count >= 5) return;

            _words.Add(word);
            UpdateButtonStates();
            await SendStateAsync();
        }

        private async Task ResetAndSendAsync()
        {
            _timer.Stop();
            _words.Clear();
            UpdateButtonStates();
            await SendStateAsync();
        }

        private async Task SendStateAsync()
        {
            CurrentStateText.Text = _words.Count > 0 ? $"[ {string.Join(", ", _words)} ]" : "[ Vacío ]";

            try
            {
                await ConnectWebSocketAsync();

                if (_ws.State == WebSocketState.Open)
                {
                    string json = JsonSerializer.Serialize(_words);
                    var buffer = Encoding.UTF8.GetBytes(json);

                    await _ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                    Log($"Enviado: {CurrentStateText.Text}");
                }
            }
            catch (Exception ex)
            {
                Log($"Error de red: {ex.Message}");
            }
        }

        private void Log(string message)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            LogConsole.Items.Insert(0, $"[{time}] {message}");

            if (LogConsole.Items.Count > 50)
                LogConsole.Items.RemoveAt(LogConsole.Items.Count - 1);
        }

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();

            try
            {
                _timer?.Stop();

                if (_recognizer != null)
                {
                    _recognizer.RecognizeAsyncCancel();
                    _recognizer.Dispose();
                }

                if (_ws != null && _ws.State == WebSocketState.Open)
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Cerrando app", cts.Token);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                Environment.Exit(0);
            }
        }

        private void UpdateButtonStates()
        {
            foreach (var child in ButtonsPanel.Children)
            {
                if (child is Button btn && btn.Tag is string tag && tag != "borrar")
                {
                    btn.IsEnabled = !_words.Contains(tag);
                }
            }
        }

        private void DragHandle_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left) this.DragMove();
        }
    }
}