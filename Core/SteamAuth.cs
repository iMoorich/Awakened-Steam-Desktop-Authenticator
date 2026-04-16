using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

            // Разэкранирование \+ и других символов
            var unescaped = Regex.Unescape(sharedSecretBase64);
            _sharedSecret = Convert.FromBase64String(unescaped);
        }

        /// <summary>
        /// Получить серверное время Steam
        /// </summary>
        public static async Task<long> GetSteamTimeAsync()
        {
            if (!_timeAligned)
            {
                await AlignTimeAsync();
            }
            return GetUnixTime() + _timeDifference;
        }

        /// <summary>
        /// Синхронизировать время с серверами Steam
        /// </summary>
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
                var serverTime = (long)obj.response.server_time;

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

        /// <summary>
        /// Unix время в секундах
        /// </summary>
        private static long GetUnixTime()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        }

        /// <summary>
        /// Генерация текущего 2FA кода
        /// </summary>
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

        /// <summary>
        /// Получить серверное время синхронно (без ожидания)
        /// </summary>
        private long GetSteamTimeSync()
        {
            if (!_timeAligned)
            {
                AlignTimeSync();
            }
            return GetUnixTime() + _timeDifference;
        }

        /// <summary>
        /// Синхронизировать время синхронно
        /// </summary>
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
                var serverTime = (long)obj.response.server_time;

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

        /// <summary>
        /// Получить несколько кодов (предыдущий, текущий, следующий)
        /// </summary>
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

    /// <summary>
    /// Модель данных аккаунта Steam
    /// </summary>
    public class SteamAccount
    {
        [Newtonsoft.Json.JsonProperty("account_name")]
        public string Username { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonProperty("shared_secret")]
        public string SharedSecret { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonProperty("identity_secret")]
        public string IdentitySecret { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonProperty("device_id")]
        public string DeviceId { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonProperty("revocation_code")]
        public string RevocationCode { get; set; } = string.Empty;

        public string SessionId { get; set; } = string.Empty;
        public string SteamLogin { get; set; } = string.Empty;
        public string SteamLoginSecure { get; set; } = string.Empty;
        public string WebCookie { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonProperty("steamid")]
        public long SteamId { get; set; }

        [Newtonsoft.Json.JsonProperty("AccessToken")]
        public string AccessToken { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonProperty("RefreshToken")]
        public string RefreshToken { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonProperty("Session")]
        public MaFileSession? Session { get; set; }

        [Newtonsoft.Json.JsonProperty("server_time")]
        public long ServerTime { get; set; }

        [Newtonsoft.Json.JsonProperty("serial_number")]
        public string SerialNumber { get; set; } = string.Empty;

        public string Uri { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonProperty("token_gid")]
        public string TokenGid { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonProperty("secret_1")]
        public string Secret1 { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonProperty("Proxy")]
        public MaFileProxy? Proxy { get; set; }

        [Newtonsoft.Json.JsonProperty("Group")]
        public string Group { get; set; } = Constants.DefaultGroupInternal;

        [Newtonsoft.Json.JsonProperty("Password")]
        public string Password { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonProperty("IsFavorite")]
        public bool IsFavorite { get; set; } = false;

        // Дополнительные свойства для UI (не сохраняются в mafile напрямую)
        [Newtonsoft.Json.JsonIgnore]
        public bool HasSession { get; set; } = false;

        public bool AutoTrade { get; set; } = false;

        public bool AutoMarket { get; set; } = false;

        [Newtonsoft.Json.JsonProperty("Balance")]
        public string Balance { get; set; } = string.Empty;
    }

    /// <summary>
    /// Прокси в формате .mafile
    /// </summary>
    public class MaFileProxy
    {
        [Newtonsoft.Json.JsonProperty("Id")]
        public int Id { get; set; }

        [Newtonsoft.Json.JsonProperty("Data")]
        public ProxyData? Data { get; set; }
    }

    /// <summary>
    /// Данные прокси в .mafile
    /// </summary>
    public class ProxyData
    {
        [Newtonsoft.Json.JsonProperty("Protocol")]
        public int Protocol { get; set; }

        [Newtonsoft.Json.JsonProperty("Address")]
        public string? Address { get; set; }

        [Newtonsoft.Json.JsonProperty("Port")]
        public int Port { get; set; }

        [Newtonsoft.Json.JsonProperty("Username")]
        public string? Username { get; set; }

        [Newtonsoft.Json.JsonProperty("Password")]
        public string? Password { get; set; }

        [Newtonsoft.Json.JsonProperty("AuthEnabled")]
        public bool AuthEnabled { get; set; }
    }

    /// <summary>
    /// Сессия в формате .mafile
    /// </summary>
    public class MaFileSession
    {
        [Newtonsoft.Json.JsonProperty("AccessToken")]
        public string AccessToken { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonProperty("steamLoginSecure")]
        public string SteamLoginSecure { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonProperty("RefreshToken")]
        public string RefreshToken { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonProperty("SteamID")]
        public long SteamId { get; set; }

        [Newtonsoft.Json.JsonProperty("SessionID")]
        public string SessionId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Менеджер для управления аккаунтами
    /// </summary>
    public class AccountManager
    {
        private readonly string _mafileDirectory;
        private List<SteamAccount> _accounts = new();

        public List<SteamAccount> Accounts => _accounts;
        public SteamAccount? CurrentAccount { get; private set; }

        public AccountManager(string mafileDirectory = "mafile")
        {
            _mafileDirectory = mafileDirectory;
        }

        /// <summary>
        /// Загрузить все аккаунты из .mafile файлов
        /// </summary>
        public void LoadAccounts()
        {
            _accounts.Clear();

            if (!Directory.Exists(_mafileDirectory))
                return;

            var maFiles = Directory.GetFiles(_mafileDirectory, "*.mafile");
            foreach (var maFile in maFiles)
            {
                try
                {
                    string content = File.ReadAllText(maFile);
                    var account = Newtonsoft.Json.JsonConvert.DeserializeObject<SteamAccount>(content);
                    if (account != null && !string.IsNullOrEmpty(account.Username))
                    {
                        // Конвертируем "none" в "Без группы" для отображения
                        if (account.Group == Constants.DefaultGroupInternal)
                            account.Group = Constants.DefaultGroup;

                        // Если SteamId не указан, берём из Session
                        if (account.SteamId == 0 && account.Session?.SteamId > 0)
                        {
                            account.SteamId = account.Session.SteamId;
                        }

                        AppLogger.Debug($"Loaded account {account.Username}, SteamId: {account.SteamId}");

                        // Восстанавливаем статус сессии из данных аккаунта
                        account.HasSession = !string.IsNullOrEmpty(account.Session?.AccessToken) ||
                                            !string.IsNullOrEmpty(account.SteamLoginSecure);

                        _accounts.Add(account);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Ошибка загрузки maFile: {maFile}", ex);
                }
            }

            // Удаляем дубликаты
            _accounts = _accounts.GroupBy(a => a.Username).Select(g => g.First()).ToList();
        }

        /// <summary>
        /// Сохранить аккаунт в .mafile файл
        /// </summary>
        private void SaveMaFile(SteamAccount account)
        {
            string maFilePath = Path.Combine(_mafileDirectory, $"{account.Username}.mafile");

            AppLogger.Info($"Saving account {account.Username}: AutoTrade={account.AutoTrade}, AutoMarket={account.AutoMarket}");

            // Создаём копию для сохранения (конвертируем "Без группы" → "none")
            var saveAccount = new SteamAccount
            {
                Username = account.Username,
                SharedSecret = account.SharedSecret,
                IdentitySecret = account.IdentitySecret,
                DeviceId = account.DeviceId,
                RevocationCode = account.RevocationCode,
                SessionId = account.Session?.SessionId ?? account.SessionId,
                SteamLogin = account.SteamLogin,
                SteamLoginSecure = account.Session?.SteamLoginSecure ?? account.SteamLoginSecure,
                WebCookie = account.WebCookie,
                SteamId = account.SteamId,
                AccessToken = account.Session?.AccessToken ?? account.AccessToken,
                RefreshToken = account.Session?.RefreshToken ?? account.RefreshToken,
                Session = account.Session,
                ServerTime = account.ServerTime,
                SerialNumber = account.SerialNumber,
                Uri = account.Uri,
                TokenGid = account.TokenGid,
                Secret1 = account.Secret1,
                Proxy = account.Proxy,
                Group = account.Group == Constants.DefaultGroup ? Constants.DefaultGroupInternal : account.Group,
                Password = account.Password,
                IsFavorite = account.IsFavorite,
                AutoTrade = account.AutoTrade,
                AutoMarket = account.AutoMarket,
                Balance = account.Balance
            };

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(saveAccount, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(maFilePath, json);
            AppLogger.Info($"Account {account.Username} saved to {maFilePath}");
        }

        /// <summary>
        /// Сохранить настройки аккаунта в .mafile
        /// </summary>
        public void SaveAccountSettings(SteamAccount account)
        {
            SaveMaFile(account);
        }

        public void AddAccount(SteamAccount account)
        {
            if (!_accounts.Any(a => a.Username == account.Username))
            {
                SaveMaFile(account);
                _accounts.Add(account);
            }
        }

        public void RemoveAccount(SteamAccount account)
        {
            _accounts.Remove(account);

            // Удаляем .mafile файл
            string maFilePath = Path.Combine(_mafileDirectory, $"{account.Username}.mafile");
            if (File.Exists(maFilePath))
            {
                File.Delete(maFilePath);
            }
        }

        public void SetCurrentAccount(SteamAccount account)
        {
            CurrentAccount = account;
        }

        public void SaveAccounts()
        {
            foreach (var account in _accounts)
            {
                SaveMaFile(account);
            }
        }

        /// <summary>
        /// Получить все уникальные группы из аккаунтов
        /// </summary>
        public List<string> GetGroups()
        {
            return _accounts
                .Select(a => a.Group ?? "Без группы")
                .Distinct()
                .OrderBy(g => g)
                .ToList();
        }
    }
}
