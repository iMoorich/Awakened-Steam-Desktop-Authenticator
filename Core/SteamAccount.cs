namespace SteamGuard
{
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
        public bool IsFavorite { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public bool HasSession { get; set; }

        public bool AutoTrade { get; set; }

        public bool AutoMarket { get; set; }

        [Newtonsoft.Json.JsonProperty("Balance")]
        public string Balance { get; set; } = string.Empty;
    }

    public class MaFileProxy
    {
        [Newtonsoft.Json.JsonProperty("Id")]
        public int Id { get; set; }

        [Newtonsoft.Json.JsonProperty("Data")]
        public ProxyData? Data { get; set; }
    }

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
}
