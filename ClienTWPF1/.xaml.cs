using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;

namespace CurrencyExchangeClient
{
    public partial class MainWindow : Window
    {
        private const string ServerIp = "127.0.0.1";
        private const int ServerPort = 12345;
        private const string ApiUrl = "https://api.exchangerate-api.com/v4/latest/";

        private DateTime blockEndTime;
        private bool isBlocked = false;
        private DispatcherTimer updateTimer;
        private HttpClient httpClient = new HttpClient();

        public class Currency
        {
            public string Name { get; set; }
            public string Flag { get; set; }
        }

        private List<Currency> currencies = new List<Currency>
        {
            new Currency { Name = "USD", Flag = "https://flagcdn.com/w40/us.png" },
            new Currency { Name = "EUR", Flag = "https://flagcdn.com/w40/eu.png" },
            new Currency { Name = "UAH", Flag = "https://flagcdn.com/w40/ua.png" },
            new Currency { Name = "RUB", Flag = "https://flagcdn.com/w40/ru.png" },
            new Currency { Name = "PLN", Flag = "https://flagcdn.com/w40/pl.png" },
            new Currency { Name = "BYN", Flag = "https://flagcdn.com/w40/by.png" }
        };

        public MainWindow()
        {
            InitializeComponent();
            LoadCurrencies();
            StartAutoUpdate();
        }

        private void LoadCurrencies()
        {
            FromCurrencyComboBox.ItemsSource = currencies;
            ToCurrencyComboBox.ItemsSource = currencies;
            FromCurrencyComboBoxConvert.ItemsSource = currencies;
            ToCurrencyComboBoxConvert.ItemsSource = currencies;

            FromCurrencyComboBox.SelectedIndex = 0;
            ToCurrencyComboBox.SelectedIndex = 1;
            FromCurrencyComboBoxConvert.SelectedIndex = 0;
            ToCurrencyComboBoxConvert.SelectedIndex = 1;
        }

        private void CurrencySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is Currency selectedCurrency)
            {
                comboBox.ToolTip = $"{selectedCurrency.Name}";
            }
        }

        private void StartAutoUpdate()
        {
            updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            updateTimer.Tick += async (sender, e) => await FetchExchangeRates();
            updateTimer.Start();
            _ = FetchExchangeRates();
        }

        private async Task FetchExchangeRates()
        {
            try
            {
                string apiUrl = $"{ApiUrl}USD";
                string response = await httpClient.GetStringAsync(apiUrl);
                var data = JObject.Parse(response);
                var rates = data["rates"];

                if (rates != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (ToCurrencyComboBox.SelectedItem is Currency currency && rates[currency.Name] != null)
                        {
                            double rate = rates[currency.Name].Value<double>();
                            ExchangeRateText.Text = $"Курс {currency.Name}: {rate:F4}";
                        }
                        else
                        {
                            ExchangeRateText.Text = "Ошибка: курс не найден!";
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ExchangeRateText.Text = $"Ошибка обновления курса: {ex.Message}";
                });
            }
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    var result = client.BeginConnect(ServerIp, ServerPort, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));

                    if (success && client.Connected)
                    {
                        using (NetworkStream stream = client.GetStream())
                        {
                            byte[] data = Encoding.UTF8.GetBytes("ATTEMPTS");
                            await stream.WriteAsync(data, 0, data.Length);

                            byte[] buffer = new byte[1024];
                            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                            string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                            MessageBox.Show($"Количество оставшихся попыток: {response}", "Попытки", MessageBoxButton.OK, MessageBoxImage.Information);
                        }

                        Dispatcher.Invoke(() =>
                        {
                            ConnectionStatusText.Text = "Подключено";
                            ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Green;
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            ConnectionStatusText.Text = "Сервер недоступен";
                            ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Red;
                            MessageBox.Show("Не удалось подключиться к серверу.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Ошибка подключения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    ConnectionStatusText.Text = "Ошибка";
                    ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Red;
                });
            }
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectionStatusText.Text = "Отключено";
            ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Red;
        }

        private async void GetExchangeRateButton_Click(object sender, RoutedEventArgs e)
        {
            if (isBlocked)
            {
                var remainingTime = blockEndTime - DateTime.Now;

                if (remainingTime.TotalSeconds > 0)
                {
                    blockEndTime = DateTime.Now.AddMinutes(1);
                    MessageBox.Show($"Вы заблокированы до {blockEndTime:HH:mm:ss}. Попробуйте позже.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                else
                {
                    isBlocked = false;
                }
            }

            if (FromCurrencyComboBox.SelectedItem is Currency fromCurrency && ToCurrencyComboBox.SelectedItem is Currency toCurrency)
            {
                string request = $"RATE {fromCurrency.Name} {toCurrency.Name}";
                await SendRequestToServer(request, response => Dispatcher.Invoke(() => ExchangeRateText.Text = response));
            }
        }

        private async void ConvertCurrencyButton_Click(object sender, RoutedEventArgs e)
        {
            if (isBlocked)
            {
                var remainingTime = blockEndTime - DateTime.Now;

                if (remainingTime.TotalSeconds > 0)
                {
                    blockEndTime = DateTime.Now.AddMinutes(1);
                    MessageBox.Show($"Вы заблокированы до {blockEndTime:HH:mm:ss}. Попробуйте позже.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                else
                {
                    isBlocked = false;
                }
            }

            if (decimal.TryParse(AmountTextBox.Text, out decimal amount) &&
                FromCurrencyComboBoxConvert.SelectedItem is Currency fromCurrency &&
                ToCurrencyComboBoxConvert.SelectedItem is Currency toCurrency)
            {
                string request = $"CONVERT {amount} {fromCurrency.Name} {toCurrency.Name}";
                await SendRequestToServer(request, response => Dispatcher.Invoke(() => ConversionResultTextBox.Text = response));
            }
            else
            {
                ConversionResultTextBox.Text = "Ошибка ввода!";
            }
        }

        private async Task SendRequestToServer(string request, Action<string> callback)
        {
            try
            {
                if (isBlocked)
                {
                    var remainingTime = blockEndTime - DateTime.Now;

                    if (remainingTime.TotalSeconds > 0)
                    {
                        blockEndTime = DateTime.Now.AddMinutes(1);
                        MessageBox.Show($"Вы заблокированы. Попробуйте позже. Блокировка продлена до: {blockEndTime:HH:mm:ss}.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    else
                    {
                        isBlocked = false;
                    }
                }

                using (TcpClient client = new TcpClient(ServerIp, ServerPort))
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] data = Encoding.UTF8.GetBytes(request);
                    await stream.WriteAsync(data, 0, data.Length);

                    byte[] buffer = new byte[1024];
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    if (response.Contains("Превышен лимит запросов"))
                    {
                        isBlocked = true;
                        blockEndTime = DateTime.Now.AddMinutes(1);
                        MessageBox.Show($"Вы превысили лимит запросов и были заблокированы. Блокировка на 1 минуту. Новое время блокировки: {blockEndTime:HH:mm:ss}.", "Блокировка", MessageBoxButton.OK, MessageBoxImage.Warning);
                        _ = Task.Delay(60000).ContinueWith(_ => isBlocked = false);
                    }

                    callback(response);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => MessageBox.Show($"Ошибка подключения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }
    }
}
