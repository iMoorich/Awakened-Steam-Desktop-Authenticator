using System.Text.Json.Serialization;

namespace SteamGuard;

/// <summary>
/// JWT сессия Steam — для входа без пароля
/// </summary>
public class SteamSession
{
    /// <summary>JWT Access Token для мобильных API</summary>
    public string? AccessToken { get; set; }

    /// <summary>JWT Refresh Token — для обновления AccessToken</summary>
    public string? RefreshToken { get; set; }

    /// <summary>Cookie steamLoginSecure: {SteamId}%7C%7C{AccessToken}</summary>
    public string? SteamLoginSecure { get; set; }

    /// <summary>Steam ID 64</summary>
    public ulong SteamID { get; set; }

    /// <summary>Session ID для POST запросов</summary>
    public string? SessionID { get; set; }

    /// <summary>Время создания сессии</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Время истечения Refresh Token (обычно ~21 день)</summary>
    [JsonIgnore]
    public bool IsExpired => RefreshTokenExpired();

    /// <summary>Проверить, что Refresh Token ещё валиден</summary>
    public bool RefreshTokenExpired() => JwtExpired(RefreshToken);

    /// <summary>Проверить, что Access Token ещё валиден</summary>
    public bool AccessTokenExpired() => JwtExpired(AccessToken);

    private static bool JwtExpired(string? token)
    {
        if (string.IsNullOrEmpty(token)) return true;
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return true;
            var payload = parts[1];
            var padding = payload.Length % 4;
            if (padding > 0) payload += new string('=', 4 - padding);
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("exp", out var expProp) && expProp.TryGetInt64(out var exp))
            {
                return DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime < DateTime.UtcNow;
            }
        }
        catch { /* Не JWT — считаем валидным */ }
        return false;
    }
}
