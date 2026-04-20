using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ProtoBuf;

namespace SteamGuard
{
    [ProtoContract]
    public class GuardCodeRequestProto
    {
        [ProtoMember(1)] public ulong ClientId { get; set; }
        [ProtoMember(2, DataFormat = DataFormat.FixedSize)] public ulong Steamid { get; set; }
        [ProtoMember(3)] public string Code { get; set; } = "";
        [ProtoMember(4)] public int CodeType { get; set; }
    }

    public static class SessionRefreshService
    {
        private const string ApiUrl = Constants.AuthGenerateAccessTokenUrl;

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
                uint tag = ReadVarint(reader);
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

    public static class SessionLoginService
    {
        private const string ApiBase = Constants.SteamApiBase;

        public static async Task<LoginResult> LoginOrRefreshAsync(SteamAccount account)
        {
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

            if (string.IsNullOrEmpty(account.Password))
            {
                return new LoginResult { Success = false, Error = "Нет пароля для авторизации" };
            }

            return await FullLoginAsync(account.Username, account.Password, account.SharedSecret);
        }

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

                AppLogger.Info($"Запрос RSA ключа для {username}");
                var rsaResult = await GetPasswordRsaPublicKeyAsync(client, username);
                if (rsaResult == null)
                {
                    AppLogger.Error("Не удалось получить RSA ключ");
                    return new LoginResult { Success = false, Error = "Не удалось получить RSA ключ" };
                }

                var rsaData = rsaResult.Value;
                AppLogger.Info($"RSA ключ получен. timestamp: {rsaData.timestamp}");

                var encryptedPassword = CryptoHelper.EncryptPasswordRsa(password, rsaData.publicKeyMod, rsaData.publicKeyExp);
                AppLogger.Info($"Пароль зашифрован, длина: {encryptedPassword.Length}");

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

                if (allowedConfirmations.Length > 0)
                {
                    var guardType = allowedConfirmations[0];
                    AppLogger.Info($"Требуется guard код типа {guardType}");

                    if (guardType == 2)
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
                    else if (guardType == 3 && !string.IsNullOrEmpty(sharedSecret))
                    {
                        await SteamAuthenticator.AlignTimeAsync();
                        var steamTime = await SteamAuthenticator.GetSteamTimeAsync();

                        var codes = new[]
                        {
                            Generate2FACode(sharedSecret, steamTime - 30),
                            Generate2FACode(sharedSecret, steamTime),
                            Generate2FACode(sharedSecret, steamTime + 30)
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

                AppLogger.Info("Polling auth session status...");
                var pollResult = await PollAuthSessionStatusAsync(client, clientId, requestId);
                if (pollResult == null)
                {
                    AppLogger.Error("Не удалось получить токены");
                    return new LoginResult { Success = false, Error = "Не удалось получить токены" };
                }
                AppLogger.Info("Токены получены");

                var pollData = pollResult.Value;
                var sessionId = await GetSessionIdAsync(client);
                AppLogger.Info($"SessionId получен: {sessionId}");

                var finalizeResult = await FinalizeLoginAsync(client, pollData.refreshToken, steamId, sessionId);
                if (!finalizeResult.success)
                {
                    AppLogger.Error("Не удалось завершить вход (FinalizeLogin)");
                    return new LoginResult { Success = false, Error = "Не удалось завершить вход" };
                }

                AppLogger.Info($"Авторизация успешна: steamId={steamId}");

                return new LoginResult
                {
                    Success = true,
                    AccessToken = pollData.accessToken,
                    RefreshToken = pollData.refreshToken,
                    SteamLoginSecure = finalizeResult.steamLoginSecure,
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

        private static async Task<(string publicKeyMod, string publicKeyExp, long timestamp)?> GetPasswordRsaPublicKeyAsync(HttpClient client, string accountName)
        {
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

            var json = await response.Content.ReadAsStringAsync();
            AppLogger.Debug($"RSA response JSON: {json}");

            try
            {
                var jObj = Newtonsoft.Json.Linq.JObject.Parse(json);
                var responseObj = jObj["response"];
                if (responseObj == null) return null;

                var publicKeyMod = responseObj["publickey_mod"]?.ToString() ?? "";
                var publicKeyExp = responseObj["publickey_exp"]?.ToString() ?? "";
                var timestamp = responseObj["timestamp"]?.ToObject<long?>() ?? 0;

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

        private static async Task<(ulong clientId, ulong steamId, byte[] requestId, int[] allowedConfirmations, string errorMessage)?> BeginAuthSessionAsync(
            HttpClient client, string accountName, string encryptedPassword, long timestamp)
        {
            var requestBody = EncodeBeginAuthRequest(accountName, encryptedPassword, timestamp);
            var base64Body = Convert.ToBase64String(requestBody);
            AppLogger.Debug($"BeginAuth request bytes: {requestBody.Length} [{BitConverter.ToString(requestBody.Take(50).ToArray()).Replace("-", " ")}...]");

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
            ProtobufHelper.WriteString(ms, 1, "Steam Guard");
            ProtobufHelper.WriteString(ms, 2, accountName);
            ProtobufHelper.WriteString(ms, 3, encryptedPassword);
            ProtobufHelper.WriteTag(ms, 4, 0);
            ProtobufHelper.WriteVarint(ms, (ulong)timestamp);
            ProtobufHelper.WriteTag(ms, 5, 0);
            ProtobufHelper.WriteVarint(ms, 1u);
            ProtobufHelper.WriteTag(ms, 6, 0);
            ProtobufHelper.WriteVarint(ms, 3u);
            ProtobufHelper.WriteTag(ms, 7, 0);
            ProtobufHelper.WriteVarint(ms, 1u);
            ProtobufHelper.WriteString(ms, 8, "Mobile");
            WriteBeginAuthDeviceDetails(ms, "Pixel 6 Pro", 3, -500, 528);
            return ms.ToArray();
        }

        private static void WriteBeginAuthDeviceDetails(Stream ms, string deviceName, int platformType, int osType, uint gamingDeviceType)
        {
            using var innerMs = new MemoryStream();
            ProtobufHelper.WriteString(innerMs, 1, deviceName);
            ProtobufHelper.WriteTag(innerMs, 2, 0);
            ProtobufHelper.WriteVarint(innerMs, (uint)platformType);
            ProtobufHelper.WriteTag(innerMs, 3, 0);
            ProtobufHelper.WriteSInt32(innerMs, osType);
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
                ulong tag = ReadVarint(reader);
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
                else if (fieldNumber == 8 && wireType == 2)
                {
                    int len = (int)ReadVarint(reader);
                    errorMessage = Encoding.UTF8.GetString(reader.ReadBytes(len));
                }
                else SkipField(reader, wireType);
            }

            if (!string.IsNullOrEmpty(errorMessage))
                AppLogger.Warn($"BeginAuth error message: {errorMessage}");

            AppLogger.Info($"BeginAuth decoded: clientId={clientId}, steamId={steamId}, requestId length={requestId.Length}, confirmations=[{string.Join(",", confirmations)}]");
            return (clientId, steamId, requestId, confirmations.ToArray(), errorMessage);
        }

        private static int DecodeConfirmationType(byte[] data)
        {
            using var ms = new MemoryStream(data);
            var reader = new BinaryReader(ms);
            while (ms.Position < ms.Length)
            {
                ulong tag = ReadVarint(reader);
                int fieldNumber = (int)(tag >> 3);
                int wireType = (int)(tag & 0x07);
                if (fieldNumber == 1 && wireType == 0) return (int)ReadVarint(reader);
                SkipField(reader, wireType);
            }
            return 0;
        }

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
            ProtobufHelper.WriteTag(ms, 1, 0);
            ProtobufHelper.WriteVarint(ms, clientId);
            ProtobufHelper.WriteTag(ms, 2, 1);
            ProtobufHelper.WriteFixed64(ms, steamId);
            ProtobufHelper.WriteString(ms, 3, code);
            ProtobufHelper.WriteTag(ms, 4, 0);
            ProtobufHelper.WriteVarint(ms, (uint)codeType);

            var manualBytes = ms.ToArray();

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

        private static async Task<(string accessToken, string refreshToken)?> PollAuthSessionStatusAsync(HttpClient client, ulong clientId, byte[] requestId)
        {
            var requestBody = EncodePollRequest(clientId, requestId);
            var base64Body = Convert.ToBase64String(requestBody);
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("input_protobuf_encoded", base64Body)
            });

            AppLogger.Debug($"Poll request: clientId={clientId}, requestId={BitConverter.ToString(requestId).Replace("-", "")}");

            const int maxAttempts = 15;
            const int delayMs = 5000;
            for (int i = 0; i < maxAttempts; i++)
            {
                AppLogger.Debug($"Poll attempt {i + 1}/{maxAttempts}...");
                var response = await client.PostAsync($"{ApiBase}/IAuthenticationService/PollAuthSessionStatus/v1", content);

                AppLogger.Debug($"Poll HTTP status: {(int)response.StatusCode} {response.StatusCode}");
                var headers = string.Join(", ", response.Headers.Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));
                AppLogger.Debug($"Poll response headers: {headers}");

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

                if (response.Headers.TryGetValues("X-eresult", out var eResultValues))
                {
                    var eResultStr = eResultValues.FirstOrDefault();
                    if (int.TryParse(eResultStr, out int eResult) && eResult == 9)
                    {
                        AppLogger.Error("Poll: X-eresult=9 (InvalidState) — сессия недействительна или ожидает другого подтверждения");
                        return null;
                    }
                }

                AppLogger.Debug($"Poll attempt {i + 1}: ещё не готово, ждём...");
                await Task.Delay(delayMs);
            }
            AppLogger.Error($"Poll auth session status: все {maxAttempts} попыток провалились");
            return null;
        }

        private static byte[] EncodePollRequest(ulong clientId, byte[] requestId)
        {
            using var ms = new MemoryStream();
            ProtobufHelper.WriteTag(ms, 1, 0);
            ProtobufHelper.WriteVarint(ms, clientId);
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
                ulong tag = ReadVarint(reader);
                int fieldNumber = (int)(tag >> 3);
                int wireType = (int)(tag & 0x07);

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

                var nonceMatch = System.Text.RegularExpressions.Regex.Match(html, @"transfer_info[^{]*{[^}]*""nonce"":""([^""]+)""");
                var authMatch = System.Text.RegularExpressions.Regex.Match(html, @"transfer_info[^{]*{[^}]*""auth"":""([^""]+)""");
                var urlMatch = System.Text.RegularExpressions.Regex.Match(html, @"""url"":""([^""]+)""");

                if (!nonceMatch.Success || !authMatch.Success || !urlMatch.Success)
                    return (false, "");

                var nonce = nonceMatch.Groups[1].Value;
                var auth = authMatch.Groups[1].Value;
                var transferUrl = urlMatch.Groups[1].Value.Replace("\\/", "/");

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

                var cookieMatch = System.Text.RegularExpressions.Regex.Match(setCookie, @"steamLoginSecure=([^;]+)");
                if (cookieMatch.Success)
                {
                    return (true, cookieMatch.Groups[1].Value);
                }

                return (false, "");
            }
            catch
            {
                return (false, "");
            }
        }

        private static string Generate2FACode(string sharedSecret, long? steamTime = null)
        {
            try
            {
                var unescaped = System.Text.RegularExpressions.Regex.Unescape(sharedSecret);
                var secret = Convert.FromBase64String(unescaped);

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
