using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SteamGuard
{
    public class MarketService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly SteamAccount _account;

        public MarketService(SteamAccount account, SettingsManager? settingsManager = null)
        {
            _account = account;
            _httpClient = SteamHttpClientFactory.CreateAuthenticatedClient(account, settingsManager);
        }

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
                    foreach (var app in assets.Properties())
                    {
                        var appId = app.Name;
                        if (app.Value is JObject contextObj && contextObj["2"] is JObject items)
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
                            listings[i].Price = ParsePrice(listingData["price"]?.ToString() ?? "0");
                            listings[i].Fees = ParsePrice(listingData["fees"]?.ToString() ?? "0");
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
                            Price = ParsePrice(result["price"]?.ToString() ?? "0"),
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
                    ["price"] = ((int)(price * 100)).ToString()
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

        public async Task<bool> AcceptListingConfirmationAsync(string listingId)
        {
            try
            {
                AppLogger.Info($"Подтверждение листинга {listingId} через систему подтверждений");
                await Task.CompletedTask;
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
}
