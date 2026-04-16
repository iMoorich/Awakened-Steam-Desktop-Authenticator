namespace SteamGuard
{
    /// <summary>
    /// Константы приложения
    /// </summary>
    public static class Constants
    {
        // Steam API URLs
        public const string SteamCommunityUrl = "https://steamcommunity.com";
        public const string SteamApiBase = "https://api.steampowered.com";
        public const string TimeAlignEndpoint = "https://api.steampowered.com/ITwoFactorService/QueryTime/v0001";

        // Auth API
        public const string AuthGenerateAccessTokenUrl = "https://api.steampowered.com/IAuthenticationService/GenerateAccessTokenForApp/v1";
        public const string AuthGetPasswordRsaUrl = "https://api.steampowered.com/IAuthenticationService/GetPasswordRSAPublicKey/v1";
        public const string AuthBeginSessionUrl = "https://api.steampowered.com/IAuthenticationService/BeginAuthSessionViaCredentials/v1";
        public const string AuthUpdateGuardCodeUrl = "https://api.steampowered.com/IAuthenticationService/UpdateAuthSessionWithSteamGuardCode/v1";
        public const string AuthPollStatusUrl = "https://api.steampowered.com/IAuthenticationService/PollAuthSessionStatus/v1";

        // Login URLs
        public const string LoginHomeUrl = "https://steamcommunity.com/login/home/?goto=";
        public const string LoginFinalizeUrl = "https://login.steampowered.com/jwt/finalizelogin";

        // 2FA
        public const int TotpPeriodSeconds = 30;
        public const string SteamGuardAlphabet = "23456789BCDFGHJKMNPQRTVWXY";

        // UI
        public const int WindowWidth = 350;
        public const int WindowHeight = 600;
        public const int CodeTimerIntervalMs = 1000;

        // Timeouts
        public const int SessionValidationTimeoutSeconds = 5;
        public const int PollMaxAttempts = 15;
        public const int PollDelayMs = 5000;

        // User Agent
        public const string UserAgent = "okhttp/3.12.12";

        // Default values
        public const string DefaultGroup = "Без группы";
        public const string DefaultGroupInternal = "none";

        // File paths
        public const string MaFileDirectory = "mafile";
        public const string SettingsFileName = "app_settings.json";
        public const string LogDirectory = "logs";
        public const string IconFileName = "app.ico";
        public const string IndexHtmlPath = "wwwroot/index.html";

        // Mobile constants
        public const string MobileUserAgent = "okhttp/3.12.12";
        public const string MobileClientVersion = "777777 3.6.1";
        public const string MobileClient = "android";
        public const string MobileLanguage = "english";
        public const string DeviceFriendlyName = "Pixel 6 Pro";
        public const int DevicePlatformType = 3;
        public const int DeviceOsType = -500;
        public const uint DeviceGamingDeviceType = 528;

        // API retry constants
        public const int MaxPollAttempts = 30;
        public const int MaxRetryAttempts = 3;

        // Aliases
        public const string CommunityUrl = SteamCommunityUrl;
    }
}
