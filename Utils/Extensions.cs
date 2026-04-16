using System.Text.Json;

namespace SteamGuard;

/// <summary>
/// Extension методы для JsonElement
/// </summary>
public static class JsonElementExtensions
{
    /// <summary>
    /// Безопасное получение string из JSON поля
    /// </summary>
    public static string? GetStringSafe(this JsonElement el, string key) =>
        el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    /// <summary>
    /// Безопасное получение ulong из числового JSON поля
    /// </summary>
    public static ulong GetUlongSafe(this JsonElement el, string key, ulong defaultValue = 0) =>
        el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetUInt64() : defaultValue;

    /// <summary>
    /// Получение ulong из строкового или числового JSON поля
    /// </summary>
    public static ulong GetUlongOrStringSafe(this JsonElement el, string key, ulong defaultValue = 0)
    {
        if (el.TryGetProperty(key, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
                return prop.GetUInt64();

            if (prop.ValueKind == JsonValueKind.String)
            {
                var str = prop.GetString();
                if (!string.IsNullOrEmpty(str) && ulong.TryParse(str, out var val))
                    return val;
            }
        }
        return defaultValue;
    }
}

/// <summary>
/// Extension методы для строк
/// </summary>
public static class StringExtensions
{
    private const int MaskedLength = 20;

    /// <summary>
    /// Маскировка логина: 20 символов, первые 2 и последняя 1 буква видны
    /// Пример: "moorich" → "mo*************h" (20 символов)
    /// </summary>
    public static string MaskLogin(this string login)
    {
        if (string.IsNullOrEmpty(login))
            return login;

        char first1 = login[0];
        char first2 = login.Length > 1 ? login[1] : '*';
        char last = login.Length > 2 ? login[^1] : login[0];

        int stars = MaskedLength - 3;
        if (stars < 0) stars = 0;

        return first1.ToString() + first2 + new string('*', stars) + last;
    }
}
