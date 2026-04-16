using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProtoBuf;

namespace SteamGuard
{
    /// <summary>
    /// Proto модель для тестовой сериализации через protobuf-net
    /// </summary>
    [ProtoContract]
    public class GuardCodeRequestProto
    {
        [ProtoMember(1)] public ulong ClientId { get; set; }
        [ProtoMember(2, DataFormat = DataFormat.FixedSize)] public ulong Steamid { get; set; }
        [ProtoMember(3)] public string Code { get; set; } = "";
        [ProtoMember(4)] public int CodeType { get; set; }
    }

    /// <summary>
    /// Сервис для работы с подтверждениями Steam
    /// </summary>
    public class ConfirmationService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly SteamAccount _account;
        private readonly SteamAuthenticator _authenticator;

        public ConfirmationService(SteamAccount account, SteamAuthenticator authenticator, SettingsManager? settingsManager = null)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
            _httpClient = SteamHttpClientFactory.CreateAuthenticatedClient(account, settingsManager);
            _httpClient.BaseAddress = new Uri(Constants.SteamCommunityUrl);
        }

        /// <summary>
        /// Получить список подтверждений
        /// </summary>
        public async Task<List<Confirmation>> GetConfirmationsAsync()
        {
            try
            {
                var queryParams = GenerateConfirmationQueryParams("list", out _, out _);
                string url = $"/mobileconf/getlist?{queryParams}";

                AppLogger.Debug($"GetConfirmations: {url}");
                var response = await _httpClient.GetAsync(url);
                var responseBody = await response.Content.ReadAsStringAsync();
                AppLogger.Debug($"GetConfirmations response: {responseBody}");

                var data = JObject.Parse(responseBody);

                var confirmations = new List<Confirmation>();

                if (data["conf"] is JArray confArray)
                {
                    AppLogger.Info($"Найдено подтверждений в массиве: {confArray.Count}");
                    foreach (var conf in confArray)
                    {
                        var confirmation = conf?.ToObject<Confirmation>();
                        if (confirmation != null)
                        {
                            AppLogger.Debug($"Подтверждение: id={confirmation.Id}, type={confirmation.IntType}, headline={confirmation.Headline}");
                            confirmations.Add(confirmation);
                        }
                    }
                }

                // Проверяем needconfirmation = 0 (пустой список)
                if (data["needconfirmation"]?.ToString() == "0")
                {
                    AppLogger.Info("Подтверждений нет (needconfirmation=0)");
                }
                else if (confirmations.Count > 0)
                {
                    AppLogger.Info($"Получено подтверждений: {confirmations.Count}");
                }
                else
                {
                    AppLogger.Warn("Подтверждений не найдено в ответе");
                }

                return confirmations;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Ошибка получения подтверждений", ex);
                return new List<Confirmation>();
            }
        }

        /// <summary>
        /// Принять подтверждение
        /// </summary>
        public async Task<bool> AcceptConfirmationAsync(Confirmation confirmation)
        {
            return await RespondToConfirmationAsync(confirmation, true);
        }

        /// <summary>
        /// Отклонить подтверждение
        /// </summary>
        public async Task<bool> DenyConfirmationAsync(Confirmation confirmation)
        {
            return await RespondToConfirmationAsync(confirmation, false);
        }

        /// <summary>
        /// Принять все подтверждения
        /// </summary>
        public async Task<bool> AcceptAllConfirmationsAsync()
        {
            var confirmations = await GetConfirmationsAsync();
            if (confirmations.Count == 0)
                return true;

            // Принимаем каждое подтверждение по отдельности
            bool allSuccess = true;
            foreach (var confirmation in confirmations)
            {
                bool success = await AcceptConfirmationAsync(confirmation);
                if (!success)
                {
                    AppLogger.Warn($"Не удалось принять подтверждение {confirmation.Id}");
                    allSuccess = false;
                }
            }

            return allSuccess;
        }

        private async Task<bool> RespondToConfirmationAsync(Confirmation confirmation, bool accept)
        {
            string action = accept ? "allow" : "cancel";

            // Генерируем новые параметры для каждого запроса
            var queryParams = GenerateConfirmationQueryParams(action, out _, out _);

            // Добавляем op, cid и ck параметры
            string url = $"/mobileconf/ajaxop?op={action}&{queryParams}&cid={confirmation.Id}&ck={confirmation.Nonce}";

            AppLogger.Debug($"{action} confirmation: {url}");

            // Используем GET запрос как в Nebula Auth
            var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();
            AppLogger.Debug($"Response: {json}");
            var data = JObject.Parse(json);

            return data["success"]?.Value<bool>() ?? false;
        }

        /// <summary>
        /// Получить детали трейда
        /// </summary>
        public async Task<string> GetTradeDetailsAsync(string tradeId)
        {
            var queryParams = GenerateConfirmationQueryParams("details", out _, out _);
            string url = $"/mobileconf/details/{tradeId}?{queryParams}";

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// Генерация query-параметров с HMAC-SHA1 подписью для mobileconf API
        /// </summary>
        private string GenerateConfirmationQueryParams(string action, out string signature, out long time)
        {
            time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Декодируем identity_secret из base64
            byte[] identityBytes;
            try
            {
                identityBytes = Convert.FromBase64String(_account.IdentitySecret);
            }
            catch
            {
                // Если не base64, пробуем как сырые байты
                identityBytes = Encoding.UTF8.GetBytes(_account.IdentitySecret);
            }

            // Формируем данные для подписи: time (8 байт big-endian) + action (ASCII)
            int tagLength = action.Length > 32 ? 32 : action.Length;
            var dataToSign = new byte[8 + tagLength];

            // Записываем time в big-endian (старшие байты первыми)
            long timeValue = time;
            for (int i = 7; i >= 0; i--)
            {
                dataToSign[i] = (byte)timeValue;
                timeValue >>= 8;
            }

            // Добавляем tag
            Array.Copy(Encoding.UTF8.GetBytes(action), 0, dataToSign, 8, tagLength);

            // HMAC-SHA1 подпись
            using var hmac = new System.Security.Cryptography.HMACSHA1(identityBytes);
            var hash = hmac.ComputeHash(dataToSign);

            // Кодируем подпись в URL-safe base64
            signature = Uri.EscapeDataString(Convert.ToBase64String(hash));

            return $"p={_account.DeviceId}&" +
                   $"a={_account.SteamId}&" +
                   $"k={signature}&" +
                   $"t={time}&" +
                   $"m=react&" +
                   $"tag={action}";
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Сервис для работы с торговыми предложениями
    /// </summary>
    public class TradeService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly SteamAccount _account;

        public TradeService(SteamAccount account, SettingsManager? settingsManager = null)
        {
            _account = account;
            _httpClient = SteamHttpClientFactory.CreateAuthenticatedClient(account, settingsManager);
        }

        /// <summary>
        /// Получить входящие трейды
        /// </summary>
        public async Task<List<TradeOffer>> GetIncomingTradesAsync()
        {
            try
            {
                var url = "https://steamcommunity.com/tradeoffermanager/tradeoffers/v1/?get_received_offers=1&active_only=1";
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                
                var trades = new List<TradeOffer>();
                if (json["response"]?["trade_offers_received"] is JArray offersArray)
                {
                    foreach (var offer in offersArray)
                    {
                        var tradeOffer = offer.ToObject<TradeOffer>();
                        if (tradeOffer != null)
                        {
                            trades.Add(tradeOffer);
                        }
                    }
                }
                
                AppLogger.Info($"Получено входящих трейдов: {trades.Count}");
                return trades;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ошибка получения входящих трейдов: {ex.Message}");
                return new List<TradeOffer>();
            }
        }

        /// <summary>
        /// Получить исходящие трейды
        /// </summary>
        public async Task<List<TradeOffer>> GetSentTradesAsync()
        {
            try
            {
                var url = "https://steamcommunity.com/tradeoffermanager/tradeoffers/v1/?get_sent_offers=1&active_only=1";
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                
                var trades = new List<TradeOffer>();
                if (json["response"]?["trade_offers_sent"] is JArray offersArray)
                {
                    foreach (var offer in offersArray)
                    {
                        var tradeOffer = offer.ToObject<TradeOffer>();
                        if (tradeOffer != null)
                        {
                            trades.Add(tradeOffer);
                        }
                    }
                }
                
                AppLogger.Info($"Получено исходящих трейдов: {trades.Count}");
                return trades;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ошибка получения исходящих трейдов: {ex.Message}");
                return new List<TradeOffer>();
            }
        }

        /// <summary>
        /// Получить активные трейды (входящие + исходящие)
        /// </summary>
        public async Task<List<TradeOffer>> GetActiveTradesAsync()
        {
            var incoming = await GetIncomingTradesAsync();
            var sent = await GetSentTradesAsync();
            return incoming.Concat(sent).ToList();
        }

        /// <summary>
        /// Принять трейд
        /// </summary>
        public async Task<bool> AcceptTradeAsync(string tradeOfferId)
        {
            try
            {
                var url = $"https://steamcommunity.com/tradeoffer/{tradeOfferId}/accept";
                var requestData = new Dictionary<string, string>
                {
                    ["sessionid"] = _account.SessionId,
                    ["serverid"] = "1",
                    ["tradeofferid"] = tradeOfferId,
                    ["partner"] = "0"
                };
                
                var content = new FormUrlEncodedContent(requestData);
                var response = await _httpClient.PostAsync(url, content);
                var responseJson = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(responseJson);
                
                var success = json["trade_offer_state"]?.Value<int>() == 3; // 3 = Accepted
                if (success)
                {
                    AppLogger.Info($"Трейд {tradeOfferId} принят");
                }
                else
                {
                    AppLogger.Error($"Не удалось принять трейд {tradeOfferId}");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ошибка принятия трейда: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Отклонить трейд
        /// </summary>
        public async Task<bool> DeclineTradeAsync(string tradeOfferId)
        {
            try
            {
                var url = $"https://steamcommunity.com/tradeoffer/{tradeOfferId}/decline";
                var requestData = new Dictionary<string, string>
                {
                    ["sessionid"] = _account.SessionId,
                    ["tradeofferid"] = tradeOfferId
                };
                
                var content = new FormUrlEncodedContent(requestData);
                var response = await _httpClient.PostAsync(url, content);
                var responseJson = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(responseJson);
                
                var success = json["trade_offer_state"]?.Value<int>() == 2; // 2 = Declined
                if (success)
                {
                    AppLogger.Info($"Трейд {tradeOfferId} отклонен");
                }
                else
                {
                    AppLogger.Error($"Не удалось отклонить трейд {tradeOfferId}");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ошибка отклонения трейда: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Отменить свой трейд
        /// </summary>
        public async Task<bool> CancelTradeAsync(string tradeOfferId)
        {
            try
            {
                var url = $"https://steamcommunity.com/tradeoffer/{tradeOfferId}/cancel";
                var requestData = new Dictionary<string, string>
                {
                    ["sessionid"] = _account.SessionId,
                    ["tradeofferid"] = tradeOfferId
                };
                
                var content = new FormUrlEncodedContent(requestData);
                var response = await _httpClient.PostAsync(url, content);
                var responseJson = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(responseJson);
                
                var success = json["trade_offer_state"]?.Value<int>() == 7; // 7 = Canceled
                if (success)
                {
                    AppLogger.Info($"Трейд {tradeOfferId} отменен");
                }
                else
                {
                    AppLogger.Error($"Не удалось отменить трейд {tradeOfferId}");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ошибка отмены трейда: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Получить детали трейда
        /// </summary>
        public async Task<TradeOffer> GetTradeOfferAsync(string tradeOfferId)
        {
            try
            {
                var url = $"https://steamcommunity.com/tradeoffer/{tradeOfferId}/";
                var html = await _httpClient.GetStringAsync(url);
                
                // Парсим HTML для извлечения данных о трейде
                // Это упрощенная версия - в реальном приложении лучше использовать API
                var tradeOffer = new TradeOffer { TradeOfferId = tradeOfferId };
                return tradeOffer;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ошибка получения деталей трейда: {ex.Message}");
                return new TradeOffer();
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Модель торгового предложения
    /// </summary>
    public class TradeOffer
    {
        [JsonProperty("trade_offer_id")]
        public string TradeOfferId { get; set; } = string.Empty;
        
        [JsonProperty("partner_steam_id")]
        public string PartnerSteamId { get; set; } = string.Empty;
        
        [JsonProperty("account_name")]
        public string AccountName { get; set; } = string.Empty;
        
        [JsonProperty("trade_offer_state")]
        public string TradeOfferState { get; set; } = string.Empty;
        
        [JsonProperty("items_to_give")]
        public List<TradeAsset> ItemsToGive { get; set; } = new();
        
        [JsonProperty("items_to_receive")]
        public List<TradeAsset> ItemsToReceive { get; set; } = new();
        
        [JsonProperty("time_created")]
        public long TimeCreated { get; set; }
        
        [JsonProperty("time_expiration")]
        public long TimeExpiration { get; set; }
        
        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;
        
        public bool IsOurOffer => TradeOfferState == "SentByMe";
    }

    /// <summary>
    /// Модель предмета в трейде
    /// </summary>
    public class TradeAsset
    {
        [JsonProperty("appid")]
        public long AppId { get; set; }
        
        [JsonProperty("contextid")]
        public long ContextId { get; set; }
        
        [JsonProperty("assetid")]
        public string AssetId { get; set; } = string.Empty;
        
        [JsonProperty("classid")]
        public string ClassId { get; set; } = string.Empty;
        
        [JsonProperty("instanceid")]
        public string InstanceId { get; set; } = string.Empty;
        
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("market_hash_name")]
        public string MarketHashName { get; set; } = string.Empty;
        
        [JsonProperty("icon_url")]
        public string IconUrl { get; set; } = string.Empty;
        
        [JsonProperty("color")]
        public string Color { get; set; } = string.Empty;
        
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
        
        [JsonProperty("rarity")]
        public string Rarity { get; set; } = string.Empty;
        
        [JsonProperty("price")]
        public decimal Price { get; set; }
    }

    /// <summary>
    /// Сервис для работы с маркетом Steam
    /// </summary>
    public class MarketService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly SteamAccount _account;

        public MarketService(SteamAccount account, SettingsManager? settingsManager = null)
        {
            _account = account;
            _httpClient = SteamHttpClientFactory.CreateAuthenticatedClient(account, settingsManager);
        }

        /// <summary>
        /// Получить активные листинги на маркете
        /// </summary>
        public async Task<List<MarketListing>> GetActiveListingsAsync()
        {
            try
            {
                var url = "https://steamcommunity.com/market/mylistings/render?count=100&start=0";
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                
                var listings = new List<MarketListing>();
                
                if (json["assets"] is JObject assets)
                {
                    // Парсим активы
                    foreach (var app in assets.Properties())
                    {
                        var appId = app.Name;
                        if (app.Value is JObject contextObj && contextObj["2"] is JObject items) // Context ID 2 для большинства игр
                        {
                            foreach (var item in items.Properties())
                            {
                                var assetId = item.Name;
                                if (item.Value is JObject assetData)
                                {
                                    var listing = new MarketListing
                                    {
                                        AssetId = assetId,
                                        AppId = long.Parse(appId),
                                        Name = assetData["name"]?.ToString() ?? "",
                                        MarketHashName = assetData["market_hash_name"]?.ToString() ?? "",
                                        IconUrl = assetData["icon_url"]?.ToString() ?? "",
                                        Type = assetData["type"]?.ToString() ?? "",
                                        Game = GetGameName(long.Parse(appId))
                                    };
                                    listings.Add(listing);
                                }
                            }
                        }
                    }
                }
                
                if (json["listings"] is JArray listingsArray)
                {
                    for (int i = 0; i < listingsArray.Count && i < listings.Count; i++)
                    {
                        var listingData = listingsArray[i];
                        if (listings[i] != null)
                        {
                            listings[i].ListingId = listingData["listingid"]?.ToString() ?? "";
                            listings[i].Price = ParsePrice(listingData["price"]?.ToString());
                            listings[i].Fees = ParsePrice(listingData["fees"]?.ToString());
                            listings[i].NetPrice = listings[i].Price - listings[i].Fees;
                            listings[i].Status = "active";
                            listings[i].TimeCreated = listingData["created_on"]?.Value<long>() ?? 0;
                        }
                    }
                }
                
                AppLogger.Info($"Получено активных листингов: {listings.Count}");
                return listings;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ошибка получения листингов: {ex.Message}");
                return new List<MarketListing>();
            }
        }

        /// <summary>
        /// Получить историю продаж
        /// </summary>
        public async Task<List<MarketListing>> GetSalesHistoryAsync()
        {
            try
            {
                var url = "https://steamcommunity.com/market/sellhistory/render?count=100&start=0";
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                
                var sales = new List<MarketListing>();
                
                if (json["results_html"] is JArray resultsArray)
                {
                    foreach (var result in resultsArray)
                    {
                        var sale = new MarketListing
                        {
                            Name = result["name"]?.ToString() ?? "",
                            MarketHashName = result["market_hash_name"]?.ToString() ?? "",
                            Price = ParsePrice(result["price"]?.ToString()),
                            Status = "sold",
                            TimeCreated = result["sold_on"]?.Value<long>() ?? 0
                        };
                        sales.Add(sale);
                    }
                }
                
                AppLogger.Info($"Получено продаж: {sales.Count}");
                return sales;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ошибка получения истории продаж: {ex.Message}");
                return new List<MarketListing>();
            }
        }

        /// <summary>
        /// Создать листинг на маркете
        /// </summary>
        public async Task<bool> CreateListingAsync(string assetId, long appId, decimal price)
        {
            try
            {
                var url = "https://steamcommunity.com/market/sellitem/";
                var requestData = new Dictionary<string, string>
                {
                    ["sessionid"] = _account.SessionId,
                    ["appid"] = appId.ToString(),
                    ["contextid"] = "2",
                    ["assetid"] = assetId,
                    ["amount"] = "1",
                    ["price"] = ((int)(price * 100)).ToString() // Цена в центах
                };
                
                var content = new FormUrlEncodedContent(requestData);
                var response = await _httpClient.PostAsync(url, content);
                var responseJson = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(responseJson);
                
                var success = json["success"]?.Value<bool>() ?? false;
                if (success)
                {
                    AppLogger.Info($"Листинг создан для актива {assetId}");
                }
                else
                {
                    AppLogger.Error($"Не удалось создать листинг: {json["message"]?.ToString()}");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ошибка создания листинга: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Отменить листинг
        /// </summary>
        public async Task<bool> CancelListingAsync(string listingId)
        {
            try
            {
                var url = $"https://steamcommunity.com/market/removelisting/{listingId}";
                var requestData = new Dictionary<string, string>
                {
                    ["sessionid"] = _account.SessionId,
                    ["listingid"] = listingId
                };
                
                var content = new FormUrlEncodedContent(requestData);
                var response = await _httpClient.PostAsync(url, content);
                var responseJson = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(responseJson);
                
                var success = json["success"]?.Value<bool>() ?? false;
                if (success)
                {
                    AppLogger.Info($"Листинг {listingId} отменен");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ошибка отмены листинга: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Принять листинг (подтверждение через confirmation)
        /// </summary>
        public async Task<bool> AcceptListingConfirmationAsync(string listingId)
        {
            // Подтверждение листинга происходит через ConfirmationService
            // Этот метод может быть использован для автоматического принятия
            try
            {
                AppLogger.Info($"Подтверждение листинга {listingId} через систему подтверждений");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ошибка подтверждения листинга: {ex.Message}");
                return false;
            }
        }

        private decimal ParsePrice(string priceStr)
        {
            if (string.IsNullOrEmpty(priceStr)) return 0;
            
            // Удаляем валюту и пробелы
            priceStr = priceStr.Replace("$", "").Replace(",", "").Replace(" ", "").Trim();
            
            if (decimal.TryParse(priceStr, out var price))
                return price;
            
            return 0;
        }

        private string GetGameName(long appId)
        {
            return appId switch
            {
                730 => "CS2",
                570 => "Dota 2",
                440 => "Team Fortress 2",
                252490 => "Rust",
                578080 => "PUBG",
                _ => $"App {appId}"
            };
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Модель листинга на маркете
    /// </summary>
    public class MarketListing
    {
        [JsonProperty("listing_id")]
        public string ListingId { get; set; } = string.Empty;
        
        [JsonProperty("asset_id")]
        public string AssetId { get; set; } = string.Empty;
        
        [JsonProperty("appid")]
        public long AppId { get; set; }
        
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("market_hash_name")]
        public string MarketHashName { get; set; } = string.Empty;
        
        [JsonProperty("icon_url")]
        public string IconUrl { get; set; } = string.Empty;
        
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
        
        [JsonProperty("price")]
        public decimal Price { get; set; }
        
        [JsonProperty("fees")]
        public decimal Fees { get; set; }
        
        [JsonProperty("net_price")]
        public decimal NetPrice { get; set; }
        
        [JsonProperty("status")]
        public string Status { get; set; } = string.Empty;
        
        [JsonProperty("time_created")]
        public long TimeCreated { get; set; }
        
        [JsonIgnore]
        public DateTime CreationDate => DateTimeOffset.FromUnixTimeSeconds(TimeCreated).LocalDateTime;
        
        [JsonProperty("game")]
        public string Game { get; set; } = string.Empty;
        
        [JsonProperty("rarity")]
        public string Rarity { get; set; } = string.Empty;
        
        [JsonProperty("exterior")]
        public string Exterior { get; set; } = string.Empty;
    }

    /// <summary>
    /// Сервис для обновления и восстановления сессии Steam
    /// </summary>
    public static class SessionRefreshService
    {
        private const string ApiUrl = Constants.AuthGenerateAccessTokenUrl;

        /// <summary>
        /// Обновить сессию аккаунта используя RefreshToken
        /// </summary>
        public static async Task<bool> RefreshSessionAsync(SteamAccount account)
        {
            AppLogger.Info($"RefreshSessionAsync: начало для аккаунта {account.Username}");

            if (account.Session == null || string.IsNullOrEmpty(account.Session.RefreshToken))
            {
                AppLogger.Warn($"RefreshSessionAsync: нет Session или RefreshToken для {account.Username}");
                return false;
            }

            var refreshToken = account.Session.RefreshToken;
            var steamId = account.Session.SteamId > 0 ? account.Session.SteamId : account.SteamId;

            if (steamId == 0)
            {
                AppLogger.Warn($"RefreshSessionAsync: SteamId = 0 для {account.Username}");
                return false;
            }

            AppLogger.Debug($"RefreshSessionAsync: SteamId={steamId}, RefreshToken length={refreshToken.Length}");

            try
            {
                var requestBody = EncodeRefreshRequest(refreshToken, steamId);
                var base64Body = Convert.ToBase64String(requestBody);

                var client = SteamHttpClientFactory.GetSharedClient();

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("input_protobuf_encoded", base64Body)
                });

                var response = await client.PostAsync(ApiUrl, content);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Warn($"RefreshSessionAsync: API вернул код {response.StatusCode}");
                    return false;
                }

                var responseBytes = await response.Content.ReadAsByteArrayAsync();
                var result = DecodeRefreshResponse(responseBytes);

                if (string.IsNullOrEmpty(result.accessToken))
                {
                    AppLogger.Warn("RefreshSessionAsync: accessToken пустой в ответе");
                    return false;
                }

                AppLogger.Info($"RefreshSessionAsync: получен новый accessToken (length={result.accessToken.Length})");

                account.Session.AccessToken = result.accessToken;
                account.Session.SteamLoginSecure = $"{steamId}%7C%7C{result.accessToken}";
                account.Session.SteamId = steamId;

                if (!string.IsNullOrEmpty(result.refreshToken))
                {
                    account.Session.RefreshToken = result.refreshToken;
                    AppLogger.Debug("RefreshSessionAsync: обновлён RefreshToken");
                }

                // Генерируем новый SessionId если его нет
                if (string.IsNullOrEmpty(account.Session.SessionId))
                {
                    account.Session.SessionId = SessionLoginService.GenerateSessionId();
                    AppLogger.Info($"RefreshSessionAsync: сгенерирован новый SessionId: {account.Session.SessionId}");
                }
                else
                {
                    AppLogger.Debug($"RefreshSessionAsync: используется существующий SessionId: {account.Session.SessionId}");
                }

                AppLogger.Info($"RefreshSessionAsync: успешно обновлена сессия для {account.Username}");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Ошибка обновления сессии через RefreshToken", ex);
                return false;
            }
        }

        private static byte[] EncodeRefreshRequest(string refreshToken, long steamId)
        {
            using var ms = new MemoryStream();
            ProtobufHelper.WriteString(ms, 1, refreshToken);
            ProtobufHelper.WriteVarint(ms, 2 << 3);
            ProtobufHelper.WriteVarint(ms, (ulong)steamId);
            ProtobufHelper.WriteVarint(ms, (3 << 3) | 0);
            ProtobufHelper.WriteVarint(ms, 1);
            return ms.ToArray();
        }

        private static (string accessToken, string refreshToken) DecodeRefreshResponse(byte[] data)
        {
            string accessToken = "";
            string refreshToken = "";

            using var ms = new MemoryStream(data);
            var reader = new BinaryReader(ms);

            while (ms.Position < ms.Length)
            {
                uint tag = (uint)ReadVarint(reader);
                int fieldNumber = (int)(tag >> 3);
                int wireType = (int)(tag & 0x07);

                if (fieldNumber == 1 && wireType == 2)
                    accessToken = ReadString(reader);
                else if (fieldNumber == 2 && wireType == 2)
                    refreshToken = ReadString(reader);
                else
                    SkipField(reader, wireType);
            }

            return (accessToken, refreshToken);
        }

        private static uint ReadVarint(BinaryReader reader)
        {
            uint result = 0;
            int shift = 0;
            while (true)
            {
                byte b = reader.ReadByte();
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    break;
                shift += 7;
            }
            return result;
        }

        private static string ReadString(BinaryReader reader)
        {
            int length = (int)ReadVarint(reader);
            var bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }

        private static void SkipField(BinaryReader reader, int wireType)
        {
            switch (wireType)
            {
                case 0: ReadVarint(reader); break;
                case 1: reader.ReadBytes(8); break;
                case 2: int len = (int)ReadVarint(reader); reader.ReadBytes(len); break;
                case 5: reader.ReadBytes(4); break;
            }
        }
    }

    /// <summary>
    /// Результат попытки входа
    /// </summary>
    public class LoginResult
    {
        public bool Success { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? SteamLoginSecure { get; set; }
        public string? SessionId { get; set; }
        public long SteamId { get; set; }
        public bool NeedsEmailCode { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    /// <summary>
    /// Сервис для полной авторизации через Steam API
    /// </summary>
    public static class SessionLoginService
    {
        private const string ApiBase = Constants.SteamApiBase;

        /// <summary>
        /// Полная авторизация: сначала RefreshToken, потом логин с паролем
        /// </summary>
        public static async Task<LoginResult> LoginOrRefreshAsync(SteamAccount account)
        {
            // 1. Пробуем через RefreshToken
            if (await SessionRefreshService.RefreshSessionAsync(account))
            {
                return new LoginResult
                {
                    Success = true,
                    AccessToken = account.Session?.AccessToken,
                    RefreshToken = account.Session?.RefreshToken,
                    SteamLoginSecure = account.Session?.SteamLoginSecure,
                    SessionId = account.Session?.SessionId,
                    SteamId = account.Session?.SteamId ?? account.SteamId
                };
            }

            // 2. Если нет RefreshToken - пробуем полную авторизацию
            if (string.IsNullOrEmpty(account.Password))
            {
                return new LoginResult { Success = false, Error = "Нет пароля для авторизации" };
            }

            return await FullLoginAsync(account.Username, account.Password, account.SharedSecret);
        }

        /// <summary>
        /// Полная авторизация через Steam API
        /// </summary>
        public static async Task<LoginResult> FullLoginAsync(string username, string password, string sharedSecret, string emailCode = "")
        {
            AppLogger.Info($"FullLoginAsync: {username}");
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "okhttp/3.12.12");
                client.DefaultRequestHeaders.Referrer = new Uri("https://steamcommunity.com");
                client.DefaultRequestHeaders.Add("Origin", "https://steamcommunity.com");
                client.DefaultRequestHeaders.Add("Accept-Language", "en-US");
                client.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, text/html, application/xml, text/xml, */*");

                // Шаг 1: Получаем RSA публичный ключ
                AppLogger.Info($"Запрос RSA ключа для {username}");
                var rsaResult = await GetPasswordRsaPublicKeyAsync(client, username);
                if (rsaResult == null)
                {
                    AppLogger.Error("Не удалось получить RSA ключ");
                    return new LoginResult { Success = false, Error = "Не удалось получить RSA ключ" };
                }

                var rsaData = rsaResult.Value;
                AppLogger.Info($"RSA ключ получен. timestamp: {rsaData.timestamp}");

                // Steam timestamp уже в миллисекундах (13 цифр)
                var timestamp = rsaData.timestamp;

                // Шаг 2: Шифруем пароль
                var encryptedPassword = CryptoHelper.EncryptPasswordRsa(password, rsaData.publicKeyMod, rsaData.publicKeyExp);
                AppLogger.Info($"Пароль зашифрован, длина: {encryptedPassword.Length}");

                // Шаг 3: BeginAuthSessionViaCredentials (используем timestamp из RSA API)
                var beginResult = await BeginAuthSessionAsync(client, username, encryptedPassword, rsaData.timestamp);
                if (beginResult == null)
                {
                    AppLogger.Error("Не удалось начать сессию");
                    return new LoginResult { Success = false, Error = "Не удалось начать сессию" };
                }

                var beginData = beginResult.Value;
                if (beginData.clientId == 0)
                {
                    var errorMsg = string.IsNullOrEmpty(beginData.errorMessage) ? "Steam вернул пустой ответ" : beginData.errorMessage;
                    AppLogger.Error($"BeginAuth ошибка: {errorMsg}");
                    return new LoginResult { Success = false, Error = $"Ошибка авторизации: {errorMsg}" };
                }
                var clientId = beginData.clientId;
                var steamId = beginData.steamId;
                var requestId = beginData.requestId;
                var allowedConfirmations = beginData.allowedConfirmations;

                AppLogger.Info($"Сессия начата: clientId={clientId}, steamId={steamId}, guardTypes={string.Join(",", allowedConfirmations)}");

                // Шаг 4: Обрабатываем guard-коды
                if (allowedConfirmations.Length > 0)
                {
                    var guardType = allowedConfirmations[0];
                    AppLogger.Info($"Требуется guard код типа {guardType}");

                    if (guardType == 2) // EmailCode
                    {
                        if (string.IsNullOrEmpty(emailCode))
                        {
                            AppLogger.Info("Нужен код с почты");
                            return new LoginResult { Success = false, NeedsEmailCode = true, Error = "Требуется код с почты" };
                        }

                        var emailSent = await SendGuardCodeAsync(client, emailCode, clientId, steamId, 2);
                        if (!emailSent)
                            return new LoginResult { Success = false, Error = "Неверный код с почты" };
                        AppLogger.Info("Код с почты отправлен");
                    }
                    else if (guardType == 3 && !string.IsNullOrEmpty(sharedSecret)) // DeviceCode (2FA)
                    {
                        // Синхронизируем время с серверами Steam перед генерацией кода
                        await SteamAuthenticator.AlignTimeAsync();
                        var steamTime = await SteamAuthenticator.GetSteamTimeAsync();
                        
                        // Пробуем текущий, предыдущий и следующий код
                        var codes = new[]
                        {
                            Generate2FACode(sharedSecret, steamTime - 30), // предыдущий
                            Generate2FACode(sharedSecret, steamTime),       // текущий
                            Generate2FACode(sharedSecret, steamTime + 30)   // следующий
                        };
                        
                        bool guardSent = false;
                        foreach (var code in codes)
                        {
                            AppLogger.Info($"Пробуем 2FA код: {code}");
                            guardSent = await SendGuardCodeAsync(client, code, clientId, steamId, 3);
                            if (guardSent)
                            {
                                AppLogger.Info("2FA код отправлен успешно");
                                break;
                            }
                        }
                        
                        if (!guardSent)
                            return new LoginResult { Success = false, Error = "Неверный 2FA код" };
                    }
                    else if (guardType == 3 && string.IsNullOrEmpty(sharedSecret))
                    {
                        return new LoginResult { Success = false, Error = "Нужен shared_secret для 2FA" };
                    }
                }

                // Шаг 5: PollAuthSessionStatus
                AppLogger.Info("Polling auth session status...");
                var pollResult = await PollAuthSessionStatusAsync(client, clientId, requestId);
                if (pollResult == null)
                {
                    AppLogger.Error("Не удалось получить токены");
                    return new LoginResult { Success = false, Error = "Не удалось получить токены" };
                }
                AppLogger.Info("Токены получены");

                var pollData = pollResult.Value;
                var accessToken = pollData.accessToken;
                var refreshToken = pollData.refreshToken;

                // Шаг 6: Получаем sessionId с login страницы
                var sessionId = await GetSessionIdAsync(client);
                AppLogger.Info($"SessionId получен: {sessionId}");

                // Шаг 7: FinalizeLogin
                var finalizeResult = await FinalizeLoginAsync(client, refreshToken, steamId, sessionId);
                if (!finalizeResult.success)
                {
                    AppLogger.Error("Не удалось завершить вход (FinalizeLogin)");
                    return new LoginResult { Success = false, Error = "Не удалось завершить вход" };
                }

                var steamLoginSecure = finalizeResult.steamLoginSecure;
                AppLogger.Info($"Авторизация успешна: steamId={steamId}");

                return new LoginResult
                {
                    Success = true,
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    SteamLoginSecure = steamLoginSecure,
                    SessionId = sessionId,
                    SteamId = (long)steamId
                };
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ошибка при авторизации {username}", ex);
                return new LoginResult { Success = false, Error = ex.Message };
            }
        }

        // =====RSA ENCRYPTION =====

        private static async Task<(string publicKeyMod, string publicKeyExp, long timestamp)?> GetPasswordRsaPublicKeyAsync(HttpClient client, string accountName)
        {
            // Steam требует GET запрос для RSA ключа, возвращает JSON
            var url = $"{ApiBase}/IAuthenticationService/GetPasswordRSAPublicKey/v1?account_name={Uri.EscapeDataString(accountName)}";
            AppLogger.Debug($"RSA GET request: {url}");

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "okhttp/3.12.12");
            request.Headers.Add("Accept", "application/json");

            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errContent = await response.Content.ReadAsStringAsync();
                AppLogger.Error($"RSA API error: {response.StatusCode} - {errContent}");
                return null;
            }

            // RSA API возвращает JSON, не Protobuf
            var json = await response.Content.ReadAsStringAsync();
            AppLogger.Debug($"RSA response JSON: {json}");

            try
            {
                var jObj = Newtonsoft.Json.Linq.JObject.Parse(json);
                var responseObj = jObj["response"];
                if (responseObj == null) return null;

                var publicKeyMod = responseObj["publickey_mod"]?.ToString() ?? "";
                var publicKeyExp = responseObj["publickey_exp"]?.ToString() ?? "";
                var timestamp = responseObj["timestamp"]?.Value<long>() ?? 0;

                if (string.IsNullOrEmpty(publicKeyMod))
                {
                    AppLogger.Error("RSA ответ пустой");
                    return null;
                }

                AppLogger.Info($"RSA ключ получен: mod length={publicKeyMod.Length}, exp={publicKeyExp}, timestamp={timestamp}");
                return (publicKeyMod, publicKeyExp, timestamp);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ошибка парсинга RSA ответа: {ex.Message}");
                return null;
            }
        }

        private static byte[] EncodeRsaRequest(string accountName)
        {
            using var ms = new MemoryStream();
            ProtobufHelper.WriteString(ms, 1, accountName);
            return ms.ToArray();
        }

        private static (string publicKeyMod, string publicKeyExp, long timestamp)? DecodeRsaResponse(byte[] data)
        {
            string publicKeyMod = "", publicKeyExp = "";
            long timestamp = 0;

            using var ms = new MemoryStream(data);
            var reader = new BinaryReader(ms);

            while (ms.Position < ms.Length)
            {
                uint tag = (uint)ReadVarint(reader);
                int fieldNumber = (int)(tag >> 3);
                int wireType = (int)(tag & 0x07);

                if (fieldNumber == 1 && wireType == 2) publicKeyMod = ReadString(reader);
                else if (fieldNumber == 2 && wireType == 2) publicKeyExp = ReadString(reader);
                else if (fieldNumber == 3 && wireType == 0) timestamp = (long)ReadVarint(reader);
                else SkipField(reader, wireType);
            }

            return string.IsNullOrEmpty(publicKeyMod) ? null : (publicKeyMod, publicKeyExp, timestamp);
        }

        // ===== BEGIN AUTH SESSION =====

        private static async Task<(ulong clientId, ulong steamId, byte[] requestId, int[] allowedConfirmations, string errorMessage)?> BeginAuthSessionAsync(
            HttpClient client, string accountName, string encryptedPassword, long timestamp)
        {
            var requestBody = EncodeBeginAuthRequest(accountName, encryptedPassword, timestamp);
            var base64Body = Convert.ToBase64String(requestBody);
            AppLogger.Debug($"BeginAuth request bytes: {requestBody.Length} [{BitConverter.ToString(requestBody.Take(50).ToArray()).Replace("-", " ")}...]");

            // Steam API требует FormUrlEncodedContent с полем input_protobuf_encoded
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("input_protobuf_encoded", base64Body)
            });

            var response = await client.PostAsync($"{ApiBase}/IAuthenticationService/BeginAuthSessionViaCredentials/v1", content);

            if (!response.IsSuccessStatusCode)
            {
                var errContent = await response.Content.ReadAsStringAsync();
                AppLogger.Error($"BeginAuth API error: {response.StatusCode} - {errContent}");
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            AppLogger.Debug($"BeginAuth response bytes: {bytes.Length} [{BitConverter.ToString(bytes).Replace("-", " ")}]");
            return DecodeBeginAuthResponse(bytes);
        }

        private static byte[] EncodeBeginAuthRequest(string accountName, string encryptedPassword, long timestamp)
        {
            using var ms = new MemoryStream();

            // field 1: device_friendly_name (string) = "Steam Guard"
            ProtobufHelper.WriteString(ms, 1, "Steam Guard");
            // field 2: account_name (string)
            ProtobufHelper.WriteString(ms, 2, accountName);
            // field 3: encrypted_password (string)
            ProtobufHelper.WriteString(ms, 3, encryptedPassword);
            // field 4: encryption_timestamp (uint64)
            ProtobufHelper.WriteTag(ms, 4, 0);
            ProtobufHelper.WriteVarint(ms, (ulong)timestamp);
            // field 5: remember_login (bool = true)
            ProtobufHelper.WriteTag(ms, 5, 0);
            ProtobufHelper.WriteVarint(ms, 1u);
            // field 6: platform_type (enum = 3 for MobileApp)
            ProtobufHelper.WriteTag(ms, 6, 0);
            ProtobufHelper.WriteVarint(ms, 3u);
            // field 7: persistence (int32 = 1 for Session)
            ProtobufHelper.WriteTag(ms, 7, 0);
            ProtobufHelper.WriteVarint(ms, 1u);
            // field 8: website_id (string = "Mobile")
            ProtobufHelper.WriteString(ms, 8, "Mobile");
            // field 9: device_details (embedded message)
            WriteBeginAuthDeviceDetails(ms, "Pixel 6 Pro", 3, -500, 528);

            return ms.ToArray();
        }

        /// <summary>
        /// Кодируем DeviceDetails (field 9)
        /// </summary>
        private static void WriteBeginAuthDeviceDetails(Stream ms, string deviceName, int platformType, int osType, uint gamingDeviceType)
        {
            using var innerMs = new MemoryStream();

            // field 1: device_friendly_name (string)
            ProtobufHelper.WriteString(innerMs, 1, deviceName);
            // field 2: platform_type (int32 enum)
            ProtobufHelper.WriteTag(innerMs, 2, 0);
            ProtobufHelper.WriteVarint(innerMs, (uint)platformType);
            // field 3: os_type (int32)
            ProtobufHelper.WriteTag(innerMs, 3, 0);
            ProtobufHelper.WriteSInt32(innerMs, osType);
            // field 4: gaming_device_type (uint32)
            ProtobufHelper.WriteTag(innerMs, 4, 0);
            ProtobufHelper.WriteVarint(innerMs, gamingDeviceType);

            var innerBytes = innerMs.ToArray();
            ProtobufHelper.WriteTag(ms, 9, 2);
            ProtobufHelper.WriteVarint(ms, (uint)innerBytes.Length);
            ms.Write(innerBytes, 0, innerBytes.Length);
        }

        private static (ulong clientId, ulong steamId, byte[] requestId, int[] allowedConfirmations, string errorMessage)? DecodeBeginAuthResponse(byte[] data)
        {
            ulong clientId = 0, steamId = 0;
            byte[] requestId = Array.Empty<byte>();
            var confirmations = new List<int>();
            string errorMessage = "";

            using var ms = new MemoryStream(data);
            var reader = new BinaryReader(ms);

            while (ms.Position < ms.Length)
            {
                uint tag = (uint)ReadVarint(reader);
                int fieldNumber = (int)(tag >> 3);
                int wireType = (int)(tag & 0x07);

                if (fieldNumber == 1 && wireType == 0) clientId = ReadVarint(reader);
                else if (fieldNumber == 2 && wireType == 2)
                {
                    int len = (int)ReadVarint(reader);
                    requestId = reader.ReadBytes(len);
                }
                else if (fieldNumber == 5 && wireType == 0) steamId = ReadVarint(reader);
                else if (fieldNumber == 4 && wireType == 2)
                {
                    int len = (int)ReadVarint(reader);
                    var innerData = reader.ReadBytes(len);
                    var confType = DecodeConfirmationType(innerData);
                    if (confType > 0) confirmations.Add(confType);
                }
                else if (fieldNumber == 8 && wireType == 2) // extended_error_message
                {
                    int len = (int)ReadVarint(reader);
                    errorMessage = Encoding.UTF8.GetString(reader.ReadBytes(len));
                }
                else SkipField(reader, wireType);
            }

            if (!string.IsNullOrEmpty(errorMessage))
                AppLogger.Warn($"BeginAuth error message: {errorMessage}");

            AppLogger.Info($"BeginAuth decoded: clientId={clientId}, steamId={steamId}, requestId length={requestId.Length}, confirmations=[{string.Join(",", confirmations)}]");
            return clientId == 0 ? (clientId, steamId, requestId, confirmations.ToArray(), errorMessage) : (clientId, steamId, requestId, confirmations.ToArray(), errorMessage);
        }

        private static int DecodeConfirmationType(byte[] data)
        {
            using var ms = new MemoryStream(data);
            var reader = new BinaryReader(ms);
            while (ms.Position < ms.Length)
            {
                uint tag = (uint)ReadVarint(reader);
                int fieldNumber = (int)(tag >> 3);
                int wireType = (int)(tag & 0x07);
                if (fieldNumber == 1 && wireType == 0) return (int)ReadVarint(reader);  // confirmation_type = field 1
                SkipField(reader, wireType);
            }
            return 0;
        }

        // ===== SEND GUARD CODE =====

        private static async Task<bool> SendGuardCodeAsync(HttpClient client, string code, ulong clientId, ulong steamId, int codeType)
        {
            var requestBody = EncodeGuardCodeRequest(code, clientId, steamId, codeType);
            var base64Body = Convert.ToBase64String(requestBody);
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("input_protobuf_encoded", base64Body)
            });

            AppLogger.Debug($"Sending guard code: type={codeType}, code={code}");
            AppLogger.Debug($"GuardCode debug: clientId={clientId}, steamId={steamId}, base64={base64Body}");
            var response = await client.PostAsync($"{ApiBase}/IAuthenticationService/UpdateAuthSessionWithSteamGuardCode/v1", content);
            
            // Steam API возвращает результат в заголовке X-eresult
            var eResultHeader = response.Headers.Contains("x-eresult") 
                ? response.Headers.GetValues("x-eresult").FirstOrDefault() 
                : null;
            
            if (int.TryParse(eResultHeader, out var eResult) && eResult != 1)
            {
                var rawContent = await response.Content.ReadAsStringAsync();
                AppLogger.Error($"SendGuardCode X-eresult: {eResult} (expected 1=OK), HTTP: {response.StatusCode}, body: {rawContent}");
                return false;
            }
            
            if (!response.IsSuccessStatusCode)
            {
                var errContent = await response.Content.ReadAsStringAsync();
                AppLogger.Error($"SendGuardCode HTTP error: {response.StatusCode} - {errContent}");
                return false;
            }
            
            AppLogger.Info("Guard code отправлен успешно");
            return true;
        }

        private static byte[] EncodeGuardCodeRequest(string code, ulong clientId, ulong steamId, int codeType)
        {
            using var ms = new MemoryStream();
            // field 1: client_id (uint64, varint)
            ProtobufHelper.WriteTag(ms, 1, 0);
            ProtobufHelper.WriteVarint(ms, clientId);
            // field 2: steamid (uint64, FIXED SIZE - wire type 1)
            ProtobufHelper.WriteTag(ms, 2, 1);
            ProtobufHelper.WriteFixed64(ms, steamId);
            // field 3: code (string)
            ProtobufHelper.WriteString(ms, 3, code);
            // field 4: code_type (enum, varint)
            ProtobufHelper.WriteTag(ms, 4, 0);
            ProtobufHelper.WriteVarint(ms, (uint)codeType);
            
            var manualBytes = ms.ToArray();
            
            // Тестовая сериализация через protobuf-net для сравнения
            using var protoMs = new MemoryStream();
            Serializer.Serialize(protoMs, new GuardCodeRequestProto
            {
                ClientId = clientId,
                Steamid = steamId,
                Code = code,
                CodeType = codeType
            });
            var protoBytes = protoMs.ToArray();
            
            AppLogger.Debug($"GuardCode manual ({manualBytes.Length}): [{BitConverter.ToString(manualBytes).Replace("-", " ")}]");
            AppLogger.Debug($"GuardCode protobuf-net ({protoBytes.Length}): [{BitConverter.ToString(protoBytes).Replace("-", " ")}]");
            AppLogger.Debug($"GuardCode manual base64: {Convert.ToBase64String(manualBytes)}");
            AppLogger.Debug($"GuardCode protobuf-net base64: {Convert.ToBase64String(protoBytes)}");
            AppLogger.Debug($"Bytes equal: {manualBytes.SequenceEqual(protoBytes)}");
            
            return manualBytes;
        }

        private static void WriteFixed64(Stream ms, ulong value)
        {
            ms.WriteByte((byte)(value & 0xFF));
            ms.WriteByte((byte)((value >> 8) & 0xFF));
            ms.WriteByte((byte)((value >> 16) & 0xFF));
            ms.WriteByte((byte)((value >> 24) & 0xFF));
            ms.WriteByte((byte)((value >> 32) & 0xFF));
            ms.WriteByte((byte)((value >> 40) & 0xFF));
            ms.WriteByte((byte)((value >> 48) & 0xFF));
            ms.WriteByte((byte)((value >> 56) & 0xFF));
        }

        // ===== POLL AUTH SESSION STATUS =====

        private static async Task<(string accessToken, string refreshToken)?> PollAuthSessionStatusAsync(HttpClient client, ulong clientId, byte[] requestId)
        {
            var requestBody = EncodePollRequest(clientId, requestId);
            var base64Body = Convert.ToBase64String(requestBody);
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("input_protobuf_encoded", base64Body)
            });

            AppLogger.Debug($"Poll request: clientId={clientId}, requestId={BitConverter.ToString(requestId).Replace("-", "")}");

            // Poll up to 15 times with 5 second intervals
            const int maxAttempts = 15;
            const int delayMs = 5000;
            for (int i = 0; i < maxAttempts; i++)
            {
                AppLogger.Debug($"Poll attempt {i+1}/{maxAttempts}...");
                var response = await client.PostAsync($"{ApiBase}/IAuthenticationService/PollAuthSessionStatus/v1", content);

                AppLogger.Debug($"Poll HTTP status: {(int)response.StatusCode} {response.StatusCode}");

                // Логируем заголовки ответа
                var headers = string.Join(", ", response.Headers.Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));
                AppLogger.Debug($"Poll response headers: {headers}");

                // Читаем как строку для диагностики
                var rawContent = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrEmpty(rawContent))
                    AppLogger.Debug($"Poll raw content (first 200): {rawContent.Substring(0, Math.Min(rawContent.Length, 200))}");

                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Error($"Poll error: {response.StatusCode} - {rawContent}");
                    await Task.Delay(delayMs);
                    continue;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync();
                AppLogger.Debug($"Poll response bytes: {bytes.Length}" + (bytes.Length > 0 ? $" [{BitConverter.ToString(bytes.Take(Math.Min(bytes.Length, 50)).ToArray()).Replace("-", " ")}]" : " [EMPTY]"));
                var result = DecodePollResponse(bytes);
                if (!string.IsNullOrEmpty(result.accessToken))
                {
                    AppLogger.Info($"Poll success: accessToken length={result.accessToken.Length}");
                    return result;
                }

                // Проверяем X-eresult — если InvalidState (9), дальше ждать бессмысленно
                if (response.Headers.TryGetValues("X-eresult", out var eResultValues))
                {
                    var eResultStr = eResultValues.FirstOrDefault();
                    if (int.TryParse(eResultStr, out int eResult) && eResult == 9)
                    {
                        AppLogger.Error($"Poll: X-eresult=9 (InvalidState) — сессия недействительна или ожидает другого подтверждения");
                        return null;
                    }
                }

                AppLogger.Debug($"Poll attempt {i+1}: ещё не готово, ждём...");
                await Task.Delay(delayMs);
            }
            AppLogger.Error($"Poll auth session status: все {maxAttempts} попыток провалились");
            return null;
        }

        private static byte[] EncodePollRequest(ulong clientId, byte[] requestId)
        {
            using var ms = new MemoryStream();
            // field 1: client_id (uint64)
            ProtobufHelper.WriteTag(ms, 1, 0);
            ProtobufHelper.WriteVarint(ms, clientId);
            // field 2: request_id (bytes)
            ProtobufHelper.WriteTag(ms, 2, 2);
            ProtobufHelper.WriteVarint(ms, (uint)requestId.Length);
            ms.Write(requestId, 0, requestId.Length);
            return ms.ToArray();
        }

        private static (string accessToken, string refreshToken) DecodePollResponse(byte[] data)
        {
            string accessToken = "", refreshToken = "";
            using var ms = new MemoryStream(data);
            var reader = new BinaryReader(ms);
            while (ms.Position < ms.Length)
            {
                long startPos = ms.Position;
                uint tag = (uint)ReadVarint(reader);
                int fieldNumber = (int)(tag >> 3);
                int wireType = (int)(tag & 0x07);
                
                // PollAuthSessionStatus_Response proto:
                // field 1: NewClientId (uint64, varint)
                // field 2: NewChallengeUrl (string)
                // field 3: RefreshToken (string)
                // field 4: AccessToken (string)
                // field 5: HadRemoteInteraction (bool)
                // field 6: AccountName (string)
                // field 7: NewGuardData (string)
                // field 8: AgreementSessionUrl (string)
                if (fieldNumber == 3 && wireType == 2)
                {
                    refreshToken = ReadString(reader);
                    AppLogger.Debug($"DecodePoll: refreshToken field3, length={refreshToken.Length}");
                }
                else if (fieldNumber == 4 && wireType == 2)
                {
                    accessToken = ReadString(reader);
                    AppLogger.Debug($"DecodePoll: accessToken field4, length={accessToken.Length}");
                }
                else
                {
                    AppLogger.Debug($"DecodePoll: skipping field {fieldNumber}, wireType={wireType}, pos={startPos}");
                    SkipField(reader, wireType);
                }
            }
            return (accessToken, refreshToken);
        }

        // ===== SESSION ID =====

        private static async Task<string> GetSessionIdAsync(HttpClient client)
        {
            try
            {
                var response = await client.GetAsync("https://steamcommunity.com/login/home/?goto=");
                var html = await response.Content.ReadAsStringAsync();
                var match = System.Text.RegularExpressions.Regex.Match(html, @"g_sessionID\s*=\s*""([^""]+)""");
                if (match.Success)
                    return match.Groups[1].Value;
            }
            catch { }
            return GenerateSessionId();
        }

        public static string GenerateSessionId()
        {
            var random = new Random();
            return new string(Enumerable.Range(0, 24).Select(_ => "0123456789abcdef"[random.Next(16)]).ToArray());
        }

        // ===== FINALIZE LOGIN =====

        private static async Task<(bool success, string steamLoginSecure)> FinalizeLoginAsync(HttpClient client, string refreshToken, ulong steamId, string sessionId)
        {
            try
            {
                var data = new Dictionary<string, string>
                {
                    { "nonce", refreshToken },
                    { "sessionid", sessionId }
                };

                var response = await client.PostAsync("https://login.steampowered.com/jwt/finalizelogin", new FormUrlEncodedContent(data));
                var html = await response.Content.ReadAsStringAsync();

                // Parse transfer_info
                var nonceMatch = System.Text.RegularExpressions.Regex.Match(html, @"transfer_info[^{]*{[^}]*""nonce"":""([^""]+)""");
                var authMatch = System.Text.RegularExpressions.Regex.Match(html, @"transfer_info[^{]*{[^}]*""auth"":""([^""]+)""");
                var urlMatch = System.Text.RegularExpressions.Regex.Match(html, @"""url"":""([^""]+)""");

                if (!nonceMatch.Success || !authMatch.Success || !urlMatch.Success)
                    return (false, "");

                var nonce = nonceMatch.Groups[1].Value;
                var auth = authMatch.Groups[1].Value;
                var transferUrl = urlMatch.Groups[1].Value.Replace("\\/", "/");

                // Transfer
                var transferData = new Dictionary<string, string>
                {
                    { "nonce", nonce },
                    { "auth", auth },
                    { "steamID", steamId.ToString() }
                };

                var transferReq = new HttpRequestMessage(HttpMethod.Post, transferUrl);
                transferReq.Content = new FormUrlEncodedContent(transferData);
                transferReq.Headers.Add("Referer", "https://steamcommunity.com");

                var transferResp = await client.SendAsync(transferReq);
                var setCookie = transferResp.Headers.GetValues("Set-Cookie").FirstOrDefault();

                if (string.IsNullOrEmpty(setCookie))
                    return (false, "");

                // Extract steamLoginSecure from cookie
                var cookieMatch = System.Text.RegularExpressions.Regex.Match(setCookie, @"steamLoginSecure=([^;]+)");
                if (cookieMatch.Success)
                {
                    var cookieValue = cookieMatch.Groups[1].Value;
                    // cookieValue уже содержит полный формат: steamId%7C%7Ctoken
                    return (true, cookieValue);
                }

                return (false, "");
            }
            catch
            {
                return (false, "");
            }
        }

        // ===== 2FA CODE GENERATION =====

        private static string Generate2FACode(string sharedSecret, long? steamTime = null)
        {
            try
            {
                // Разэкранирование как в Nebula Auth
                var unescaped = System.Text.RegularExpressions.Regex.Unescape(sharedSecret);
                var secret = Convert.FromBase64String(unescaped);
                
                // Используем серверное время если доступно
                var time = steamTime ?? (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
                time /= 30L;

                AppLogger.Debug($"2FA debug: sharedSecret (first 8 bytes)={BitConverter.ToString(secret.Take(Math.Min(secret.Length, 8)).ToArray())}, timePeriod={time}");

                using var hmac = new System.Security.Cryptography.HMACSHA1(secret);
                var timeBytes = new byte[8];
                long tempTime = time;
                for (int i = 8; i > 0; i--)
                {
                    timeBytes[i - 1] = (byte)tempTime;
                    tempTime >>= 8;
                }

                var hash = hmac.ComputeHash(timeBytes);
                int b = hash[19] & 0xF;
                int codePoint = ((hash[b] & 0x7F) << 24) | ((hash[b + 1] & 0xFF) << 16) |
                               ((hash[b + 2] & 0xFF) << 8) | (hash[b + 3] & 0xFF);

                var chars = "23456789BCDFGHJKMNPQRTVWXY";
                var code = new char[5];
                for (int i = 0; i < 5; i++)
                {
                    code[i] = chars[codePoint % chars.Length];
                    codePoint /= chars.Length;
                }
                return new string(code);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Generate2FACode error: {ex.Message}");
                return "";
            }
        }

        // ===== PROTOBUF HELPERS (используем ProtobufHelper) =====

        private static ulong ReadVarint(BinaryReader reader)
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                byte b = reader.ReadByte();
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
                if (shift >= 64) throw new OverflowException("Varint too long");
            }
            return result;
        }

        private static string ReadString(BinaryReader reader)
        {
            int length = (int)ReadVarint(reader);
            var bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }

        private static void SkipField(BinaryReader reader, int wireType)
        {
            switch (wireType)
            {
                case 0: ReadVarint(reader); break;
                case 1: reader.ReadBytes(8); break;
                case 2: int len = (int)ReadVarint(reader); reader.ReadBytes(len); break;
                case 5: reader.ReadBytes(4); break;
            }
        }
    }
}
