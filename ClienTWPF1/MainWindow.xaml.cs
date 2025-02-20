using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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
            _ = FetchExchangeRates(); // Первичная загрузка при запуске
        }

        private async Task FetchExchangeRates()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string apiUrl = "https://api.exchangerate-api.com/v4/latest/USD"; // <-- Можно заменить на другой API
                    string response = await client.GetStringAsync(apiUrl);
                    var data = JObject.Parse(response);
                    var rates = data["rates"];

                    if (rates != null)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            string selectedCurrency = ToCurrencyComboBox.SelectedItem is Currency currency ? currency.Name : "UAH";
                            if (rates[selectedCurrency] != null)
                            {
                                double rate = rates[selectedCurrency].Value<double>();
                                ExchangeRateText.Text = $"Курс {selectedCurrency}: {rate:F4}";
                            }
                            else
                            {
                                ExchangeRateText.Text = "Ошибка: курс не найден!";
                            }
                        });
                    }
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


        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    using (TcpClient client = new TcpClient())
                    {
                        var result = client.BeginConnect(ServerIp, ServerPort, null, null);
                        bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));

                        Dispatcher.Invoke(() =>
                        {
                            if (success && client.Connected)
                            {
                                ConnectionStatusText.Text = "Подключено";
                                ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Green;
                            }
                            else
                            {
                                ConnectionStatusText.Text = "Сервер недоступен";
                                ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Red;
                            }
                        });
                    }
                }
                catch
                {
                    Dispatcher.Invoke(() =>
                    {
                        ConnectionStatusText.Text = "Сервер недоступен";
                        ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Red;
                    });
                }
            });
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectionStatusText.Text = "Отключено";
            ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Red;
        }

        private void GetExchangeRateButton_Click(object sender, RoutedEventArgs e)
        {
            if (isBlocked)
            {
                MessageBox.Show("Вы заблокированы. Попробуйте позже.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (FromCurrencyComboBox.SelectedItem is Currency fromCurrency && ToCurrencyComboBox.SelectedItem is Currency toCurrency)
            {
                string request = $"RATE {fromCurrency.Name} {toCurrency.Name}";
                SendRequestToServer(request, response =>
                {
                    Dispatcher.Invoke(() => ExchangeRateText.Text = response);
                });
            }
        }

        private void ConvertCurrencyButton_Click(object sender, RoutedEventArgs e)
        {
            if (isBlocked)
            {
                MessageBox.Show("Вы заблокированы. Попробуйте позже.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (decimal.TryParse(AmountTextBox.Text, out decimal amount) &&
                FromCurrencyComboBoxConvert.SelectedItem is Currency fromCurrency &&
                ToCurrencyComboBoxConvert.SelectedItem is Currency toCurrency)
            {
                string request = $"CONVERT {amount} {fromCurrency.Name} {toCurrency.Name}";
                SendRequestToServer(request, response =>
                {
                    Dispatcher.Invoke(() => ConversionResultTextBox.Text = response);
                });
            }
            else
            {
                ConversionResultTextBox.Text = "Ошибка ввода!";
            }
        }

        private void SendRequestToServer(string request, Action<string> callback)
        {
            Task.Run(() =>
            {
                try
                {
                    if (isBlocked)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show("Вы заблокированы! Подождите 1 минуту.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                        });
                        return;
                    }

                    using (TcpClient client = new TcpClient(ServerIp, ServerPort))
                    using (NetworkStream stream = client.GetStream())
                    {
                        byte[] data = Encoding.UTF8.GetBytes(request);
                        stream.Write(data, 0, data.Length);

                        byte[] buffer = new byte[1024];
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                        if (response.Contains("Превышен лимит запросов"))
                        {
                            isBlocked = true;
                            blockEndTime = DateTime.Now.AddMinutes(1);
                            Task.Run(async () =>
                            {
                                await Task.Delay(60000);
                                isBlocked = false;
                                Dispatcher.Invoke(() =>
                                {
                                    MessageBox.Show("Блокировка снята. Теперь можно делать запросы.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                                });
                            });
                        }

                        callback(response);
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => MessageBox.Show($"Ошибка подключения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error));
                }
            });
        }


        private void UnblockAfterDelay()
        {
            Thread.Sleep(60000);
            isBlocked = false;
            Dispatcher.Invoke(() => MessageBox.Show("Блокировка снята. Вы можете снова отправлять запросы.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information));
        }
    }
}
