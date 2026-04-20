using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SteamGuard
{
    /// <summary>
    /// Класс для генерации 2FA кодов Steam
    /// </summary>
    public class SteamAuthenticator
    {
        private static readonly byte[] SteamGuardCodeTranslations = Encoding.UTF8.GetBytes(Constants.SteamGuardAlphabet);

        private static bool _timeAligned;
        private static int _timeDifference;

        private readonly byte[] _sharedSecret;

        public SteamAuthenticator(byte[] sharedSecret)
        {
            _sharedSecret = sharedSecret ?? throw new ArgumentNullException(nameof(sharedSecret));
        }

        public SteamAuthenticator(string sharedSecretBase64)
        {
            if (string.IsNullOrEmpty(sharedSecretBase64))
                throw new ArgumentNullException(nameof(sharedSecretBase64));

            var unescaped = Regex.Unescape(sharedSecretBase64);
            _sharedSecret = Convert.FromBase64String(unescaped);
        }

        public static async Task<long> GetSteamTimeAsync()
        {
            if (!_timeAligned)
            {
                await AlignTimeAsync();
            }

            return GetUnixTime() + _timeDifference;
        }

        public static async Task AlignTimeAsync()
        {
            using var client = SteamHttpClientFactory.GetSharedClient();
            var sw = Stopwatch.StartNew();

            try
            {
                var response = await client.PostAsync(Constants.TimeAlignEndpoint, null);
                sw.Stop();

                var json = await response.Content.ReadAsStringAsync();
                dynamic? obj = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
                var serverTime = (long)(obj?.response?.server_time ?? 0);

                var now = GetUnixTime() - sw.ElapsedMilliseconds / 1000;
                _timeDifference = (int)(serverTime - now);
                _timeAligned = true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Ошибка синхронизации времени с Steam", ex);
                _timeAligned = true;
                _timeDifference = 0;
            }
        }

        private static long GetUnixTime()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        }

        public string GenerateCode(long? time = null)
        {
            var steamTime = time ?? GetSteamTimeSync();
            long timePeriod = steamTime / 30L;

            byte[] timeBytes = new byte[8];
            for (int i = 8; i > 0; i--)
            {
                timeBytes[i - 1] = (byte)timePeriod;
                timePeriod >>= 8;
            }

            using var hmac = new HMACSHA1(_sharedSecret);
            var hash = hmac.ComputeHash(timeBytes);

            int b = hash[19] & 0xF;
            int codePoint = ((hash[b] & 0x7F) << 24) |
                           ((hash[b + 1] & 0xFF) << 16) |
                           ((hash[b + 2] & 0xFF) << 8) |
                           (hash[b + 3] & 0xFF);

            var code = new char[5];
            for (int i = 0; i < 5; i++)
            {
                code[i] = (char)SteamGuardCodeTranslations[codePoint % SteamGuardCodeTranslations.Length];
                codePoint /= SteamGuardCodeTranslations.Length;
            }

            return new string(code);
        }

        private long GetSteamTimeSync()
        {
            if (!_timeAligned)
            {
                AlignTimeSync();
            }

            return GetUnixTime() + _timeDifference;
        }

        private void AlignTimeSync()
        {
            try
            {
                using var client = SteamHttpClientFactory.GetSharedClient();
                var sw = Stopwatch.StartNew();

                var response = client.PostAsync(Constants.TimeAlignEndpoint, null).GetAwaiter().GetResult();
                sw.Stop();

                var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                dynamic? obj = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
                var serverTime = (long)(obj?.response?.server_time ?? 0);

                var now = GetUnixTime() - sw.ElapsedMilliseconds / 1000;
                _timeDifference = (int)(serverTime - now);
                _timeAligned = true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Ошибка синхронной синхронизации времени с Steam", ex);
                _timeAligned = true;
                _timeDifference = 0;
            }
        }

        public (string Previous, string Current, string Next) GetCodes()
        {
            var now = GetSteamTimeSync();
            return (
                GenerateCode(now - 30),
                GenerateCode(now),
                GenerateCode(now + 30)
            );
        }
    }
}
