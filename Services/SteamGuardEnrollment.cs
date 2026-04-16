using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProtoBuf;

namespace SteamGuard;

/// <summary>
/// Полный Steam Guard Enrollment flow идентичный NebulaAuth
/// </summary>
public class SteamGuardEnrollment : IDisposable
{
    private readonly HttpClient _http;
    private readonly HttpClientHandler _handler;
    private readonly CookieContainer _cookies;
    private bool _disposed;
    private string? _sessionId;
    private ulong _steamId = 0;
    private string _sharedSecret = "";
    private string _deviceId = $"android:{Guid.NewGuid()}";
    private string _accessToken = "";
    private string _refreshToken = "";
    private string _steamLoginSecure = "";
    private ulong _clientId = 0;
    private byte[] _requestId = Array.Empty<byte>();
    private int _confirmationType = 0;
    private readonly string _logFile;
    private AddAuthenticatorResult? _addAuthResult;
    private SteamSession? _session;

    public string DeviceId => _deviceId;
    public ulong SteamId => _steamId;
    public SteamSession? Session => _session;

    public SteamGuardEnrollment()
    {
        _handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _cookies = _handler.CookieContainer;
        _http = new HttpClient(_handler, disposeHandler: false);

        _http.DefaultRequestHeaders.Add("User-Agent", Constants.MobileUserAgent);
        _http.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, text/html, application/xml, text/xml, */*");
        _http.DefaultRequestHeaders.Add("Accept-Language", "en-US");
        _http.DefaultRequestHeaders.Referrer = new Uri(Constants.CommunityUrl);
        _http.DefaultRequestHeaders.Add("Origin", Constants.CommunityUrl);
        _http.Timeout = TimeSpan.FromSeconds(50);

        // Настройка файла логов
        var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);
        _logFile = Path.Combine(logDir, "steam_guard_enrollment.log");
        try { File.WriteAllText(_logFile, $"=== SteamGuardEnrollment лог [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===\n"); }
        catch { }
    }

    private void Log(string message)
    {
        Console.WriteLine($"[SGE] {message}");
        try { File.AppendAllText(_logFile, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}"); } catch { }
    }

    private void LogError(string message, Exception? ex = null)
    {
        var fullMsg = $"❌ {message}";
        if (ex != null) fullMsg += $"\n   Исключение: {ex.GetType().Name}: {ex.Message}";
        Console.WriteLine($"[SGE] {fullMsg}");
        try { File.AppendAllText(_logFile, $"{DateTime.Now:HH:mm:ss.fff} {fullMsg}{Environment.NewLine}"); } catch { }
    }

    private void LogRaw(string message)
    {
        try { File.AppendAllText(_logFile, message + Environment.NewLine); } catch { }
    }

    #region Helpers

    private static string SerializeProto<T>(T obj)
    {
        using var ms = new MemoryStream();
        ProtoBuf.Serializer.Serialize(ms, obj);
        return Convert.ToBase64String(ms.ToArray());
    }

    private static T DeserializeProto<T>(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        return ProtoBuf.Serializer.Deserialize<T>(ms);
    }

    private async Task<HttpResponseMessage> SendProtoPostAsync(string url, string protoBase64)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("input_protobuf_encoded", protoBase64)
        });
        return await _http.PostAsync(url, content);
    }

    private async Task<(string Json, byte[]? ProtoBytes)> ReadResponseAsync(HttpResponseMessage resp)
    {
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        try
        {
            var json = Encoding.UTF8.GetString(bytes);
            JsonDocument.Parse(json);
            return (json, null);
        }
        catch
        {
            return ("", bytes);
        }
    }

    #endregion

    private async Task<string?> GetSessionIdAsync()
    {
        try
        {
            var html = await _http.GetStringAsync("https://steamcommunity.com/login/home");
            var match = Regex.Match(html, @"g_sessionID\s*=\s*""([^""]+)""");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch { return null; }
    }

    private void SetupMobileCookies()
    {
        var uri = new Uri(Constants.CommunityUrl);
        _cookies.Add(uri, new Cookie("mobileClientVersion", Constants.MobileClientVersion));
        _cookies.Add(uri, new Cookie("mobileClient", Constants.MobileClient));
        _cookies.Add(uri, new Cookie("Steam_Language", Constants.MobileLanguage));
    }

    /// <summary>
    /// Шаг 1: Начало логина — возвращает результат и нужно ли подтверждение
    /// </summary>
    public async Task<(bool Success, string Error, bool NeedsEmailCode, int ConfirmationType)> StartLoginAsync(string login, string password)
    {
        Log($"=== StartLoginAsync (login={login}) ===");
        try
        {
            SetupMobileCookies();
            _sessionId = await GetSessionIdAsync();
            Log($"Session ID: {_sessionId ?? "null"}");

            // RSA Key
            var rsaUrl = $"https://api.steampowered.com/IAuthenticationService/GetPasswordRSAPublicKey/v1?account_name={Uri.EscapeDataString(login)}";
            var rsaJson = await _http.GetStringAsync(rsaUrl);
            Log($"RSA ответ: {rsaJson}");

            var rsaDoc = JsonDocument.Parse(rsaJson);
            if (!rsaDoc.RootElement.TryGetProperty("response", out var rsaResp))
                return (false, "Ошибка получения RSA ключа", false, 0);

            var pubMod = rsaResp.GetStringSafe("publickey_mod") ?? "";
            var pubExp = rsaResp.GetStringSafe("publickey_exp") ?? "";
            var timestamp = rsaResp.GetUlongOrStringSafe("timestamp");

            Log($"RSA: mod_len={pubMod.Length}, timestamp={timestamp}");
            if (string.IsNullOrEmpty(pubMod))
                return (false, "Невалидный RSA ключ", false, 0);

            var encPassword = CryptoHelper.EncryptPasswordRsa(password, pubMod, pubExp);

            // BeginAuthSession
            var beginRequest = new BeginAuthSessionViaCredentials_Request
            {
                AccountName = login,
                EncryptedPassword = encPassword,
                EncryptionTimestamp = timestamp,
                RememberLogin = true,
                PlatformType = Constants.DevicePlatformType,
                Persistence = 1,
                WebsiteId = "Mobile",
                DeviceDetails = DeviceDetailsProto.CreateMobileDetails()
            };

            var beginProto = SerializeProto(beginRequest);
            var beginResp = await SendProtoPostAsync("https://api.steampowered.com/IAuthenticationService/BeginAuthSessionViaCredentials/v1", beginProto);
            var (beginJson, beginProtoBytes) = await ReadResponseAsync(beginResp);

            Log($"BeginAuth HTTP: {beginResp.StatusCode}, ProtoBuf bytes: {beginProtoBytes?.Length ?? 0}");

            if (beginProtoBytes != null && beginProtoBytes.Length > 0)
            {
                var br = DeserializeProto<BeginAuthSessionViaCredentials_Response>(beginProtoBytes);
                _clientId = br.ClientId;
                _requestId = br.RequestId;
                _steamId = br.Steamid;

                Log($"BeginAuth ProtoBuf: ClientId={_clientId}, RequestId_len={_requestId.Length}, SteamId={_steamId}");
                Log($"AllowedConfirmations count: {br.AllowedConfirmations.Count}");
                foreach (var c in br.AllowedConfirmations)
                    Log($"  ConfirmationType={c.ConfirmationType}, AssociatedMessage={c.AssociatedMessage}");

                if (!string.IsNullOrEmpty(br.ExtendedErrorMessage))
                {
                    if (br.ExtendedErrorMessage.Contains("guard", StringComparison.OrdinalIgnoreCase))
                        return (false, "На аккаунте уже активен Steam Guard.", false, 0);
                    return (false, $"Steam ошибка: {br.ExtendedErrorMessage}", false, 0);
                }

                if (_clientId == 0 || _requestId.Length == 0)
                    return (false, $"Steam вернул пустой ответ.{(_steamId > 0 ? " Возможно активен Steam Guard." : "")}", false, 0);

                // Проверяем нужно ли подтверждение
                if (br.AllowedConfirmations.Count > 0 && br.AllowedConfirmations.All(a => a.ConfirmationType != (int)AuthConfirmationType.None))
                {
                    // Первый тип подтверждения — это то что Steam хочет
                    int confirmType = br.AllowedConfirmations[0].ConfirmationType;
                    string confirmMsg = br.AllowedConfirmations[0].AssociatedMessage;
                    Log($"Требуется подтверждение: Type={confirmType} ({(AuthConfirmationType)confirmType}), Message={confirmMsg}");

                    // EmailCode = 2, EmailConfirmation = 5
                    bool isEmailConfirm = confirmType == (int)AuthConfirmationType.EmailCode ||
                                          confirmType == (int)AuthConfirmationType.EmailConfirmation ||
                                          confirmMsg.Contains("@");

                    // Также проверяем все подтверждения на наличие email
                    if (!isEmailConfirm)
                    {
                        foreach (var c in br.AllowedConfirmations)
                        {
                            if (c.ConfirmationType == (int)AuthConfirmationType.EmailCode ||
                                c.ConfirmationType == (int)AuthConfirmationType.EmailConfirmation ||
                                c.AssociatedMessage.Contains("@"))
                            {
                                isEmailConfirm = true;
                                confirmType = c.ConfirmationType;
                                confirmMsg = c.AssociatedMessage;
                                Log($"Найдено email-подтверждение: Type={confirmType}, Message={confirmMsg}");
                                break;
                            }
                        }
                    }

                    if (isEmailConfirm)
                    {
                        Log($"Требуется Email код на {confirmMsg}. Email автоматически отправлен Steam после BeginAuthSession.");
                        _confirmationType = confirmType;
                        return (true, "", true, confirmType);
                    }

                    // Type 6 = MachineToken — это тоже можно попробовать как email
                    if (confirmType == (int)AuthConfirmationType.MachineToken)
                    {
                        Log($"Требуется MachineToken (Type=6), пробуем как email");
                        _confirmationType = confirmType;
                        return (true, "", true, confirmType);
                    }

                    // DeviceCode = 3 — это 2FA из мобильного приложения
                    // Вместо ошибки — возвращаем что нужен 2FA код, и генерируем его сами
                    if (confirmType == (int)AuthConfirmationType.DeviceCode)
                    {
                        Log($"Требуется 2FA код (Type=3) — генерируем автоматически");
                        _confirmationType = confirmType;
                        // Возвращаем что нужен 2FA код (не email)
                        return (true, "", false, confirmType);
                    }

                    // Если нет email — значит это реальный 2FA
                    Log($"Требуется подтверждение (Type={confirmType})");
                    return (false, $"Аккаунт требует подтверждение типа {(AuthConfirmationType)confirmType}.", false, confirmType);
                }

                // Подтверждение не нужно — сразу polling
                Log("Подтверждение не требуется, начинаем polling...");
                var pollResult = await CompletePollingAsync();
                return (pollResult.Success, pollResult.Error, false, 0);
            }

            return (false, "Steam вернул неизвестный формат ответа", false, 0);
        }
        catch (Exception ex)
        {
            LogError($"StartLoginAsync EXCEPTION", ex);
            return (false, ex.Message, false, 0);
        }
    }

    /// <summary>
    /// Проверяет, подтверждён ли email после отправки кода (NebulaAuth: CheckEmailConfirmation)
    /// </summary>
    public async Task<(bool Confirmed, int SecondsToWait)> CheckEmailConfirmationAsync()
    {
        try
        {
            var url = $"https://api.steampowered.com/ITwoFactorService/IsAccountWaitingForEmailConfirmation/v1?access_token={Uri.EscapeDataString(_accessToken)}";
            var req = new EmptyMessage();
            var proto = SerializeProto(req);
            var resp = await SendProtoPostAsync(url, proto);
            var (json, protoBytes) = await ReadResponseAsync(resp);

            if (protoBytes != null && protoBytes.Length > 0)
            {
                var r = DeserializeProto<IsAccountWaitingForEmailConfirmation_Response>(protoBytes);
                Log($"CheckEmailConfirmation: IsWaiting={r.IsWaiting}, SecondsToWait={r.SecondsToWait}");
                return (!r.IsWaiting, r.SecondsToWait);
            }
        }
        catch (Exception ex)
        {
            LogError($"CheckEmailConfirmationAsync EXCEPTION", ex);
        }
        return (false, 0);
    }

    private static string GetEResultMessage(int eresult) => EResult.GetMessage(eresult);

    /// <summary>
    /// Шаг 2: Отправка email-кода и завершение логина
    /// NebulaAuth: retry до 3 раз при InvalidLoginAuthCode (eresult=65)
    /// </summary>
    public async Task<(bool Success, string Error)> SubmitEmailCodeAsync(string emailCode)
    {
        Log($"=== SubmitEmailCodeAsync (code_len={emailCode?.Length ?? 0}) ===");

        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            Log($"Попытка отправки email-кода {attempt}/{maxRetries}");
            try
            {
                // NebulaAuth использует EAuthSessionGuardType.EmailCode = 2
                var updateRequest = new UpdateAuthSessionWithSteamGuardCode_Request
                {
                    ClientId = _clientId,
                    Steamid = _steamId,
                    Code = emailCode ?? "",
                    CodeType = (int)AuthCodeType.EmailCode
                };

                var updateProto = SerializeProto(updateRequest);
                Log($"UpdateAuthSession proto (first 80): {updateProto.Substring(0, Math.Min(80, updateProto.Length))}");

                var updateResp = await SendProtoPostAsync("https://api.steampowered.com/IAuthenticationService/UpdateAuthSessionWithSteamGuardCode/v1", updateProto);
                var (updateJson, updateProtoBytes) = await ReadResponseAsync(updateResp);

                Log($"UpdateAuthSession HTTP: {updateResp.StatusCode}, ProtoBuf: {updateProtoBytes?.Length ?? 0} bytes, JSON: {(string.IsNullOrEmpty(updateJson) ? "нет" : updateJson.Substring(0, Math.Min(200, updateJson.Length)))}");

                // Проверяем ответ на ошибки (eresult != 1 = OK)
                bool hasError = false;
                string errorMsg = "";

                if (!string.IsNullOrEmpty(updateJson))
                {
                    try
                    {
                        var doc = JsonDocument.Parse(updateJson);
                        // eresult на верхнем уровне
                        if (doc.RootElement.TryGetProperty("eresult", out var eresult))
                        {
                            int result = eresult.TryGetInt32(out var r) ? r : 0;
                            if (result != 1)  // 1 = OK
                            {
                                hasError = true;
                                errorMsg = GetEResultMessage(result);
                                Log($"UpdateAuthSession eresult={result} ({errorMsg})");
                            }
                        }
                        // response.eresult
                        else if (doc.RootElement.TryGetProperty("response", out var resp))
                        {
                            if (resp.TryGetProperty("eresult", out var eresult2) && eresult2.TryGetInt32(out var r2) && r2 != 1)
                            {
                                hasError = true;
                                errorMsg = GetEResultMessage(r2);
                                Log($"UpdateAuthSession response.eresult={r2} ({errorMsg})");
                            }
                        }
                    }
                    catch { /* Не JSON, продолжаем */ }
                }

                if (hasError)
                {
                    if (attempt < maxRetries)
                    {
                        Log($"eresult ошибка, повторная попытка через 2с...");
                        await Task.Delay(2000);
                        continue;
                    }
                    return (false, errorMsg);
                }

                var pollResult = await CompletePollingAsync();
                return (pollResult.Success, pollResult.Error);
            }
            catch (Exception ex)
            {
                LogError($"SubmitEmailCodeAsync attempt {attempt} EXCEPTION", ex);
                if (attempt >= maxRetries)
                    return (false, ex.Message);
                await Task.Delay(2000);
            }
        }

        return (false, "Превышено количество попыток отправки email-кода");
    }

    /// <summary>
    /// Шаг 2 (альтернатива): Отправка 2FA кода (DeviceCode) и завершение логина
    /// NebulaAuth: CodeType=3 (DeviceCode) для аккаунтов с уже активным Steam Guard
    /// </summary>
    public async Task<(bool Success, string Error)> Submit2FACodeAsync(string sharedSecret)
    {
        Log($"=== Submit2FACodeAsync ===");

        // Генерируем 2FA код из SharedSecret
        var twoFACode = new SteamAuthenticator(sharedSecret).GenerateCode();
        Log($"Сгенерирован 2FA код: {twoFACode}");

        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            Log($"Попытка отправки 2FA кода {attempt}/{maxRetries}");
            try
            {
                // CodeType = DeviceCode для аккаунтов с уже активным Steam Guard
                var updateRequest = new UpdateAuthSessionWithSteamGuardCode_Request
                {
                    ClientId = _clientId,
                    Steamid = _steamId,
                    Code = twoFACode,
                    CodeType = (int)AuthCodeType.DeviceCode
                };

                var updateProto = SerializeProto(updateRequest);
                var updateResp = await SendProtoPostAsync("https://api.steampowered.com/IAuthenticationService/UpdateAuthSessionWithSteamGuardCode/v1", updateProto);
                var (updateJson, updateProtoBytes) = await ReadResponseAsync(updateResp);

                Log($"UpdateAuthSession HTTP: {updateResp.StatusCode}");

                // Проверяем ответ на ошибки
                bool hasError = false;
                string errorMsg = "";

                if (!string.IsNullOrEmpty(updateJson))
                {
                    try
                    {
                        var doc = JsonDocument.Parse(updateJson);
                        if (doc.RootElement.TryGetProperty("eresult", out var eresult))
                        {
                            int result = eresult.TryGetInt32(out var r) ? r : 0;
                            if (result != 1)
                            {
                                hasError = true;
                                errorMsg = GetEResultMessage(result);
                                Log($"UpdateAuthSession eresult={result} ({errorMsg})");
                            }
                        }
                    }
                    catch { /* Не JSON */ }
                }

                if (hasError)
                {
                    if (attempt < maxRetries)
                    {
                        // Если код неверный — генерируем новый (30 сек интервал)
                        await Task.Delay(1000);
                        twoFACode = new SteamAuthenticator(sharedSecret).GenerateCode();
                        Log($"Новый 2FA код: {twoFACode}");
                        continue;
                    }
                    return (false, errorMsg);
                }

                var pollResult = await CompletePollingAsync();
                return (pollResult.Success, pollResult.Error);
            }
            catch (Exception ex)
            {
                LogError($"Submit2FACodeAsync attempt {attempt} EXCEPTION", ex);
                if (attempt >= maxRetries)
                    return (false, ex.Message);
                await Task.Delay(2000);
            }
        }

        return (false, "Превышено количество попыток отправки 2FA кода");
    }

    /// <summary>
    /// Polling до получения access token
    /// </summary>
    private async Task<(bool Success, string Error)> CompletePollingAsync()
    {
        Log("Начинаю polling...");
        string accessToken = "";
        string refreshToken = "";
        var pollUrl = "https://api.steampowered.com/IAuthenticationService/PollAuthSessionStatus/v1";

        for (int i = 0; i < Constants.MaxPollAttempts; i++)
        {
            await Task.Delay(2000);

            var pollRequest = new PollAuthSessionStatus_Request
            {
                ClientId = _clientId,
                RequestId = _requestId
            };

            var pollProto = SerializeProto(pollRequest);
            var pollResp = await SendProtoPostAsync(pollUrl, pollProto);
            var (pollJson, pollProtoBytes) = await ReadResponseAsync(pollResp);

            if (pollProtoBytes != null && pollProtoBytes.Length > 0)
            {
                try
                {
                    var pr = DeserializeProto<PollAuthSessionStatus_Response>(pollProtoBytes);
                    Log($"Poll {i + 1}: AccessToken={!string.IsNullOrEmpty(pr.AccessToken)}");

                    if (!string.IsNullOrEmpty(pr.AccessToken))
                    {
                        accessToken = pr.AccessToken;
                        refreshToken = pr.RefreshToken ?? "";
                        if (_steamId == 0) _steamId = pr.NewClientId;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Poll ProtoBuf ошибка", ex);
                }
            }
        }

        if (string.IsNullOrEmpty(accessToken))
        {
            LogError("Polling timeout");
            return (false, "login_timeout");
        }

        _accessToken = accessToken;
        _refreshToken = refreshToken;
        Log($"✓ AccessToken получен: {accessToken.Substring(0, Math.Min(50, accessToken.Length))}...");

        // JWT Finalize Login
        if (!string.IsNullOrEmpty(refreshToken) && !string.IsNullOrEmpty(_sessionId))
        {
            Log("JWT Finalize Login...");
            try
            {
                var finalizeData = new Dictionary<string, string> { { "nonce", refreshToken }, { "sessionid", _sessionId } };
                var finalizeResp = await _http.PostAsync("https://login.steampowered.com/jwt/finalizelogin", new FormUrlEncodedContent(finalizeData));
                var finalizeJson = await finalizeResp.Content.ReadAsStringAsync();
                Log($"Finalize: {finalizeJson.Substring(0, Math.Min(300, finalizeJson.Length))}");

                // Парсим transfer_info для получения токенов по доменам
                try
                {
                    var finalizeDoc = JsonDocument.Parse(finalizeJson);
                    if (finalizeDoc.RootElement.TryGetProperty("transfer_info", out var transferInfo))
                    {
                        foreach (var transfer in transferInfo.EnumerateArray())
                        {
                            var url = transfer.GetStringSafe("url");
                            var token = transfer.GetStringSafe("token");
                            Log($"  Transfer: {url} token_len={token?.Length ?? 0}");
                        }
                    }
                }
                catch { /* Не JSON — игнорируем */ }

                // Создаём SteamLoginSecure cookie: {SteamId}%7C%7C{AccessToken}
                _steamLoginSecure = $"{_steamId}%7C%7C{accessToken}";

                // Сохраняем сессию
                _session = new SteamSession
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    SteamLoginSecure = _steamLoginSecure,
                    SteamID = _steamId,
                    SessionID = _sessionId,
                    CreatedAt = DateTime.UtcNow
                };
                Log($"✓ Session сохранена: AccessToken={accessToken.Substring(0, Math.Min(30, accessToken.Length))}...");
            }
            catch (Exception ex) { LogError("Finalize ошибка", ex); }
        }

        return (true, "");
    }

    /// <summary>
    /// Полный login flow (обёртка для совместимости)
    /// </summary>
    public async Task<(bool Success, string Error, string MobileAccessToken, ulong SteamId)> LoginAsync(string login, string password)
    {
        var result = await StartLoginAsync(login, password);
        if (!result.Success)
            return (false, result.Error, "", _steamId);

        // Если нужно подтверждение — это обрабатывается отдельно через SubmitEmailCodeAsync
        if (result.NeedsEmailCode)
            return (false, "needs_email_confirmation", "", _steamId);

        return (true, "", _accessToken, _steamId);
    }

    /// <summary>
    /// Обновить Access Token через RefreshToken (без пароля)
    /// NebulaAuth: IAuthenticationService/GenerateAccessTokenForApp/v1
    /// </summary>
    public async Task<(bool Success, string Error, string AccessToken)> RefreshSessionAsync(string refreshToken, ulong steamId)
    {
        Log($"=== RefreshSessionAsync (SteamId={steamId}) ===");
        try
        {
            var req = new GenerateAccessTokenForApp_Request
            {
                RefreshToken = refreshToken,
                SteamId = steamId,
                TokenRenewalType = true
            };

            var proto = SerializeProto(req);
            var resp = await SendProtoPostAsync("https://api.steampowered.com/IAuthenticationService/GenerateAccessTokenForApp/v1", proto);
            var (json, protoBytes) = await ReadResponseAsync(resp);

            Log($"RefreshToken HTTP: {resp.StatusCode}");

            string newAccessToken = "";

            if (protoBytes != null && protoBytes.Length > 0)
            {
                var r = DeserializeProto<GenerateAccessTokenForApp_Response>(protoBytes);
                newAccessToken = r.AccessToken ?? "";
                Log($"RefreshToken ProtoBuf: AccessToken={!string.IsNullOrEmpty(newAccessToken)}");
            }
            else if (!string.IsNullOrEmpty(json))
            {
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("response", out var respEl))
                    newAccessToken = respEl.GetStringSafe("access_token") ?? "";
                Log($"RefreshToken JSON: AccessToken={!string.IsNullOrEmpty(newAccessToken)}");
            }

            if (string.IsNullOrEmpty(newAccessToken))
                return (false, "Не удалось обновить токен", "");

            _accessToken = newAccessToken;
            _steamId = steamId;
            _steamLoginSecure = $"{steamId}%7C%7C{newAccessToken}";

            _session = new SteamSession
            {
                AccessToken = newAccessToken,
                RefreshToken = refreshToken,
                SteamLoginSecure = _steamLoginSecure,
                SteamID = steamId,
                SessionID = _sessionId ?? "",
                CreatedAt = DateTime.UtcNow
            };

            return (true, "", newAccessToken);
        }
        catch (Exception ex)
        {
            LogError($"RefreshSessionAsync EXCEPTION", ex);
            return (false, ex.Message, "");
        }
    }

    /// <summary>
    /// AddAuthenticator
    /// </summary>
    public async Task<(bool Success, string Error, string? SharedSecret, string? RevocationCode, string? Uri, long ServerTime, string? TokenGid, byte[]? IdentitySecret, byte[]? Secret1, int ConfirmType)> AddAuthenticatorAsync()
    {
        Log($"=== AddAuthenticatorAsync (SteamId={_steamId}) ===");
        try
        {
            var url = $"https://api.steampowered.com/ITwoFactorService/AddAuthenticator/v1?access_token={Uri.EscapeDataString(_accessToken)}";

            var req = new AddAuthenticator_Request
            {
                SteamId = _steamId,
                AuthenticatorType = 1,
                DeviceIdentifier = _deviceId,
                Version = 2
            };

            var proto = SerializeProto(req);
            var resp = await SendProtoPostAsync(url, proto);
            var (json, protoBytes) = await ReadResponseAsync(resp);

            Log($"AddAuth HTTP: {resp.StatusCode}");
            if (!string.IsNullOrEmpty(json)) Log($"AddAuth JSON: {json}");
            else Log($"AddAuth ProtoBuf: {protoBytes?.Length ?? 0} bytes");

            if (protoBytes != null && protoBytes.Length > 0)
            {
                var r = DeserializeProto<AddAuthenticator_Response>(protoBytes);
                Log($"AddAuth: SharedSecret_len={r.SharedSecret?.Length ?? 0}, Status={r.Status}, ConfirmType={r.ConfirmType}");

                if (r.SharedSecret == null || r.SharedSecret.Length == 0)
                    return (false, "Steam не вернул shared_secret", null, null, null, 0, null, null, null, 0);

                _sharedSecret = Convert.ToBase64String(r.SharedSecret);
                _addAuthResult = new AddAuthenticatorResult
                {
                    SharedSecret = _sharedSecret,
                    RevocationCode = r.RevocationCode,
                    Uri = r.Uri,
                    ServerTime = r.ServerTime,
                    TokenGid = r.TokenGid,
                    IdentitySecret = r.IdentitySecret?.Length > 0 ? r.IdentitySecret : null,
                    Secret1 = r.Secret1?.Length > 0 ? r.Secret1 : null,
                    ConfirmType = r.ConfirmType,
                    Status = r.Status
                };
                return (true, "", _sharedSecret, r.RevocationCode, r.Uri, r.ServerTime, r.TokenGid,
                    r.IdentitySecret?.Length > 0 ? r.IdentitySecret : null,
                    r.Secret1?.Length > 0 ? r.Secret1 : null,
                    r.ConfirmType);
            }

            if (!string.IsNullOrEmpty(json))
            {
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("response", out var r))
                {
                    var ss = r.GetStringSafe("shared_secret");
                    if (string.IsNullOrEmpty(ss))
                        return (false, "Steam не вернул shared_secret", null, null, null, 0, null, null, null, 0);

                    _sharedSecret = ss;
                    var revCode = r.GetStringSafe("revocation_code");
                    var identitySec = r.TryGetProperty("identity_secret", out var idProp) && idProp.ValueKind == JsonValueKind.String ? Convert.FromBase64String(idProp.GetString()!) : null;
                    var secret1Val = r.TryGetProperty("secret_1", out var s1Prop) && s1Prop.ValueKind == JsonValueKind.String ? Convert.FromBase64String(s1Prop.GetString()!) : null;

                    _addAuthResult = new AddAuthenticatorResult
                    {
                        SharedSecret = ss,
                        RevocationCode = revCode,
                        Uri = r.GetStringSafe("uri"),
                        ServerTime = (long)r.GetUlongSafe("server_time"),
                        TokenGid = r.GetStringSafe("token_gid"),
                        IdentitySecret = identitySec,
                        Secret1 = secret1Val,
                        ConfirmType = r.TryGetProperty("confirm_type", out var ct) ? ct.GetInt32() : 0,
                        Status = 0
                    };
                    return (true, "", ss, revCode, r.GetStringSafe("uri"),
                        (long)r.GetUlongSafe("server_time"), r.GetStringSafe("token_gid"),
                        null, null, r.TryGetProperty("confirm_type", out var ct2) ? ct2.GetInt32() : 0);
                }
            }

            return (false, "Неизвестный ответ Steam", null, null, null, 0, null, null, null, 0);
        }
        catch (Exception ex)
        {
            LogError($"AddAuthenticatorAsync EXCEPTION", ex);
            return (false, ex.Message, null, null, null, 0, null, null, null, 0);
        }
    }

    /// <summary>
    /// Возвращает результат AddAuthenticator (для UI после финализации)
    /// </summary>
    public AddAuthenticatorResult GetAddAuthenticatorResult()
    {
        return _addAuthResult ?? new AddAuthenticatorResult
        {
            SharedSecret = _sharedSecret,
            RevocationCode = null
        };
    }

    public async Task<(bool Success, string Error)> FinalizeAuthenticatorAsync(string smsOrEmailCode)
    {
        Log($"=== FinalizeAuthenticatorAsync ===");
        try
        {
            var url = $"https://api.steampowered.com/ITwoFactorService/FinalizeAddAuthenticator/v1?access_token={Uri.EscapeDataString(_accessToken)}";

            for (int tries = 0; tries < 30; tries++)
            {
                string twoFACode = new SteamAuthenticator(_sharedSecret).GenerateCode();
                Log($"Finalize {tries + 1}: TOTP={twoFACode}");

                var req = new FinalizeAddAuthenticator_Request
                {
                    SteamId = _steamId,
                    AuthenticatorCode = twoFACode,
                    AuthenticatorTime = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ConfirmationCode = smsOrEmailCode,
                    ValidateConfirmationCode = true
                };

                var proto = SerializeProto(req);
                var resp = await SendProtoPostAsync(url, proto);
                var (json, protoBytes) = await ReadResponseAsync(resp);

                int status = -1;
                if (protoBytes != null)
                {
                    var r = DeserializeProto<FinalizeAddAuthenticator_Response>(protoBytes);
                    status = r.Status;
                    Log($"Finalize ProtoBuf: Success={r.Success}, Status={status}, WantMore={r.WantMore}");
                }
                else if (!string.IsNullOrEmpty(json))
                {
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("response", out var r))
                        status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetInt32() : -1;
                    Log($"Finalize JSON status: {status}");
                }

                if (status == 2) return (true, "");
                if (status == 89) return (false, "Неверный код подтверждения");

                await Task.Delay(1000);
            }
            return (false, "Превышено количество попыток");
        }
        catch (Exception ex)
        {
            LogError($"FinalizeAuthenticatorAsync EXCEPTION", ex);
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Освобождение ресурсов HttpClient
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _http.Dispose();
        _handler.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Результат AddAuthenticator — для передачи данных в UI
/// </summary>
public class AddAuthenticatorResult
{
    public string? SharedSecret { get; set; }
    public string? RevocationCode { get; set; }
    public string? Uri { get; set; }
    public long ServerTime { get; set; }
    public string? TokenGid { get; set; }
    public byte[]? IdentitySecret { get; set; }
    public byte[]? Secret1 { get; set; }
    public int ConfirmType { get; set; }
    public int Status { get; set; }
}
