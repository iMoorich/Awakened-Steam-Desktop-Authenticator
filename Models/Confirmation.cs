namespace SteamGuard;

/// <summary>
/// Тип подтверждения сессии Steam
/// </summary>
public enum AuthConfirmationType
{
    Unknown = 0,
    None = 1,
    EmailCode = 2,
    DeviceCode = 3,
    DeviceConfirmation = 4,
    EmailConfirmation = 5,
    MachineToken = 6,
    LegacyMachineAuth = 7
}

/// <summary>
/// Типы кодов для UpdateAuthSession
/// </summary>
public enum AuthCodeType
{
    Unknown = 0,
    EmailCode = 2,
    DeviceCode = 3
}

/// <summary>
/// Коды результата Steam API
/// </summary>
public static class EResult
{
    public const int OK = 1;
    public const int ServiceUnavailable = 2;
    public const int InvalidCredentials = 8;
    public const int RateLimit = 5;
    public const int SessionExpired = 6;
    public const int InvalidLoginAuthCode = 65;

    public static string GetMessage(int eresult) => eresult switch
    {
        OK => "OK",
        InvalidLoginAuthCode => "Неверный код подтверждения",
        SessionExpired => "Сессия истекла",
        RateLimit => "Слишком много попыток",
        ServiceUnavailable => "Сервер Steam недоступен",
        InvalidCredentials => "Неверные учётные данные",
        _ => $"Ошибка Steam (eresult={eresult})"
    };
}

/// <summary>
/// Тип подтверждения Steam
/// </summary>
public enum ConfirmationType
{
    Unknown = 0,
    Trade = 2,
    MarketSellTransaction = 3,
    AccountRecovery = 6,
    RegisterApiKey = 9,
    Purchase = 12
}

/// <summary>
/// Подтверждение Steam (трейд, торговая площадка и т.д.)
/// </summary>
public class Confirmation
{
    public long Id { get; set; }
    public ulong Nonce { get; set; }
    public ulong CreatorId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string Headline { get; set; } = string.Empty;
    public DateTime Time { get; set; }
    public ConfirmationType ConfType { get; set; }
    public int IntType { get; set; }
    public List<string> Summary { get; set; } = new();

    public string TypeDescription => ConfType switch
    {
        ConfirmationType.Trade => "Обмен",
        ConfirmationType.MarketSellTransaction => "Продажа на торговой площадке",
        ConfirmationType.AccountRecovery => "Восстановление аккаунта",
        ConfirmationType.RegisterApiKey => "Регистрация API ключа",
        ConfirmationType.Purchase => "Покупка",
        _ => "Неизвестно"
    };

    public ulong TradeId => CreatorId;
}
