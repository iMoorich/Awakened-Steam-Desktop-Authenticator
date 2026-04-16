using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SteamGuard
{
    /// <summary>
    /// Сервис для работы с кошельком Steam
    /// </summary>
    public class WalletService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly SteamAccount _account;

        public WalletService(SteamAccount account, SettingsManager? settingsManager = null)
        {
            _account = account;
            _httpClient = SteamHttpClientFactory.CreateAuthenticatedClient(account, settingsManager);
        }

        /// <summary>
        /// Получить информацию о балансе кошелька
        /// </summary>
        public async Task<WalletInfo?> GetWalletInfoAsync()
        {
            try
            {
                AppLogger.Info("Получение баланса кошелька...");

                // Пробуем через Store API (более надёжный метод)
                var storeUrl = "https://store.steampowered.com/account/";
                var response = await _httpClient.GetStringAsync(storeUrl);

                // Проверяем авторизацию
                if (response.Contains("g_AccountID = 0") || response.Contains("\"logged_in\":false") || response.Contains("login/?"))
                {
                    AppLogger.Warn("Сессия не авторизована на Store - сессия устарела");
                    return new WalletInfo { IsSessionExpired = true };
                }

                // Парсим баланс из Store
                // Ищем: <div class="accountData price"><a href="...">33,84 руб.</a></div>
                var balanceMatch = Regex.Match(response, @"accountData\s+price[^>]*>(?:<a[^>]*>)?([^<]+)<", RegexOptions.IgnoreCase);
                if (!balanceMatch.Success)
                {
                    // Альтернативный паттерн для wallet_balance
                    balanceMatch = Regex.Match(response, @"wallet_balance[^>]*>(?:<a[^>]*>)?([^<]+)<", RegexOptions.IgnoreCase);
                }

                if (balanceMatch.Success)
                {
                    var balanceString = balanceMatch.Groups[1].Value.Trim();
                    AppLogger.Debug($"Найден баланс на Store: {balanceString}");

                    // Парсим валюту и сумму
                    var amountRegex = @"[\d\.,]+";
                    var amountMatch = Regex.Match(balanceString, amountRegex);

                    decimal balance = 0;
                    if (amountMatch.Success)
                    {
                        var amountStr = amountMatch.Value.Replace(",", ".");
                        decimal.TryParse(amountStr, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out balance);
                    }

                    // Извлекаем символ валюты (всё кроме цифр)
                    var currencySymbol = Regex.Replace(balanceString, amountRegex, string.Empty).Trim();

                    var walletInfo = new WalletInfo
                    {
                        Balance = balance,
                        BalanceFormatted = balanceString,
                        CurrencyCode = 0, // Определим позже если нужно
                        CurrencySymbol = currencySymbol
                    };

                    AppLogger.Info($"Баланс получен: {walletInfo.BalanceFormatted}");
                    return walletInfo;
                }

                // Если не нашли на Store, пробуем Market
                AppLogger.Warn("Не удалось найти баланс на странице Store");
                return await GetWalletInfoFromMarketAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Ошибка получения баланса кошелька", ex);
                // При любой ошибке считаем что сессия устарела
                return new WalletInfo { IsSessionExpired = true };
            }
        }

        /// <summary>
        /// Получить баланс через Community Market (запасной метод)
        /// </summary>
        private async Task<WalletInfo?> GetWalletInfoFromMarketAsync()
        {
            try
            {
                var marketUrl = "https://steamcommunity.com/market/";
                var response = await _httpClient.GetStringAsync(marketUrl);

                // Проверяем авторизацию на Market
                if (response.Contains("g_steamID = false") || response.Contains("\"logged_in\":false"))
                {
                    AppLogger.Warn("Сессия не авторизована на Market");
                    return new WalletInfo { IsSessionExpired = true };
                }

                // Пробуем несколько паттернов
                string balanceString = "";

                // Паттерн 1: Wallet (33,84 руб.)
                var walletMatch = Regex.Match(response, @"Wallet\s*\(([^\)]+)\)", RegexOptions.IgnoreCase);
                if (walletMatch.Success)
                {
                    balanceString = walletMatch.Groups[1].Value.Trim();
                    AppLogger.Debug($"Найден баланс через паттерн Wallet(): {balanceString}");
                }

                // Паттерн 2: marketWalletBalanceAmount
                if (string.IsNullOrEmpty(balanceString))
                {
                    var balanceMatch = Regex.Match(response, @"marketWalletBalanceAmount"">([^<]+)<", RegexOptions.IgnoreCase);
                    if (balanceMatch.Success)
                    {
                        balanceString = balanceMatch.Groups[1].Value.Trim();
                        AppLogger.Debug($"Найден баланс через marketWalletBalanceAmount: {balanceString}");
                    }
                }

                if (string.IsNullOrEmpty(balanceString))
                {
                    AppLogger.Warn("Market fallback: баланс не найден ни в одном паттерне");
                    return await GetWalletInfoFromInventoryAsync();
                }
                var currencyMatch = Regex.Match(response, @"wallet_currency"":(\d+)");
                var currencyCode = currencyMatch.Success ? currencyMatch.Groups[1].Value : "1";

                var amountRegex = @"[\d\.,]+";
                var amountMatch = Regex.Match(balanceString, amountRegex);

                decimal balance = 0;
                if (amountMatch.Success)
                {
                    var amountStr = amountMatch.Value.Replace(",", ".");
                    decimal.TryParse(amountStr, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out balance);
                }

                var currencySymbol = Regex.Replace(balanceString, amountRegex, string.Empty).Trim();

                return new WalletInfo
                {
                    Balance = balance,
                    BalanceFormatted = balanceString,
                    CurrencyCode = int.Parse(currencyCode),
                    CurrencySymbol = currencySymbol
                };
            }
            catch (Exception ex)
            {
                AppLogger.Error("Ошибка получения баланса через Market", ex);
                return await GetWalletInfoFromInventoryAsync();
            }
        }

        /// <summary>
        /// Получить баланс через альтернативный метод (JSON API)
        /// </summary>
        public async Task<WalletInfo?> GetWalletInfoFromInventoryAsync()
        {
            try
            {
                // При загрузке инвентаря Steam возвращает информацию о кошельке
                var inventoryUrl = $"https://steamcommunity.com/profiles/{_account.SteamId}/inventory/";
                var response = await _httpClient.GetStringAsync(inventoryUrl);

                // Ищем g_rgWalletInfo в JavaScript
                var walletMatch = Regex.Match(response, @"g_rgWalletInfo\s*=\s*({[^}]+})");
                if (!walletMatch.Success)
                {
                    return null;
                }

                var walletJson = walletMatch.Groups[1].Value;
                var walletData = JsonConvert.DeserializeObject<WalletInfoJson>(walletJson);

                if (walletData == null)
                {
                    return null;
                }

                // wallet_balance в центах
                var balance = walletData.WalletBalance / 100m;
                var currencySymbol = GetCurrencySymbol(walletData.WalletCurrency);

                return new WalletInfo
                {
                    Balance = balance,
                    BalanceFormatted = $"{currencySymbol}{balance:F2}",
                    CurrencyCode = walletData.WalletCurrency,
                    CurrencySymbol = currencySymbol
                };
            }
            catch (Exception ex)
            {
                AppLogger.Error("Ошибка получения баланса через инвентарь", ex);
                return null;
            }
        }

        /// <summary>
        /// Получить символ валюты по коду
        /// </summary>
        private string GetCurrencySymbol(int currencyCode)
        {
            return currencyCode switch
            {
                1 => "$",      // USD
                2 => "£",      // GBP
                3 => "€",      // EUR
                5 => "₽",      // RUB
                6 => "R$",     // BRL
                7 => "¥",      // JPY
                9 => "kr",     // NOK
                10 => "Rp",    // IDR
                11 => "RM",    // MYR
                12 => "₱",     // PHP
                13 => "S$",    // SGD
                14 => "฿",     // THB
                15 => "₫",     // VND
                16 => "₩",     // KRW
                17 => "TL",    // TRY
                18 => "₴",     // UAH
                19 => "Mex$",  // MXN
                20 => "CDN$",  // CAD
                21 => "A$",    // AUD
                22 => "NZ$",   // NZD
                23 => "₹",     // INR
                24 => "CLP$",  // CLP
                25 => "S/.",   // PEN
                26 => "COL$",  // COP
                27 => "zł",    // PLN
                28 => "CHF",   // CHF
                _ => "$"
            };
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Информация о кошельке Steam
    /// </summary>
    public class WalletInfo
    {
        /// <summary>Баланс в виде числа</summary>
        public decimal Balance { get; set; }

        /// <summary>Баланс в форматированном виде (например "$5.00")</summary>
        public string BalanceFormatted { get; set; } = string.Empty;

        /// <summary>Код валюты (1=USD, 3=EUR, 5=RUB и т.д.)</summary>
        public int CurrencyCode { get; set; }

        /// <summary>Символ валюты ($, €, ₽ и т.д.)</summary>
        public string CurrencySymbol { get; set; } = string.Empty;

        /// <summary>Указывает, что сессия не авторизована</summary>
        public bool IsSessionExpired { get; set; }
    }

    /// <summary>
    /// JSON модель для g_rgWalletInfo
    /// </summary>
    internal class WalletInfoJson
    {
        [JsonProperty("wallet_balance")]
        public int WalletBalance { get; set; }

        [JsonProperty("wallet_currency")]
        public int WalletCurrency { get; set; }
    }
}
