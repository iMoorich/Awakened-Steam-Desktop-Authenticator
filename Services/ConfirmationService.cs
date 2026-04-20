using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SteamGuard
{
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

        public async Task<bool> AcceptConfirmationAsync(Confirmation confirmation)
        {
            return await RespondToConfirmationAsync(confirmation, true);
        }

        public async Task<bool> DenyConfirmationAsync(Confirmation confirmation)
        {
            return await RespondToConfirmationAsync(confirmation, false);
        }

        public async Task<bool> AcceptAllConfirmationsAsync()
        {
            var confirmations = await GetConfirmationsAsync();
            if (confirmations.Count == 0)
                return true;

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
            var queryParams = GenerateConfirmationQueryParams(action, out _, out _);
            string url = $"/mobileconf/ajaxop?op={action}&{queryParams}&cid={confirmation.Id}&ck={confirmation.Nonce}";

            AppLogger.Debug($"{action} confirmation: {url}");

            var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();
            AppLogger.Debug($"Response: {json}");
            var data = JObject.Parse(json);

            return data["success"]?.Value<bool>() ?? false;
        }

        public async Task<string> GetTradeDetailsAsync(string tradeId)
        {
            var queryParams = GenerateConfirmationQueryParams("details", out _, out _);
            string url = $"/mobileconf/details/{tradeId}?{queryParams}";

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        private string GenerateConfirmationQueryParams(string action, out string signature, out long time)
        {
            time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            byte[] identityBytes;
            try
            {
                identityBytes = Convert.FromBase64String(_account.IdentitySecret);
            }
            catch
            {
                identityBytes = Encoding.UTF8.GetBytes(_account.IdentitySecret);
            }

            int tagLength = action.Length > 32 ? 32 : action.Length;
            var dataToSign = new byte[8 + tagLength];

            long timeValue = time;
            for (int i = 7; i >= 0; i--)
            {
                dataToSign[i] = (byte)timeValue;
                timeValue >>= 8;
            }

            Array.Copy(Encoding.UTF8.GetBytes(action), 0, dataToSign, 8, tagLength);

            using var hmac = new System.Security.Cryptography.HMACSHA1(identityBytes);
            var hash = hmac.ComputeHash(dataToSign);
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
}
