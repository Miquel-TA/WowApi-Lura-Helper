using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Speech.Recognition;
using System.Text;
using System.Text.Json;
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
        private readonly HttpClient _http = new HttpClient();
        private readonly SpeechRecognitionEngine _recognizer;

        private const string Url = "https://localhost:7007/write"; // Cambiar a producción
        private const string ApiKey = "TokenSeguro123";
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

            Log("Sistema iniciado.");
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

            // Lógica explícita de borrado
            if (word == "borrar")
            {
                Log("🗑️ Borrado manual");
                await ResetAndSendAsync();
                return;
            }

            // Ignorar si el símbolo ya existe o si ya hay 5 símbolos
            if (_words.Contains(word) || _words.Count >= 5)
            {
                return;
            }

            _words.Add(word);
            await SendStateAsync();
        }

        private async Task ResetAndSendAsync()
        {
            _timer.Stop();
            _words.Clear();
            await SendStateAsync();
        }

        private async Task SendStateAsync()
        {
            CurrentStateText.Text = _words.Count > 0 ? $"[ {string.Join(", ", _words)} ]" : "[ Vacío ]";

            try
            {
                // Serialización explícita para .NET 4.8
                string json = JsonSerializer.Serialize(_words);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = content };
                request.Headers.Add("X-Auth-Token", ApiKey);

                var response = await _http.SendAsync(request);

                if (response.IsSuccessStatusCode)
                    Log($"🌐 Enviado a API: {CurrentStateText.Text}");
                else
                    Log($"⚠️ Error API: {response.StatusCode}");
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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}