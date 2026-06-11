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
using System.Windows.Threading;

namespace EmisorApp
{
    public partial class MainWindow : Window
    {
        private readonly List<string> _words = new List<string>();
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private ClientWebSocket _ws = new ClientWebSocket();
        private readonly SpeechRecognitionEngine _recognizer;

        private const string WsUrl = "wss://wowapi-lura.rinconplacas.com/ws"; // Adjust for your SSL/ws scheme
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

        private async Task ConnectWebSocketAsync()
        {
            try
            {
                if (_ws.State == WebSocketState.Open) return;

                _ws = new ClientWebSocket();
                await _ws.ConnectAsync(new Uri(WsUrl), CancellationToken.None);
                Log("🔗 Conectado al servidor WebSocket.");
            }
            catch (Exception ex)
            {
                Log($"❌ Error de conexión WS: {ex.Message}");
            }
        }

        private void VoiceSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (VoiceSwitch.IsChecked == true)
            {
                VoiceSwitch.Content = "🔴 Escuchando...";
                VoiceSwitch.Background = System.Windows.Media.Brushes.DarkRed;
                _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                Log("🎤 Micrófono activado.");
            }
            else
            {
                VoiceSwitch.Content = "🎤 Micrófono Apagado";
                VoiceSwitch.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(68, 68, 68));
                _recognizer.RecognizeAsyncCancel();
                Log("🎤 Micrófono desactivado.");
            }
        }

        private async void ManualBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string word)
            {
                await ProcessKeywordAsync(word);
            }
        }

        private void Recognizer_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            string word = e.Result.Text.ToLower();

            Application.Current.Dispatcher.Invoke(async () =>
            {
                Log($"🗣️ Detectado: {word}");
                await ProcessKeywordAsync(word);
            });
        }

        private async Task ProcessKeywordAsync(string word)
        {
            _timer.Stop();
            _timer.Start();

            if (word == "borrar")
            {
                Log("🗑️ Borrado manual");
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
                await ConnectWebSocketAsync(); // Ensure connected

                if (_ws.State == WebSocketState.Open)
                {
                    string json = JsonSerializer.Serialize(_words);
                    var buffer = Encoding.UTF8.GetBytes(json);

                    await _ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                    Log($"🌐 Enviado WS: {CurrentStateText.Text}");
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error de red: {ex.Message}");
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
            if (_ws.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Cerrando app", CancellationToken.None);

            this.Close();
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