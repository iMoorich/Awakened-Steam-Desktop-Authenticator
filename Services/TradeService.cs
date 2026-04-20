using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SteamGuard
{
    public class TradeService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly SteamAccount _account;

        public TradeService(SteamAccount account, SettingsManager? settingsManager = null)
        {
            _account = account;
            _httpClient = SteamHttpClientFactory.CreateAuthenticatedClient(account, settingsManager);
        }

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

        public async Task<List<TradeOffer>> GetActiveTradesAsync()
        {
            var incoming = await GetIncomingTradesAsync();
            var sent = await GetSentTradesAsync();
            return incoming.Concat(sent).ToList();
        }

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

                var success = json["trade_offer_state"]?.Value<int>() == 3;
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

                var success = json["trade_offer_state"]?.Value<int>() == 2;
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

                var success = json["trade_offer_state"]?.Value<int>() == 7;
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

        public async Task<TradeOffer> GetTradeOfferAsync(string tradeOfferId)
        {
            try
            {
                var url = $"https://steamcommunity.com/tradeoffer/{tradeOfferId}/";
                _ = await _httpClient.GetStringAsync(url);
                return new TradeOffer { TradeOfferId = tradeOfferId };
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
}
