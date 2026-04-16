using SteamGuard;
using ProtoBuf;

namespace SteamGuard;

#region ProtoBuf Contracts для Steam API

[ProtoContract]
public class BeginAuthSessionViaCredentials_Request
{
    [ProtoMember(1)] public string DeviceFriendlyName { get; set; } = "";
    [ProtoMember(2)] public string AccountName { get; set; } = "";
    [ProtoMember(3)] public string EncryptedPassword { get; set; } = "";
    [ProtoMember(4)] public ulong EncryptionTimestamp { get; set; }
    [ProtoMember(5)] public bool RememberLogin { get; set; }
    [ProtoMember(6)] public int PlatformType { get; set; }
    [ProtoMember(7)] public int Persistence { get; set; }
    [ProtoMember(8)] public string WebsiteId { get; set; } = "";
    [ProtoMember(9)] public DeviceDetailsProto DeviceDetails { get; set; } = new();
    [ProtoMember(10)] public string GuardData { get; set; } = "";
    [ProtoMember(11)] public uint Language { get; set; }
}

[ProtoContract]
public class DeviceDetailsProto
{
    [ProtoMember(1)] public string DeviceFriendlyName { get; } = "";
    [ProtoMember(2)] public int PlatformType { get; }
    [ProtoMember(3)] public int? OsType { get; }
    [ProtoMember(4)] public uint? GamingDeviceType { get; }

    public DeviceDetailsProto() { }

    public DeviceDetailsProto(string deviceFriendlyName, int platformType, int? osType, uint? gamingDeviceType)
    {
        DeviceFriendlyName = deviceFriendlyName;
        PlatformType = platformType;
        OsType = osType;
        GamingDeviceType = gamingDeviceType;
    }

    public static DeviceDetailsProto CreateMobileDetails()
    {
        return new DeviceDetailsProto(
            Constants.DeviceFriendlyName,
            Constants.DevicePlatformType,
            Constants.DeviceOsType,
            Constants.DeviceGamingDeviceType);
    }
}

[ProtoContract]
public class BeginAuthSessionViaCredentials_Response
{
    [ProtoMember(1)] public ulong ClientId { get; set; }
    [ProtoMember(2, DataFormat = DataFormat.FixedSize)] public byte[] RequestId { get; set; } = Array.Empty<byte>();
    [ProtoMember(3)] public float Interval { get; set; }
    [ProtoMember(4)] public List<AllowedConfirmationMsg> AllowedConfirmations { get; } = new();
    [ProtoMember(5)] public ulong Steamid { get; set; }
    [ProtoMember(6)] public string WeakToken { get; set; } = "";
    [ProtoMember(7)] public string AgreementSessionUrl { get; set; } = "";
    [ProtoMember(8)] public string ExtendedErrorMessage { get; set; } = "";
}

[ProtoContract]
public class AllowedConfirmationMsg
{
    [ProtoMember(1)] public int ConfirmationType { get; set; }
    [ProtoMember(2)] public string AssociatedMessage { get; set; } = "";
}

[ProtoContract]
public class PollAuthSessionStatus_Request
{
    [ProtoMember(1)] public ulong ClientId { get; set; }
    [ProtoMember(2, DataFormat = DataFormat.FixedSize)] public byte[] RequestId { get; set; } = Array.Empty<byte>();
}

[ProtoContract]
public class PollAuthSessionStatus_Response
{
    [ProtoMember(1)] public ulong NewClientId { get; set; }
    [ProtoMember(2)] public string NewChallengeUrl { get; set; } = "";
    [ProtoMember(3)] public string RefreshToken { get; set; } = "";
    [ProtoMember(4)] public string AccessToken { get; set; } = "";
}

[ProtoContract]
public class UpdateAuthSessionWithSteamGuardCode_Request
{
    [ProtoMember(1)] public ulong ClientId { get; set; }
    [ProtoMember(2, DataFormat = DataFormat.FixedSize)] public ulong Steamid { get; set; }
    [ProtoMember(3)] public string Code { get; set; } = "";
    [ProtoMember(4)] public int CodeType { get; set; }
}

[ProtoContract]
public class AddAuthenticator_Request
{
    [ProtoMember(1, DataFormat = DataFormat.FixedSize)] public ulong SteamId { get; set; }
    [ProtoMember(4)] public int AuthenticatorType { get; set; }
    [ProtoMember(5)] public string DeviceIdentifier { get; set; } = "";
    [ProtoMember(6)] public string SmsPhoneId { get; set; } = "";
    [ProtoMember(8)] public int Version { get; set; }
}

[ProtoContract]
public class AddAuthenticator_Response
{
    [ProtoMember(1)] public byte[] SharedSecret { get; set; } = Array.Empty<byte>();
    [ProtoMember(2)] public ulong SerialNumber { get; set; }
    [ProtoMember(3)] public string RevocationCode { get; set; } = "";
    [ProtoMember(4)] public string Uri { get; set; } = "";
    [ProtoMember(5)] public long ServerTime { get; set; }
    [ProtoMember(6)] public string AccountName { get; set; } = "";
    [ProtoMember(7)] public string TokenGid { get; set; } = "";
    [ProtoMember(8)] public byte[] IdentitySecret { get; set; } = Array.Empty<byte>();
    [ProtoMember(9)] public byte[] Secret1 { get; set; } = Array.Empty<byte>();
    [ProtoMember(10)] public int Status { get; set; }
    [ProtoMember(11)] public string PhoneNumberHint { get; set; } = "";
    [ProtoMember(12)] public int ConfirmType { get; set; }
}

[ProtoContract]
public class FinalizeAddAuthenticator_Request
{
    [ProtoMember(1, DataFormat = DataFormat.FixedSize)] public ulong SteamId { get; set; }
    [ProtoMember(2)] public string AuthenticatorCode { get; set; } = "";
    [ProtoMember(3)] public ulong AuthenticatorTime { get; set; }
    [ProtoMember(4)] public string ConfirmationCode { get; set; } = "";
    [ProtoMember(6)] public bool ValidateConfirmationCode { get; set; }
}

[ProtoContract]
public class FinalizeAddAuthenticator_Response
{
    [ProtoMember(1)] public bool Success { get; set; }
    [ProtoMember(2)] public bool WantMore { get; set; }
    [ProtoMember(3)] public ulong ServerTime { get; set; }
    [ProtoMember(4)] public int Status { get; set; }
}

[ProtoContract]
public class IsAccountWaitingForEmailConfirmation_Response
{
    [ProtoMember(1)] public bool IsWaiting { get; set; }
    [ProtoMember(2)] public int SecondsToWait { get; set; }
}

[ProtoContract]
public class EmptyMessage { }

[ProtoContract]
public class GenerateAccessTokenForApp_Request
{
    [ProtoMember(1)] public string RefreshToken { get; set; } = "";
    [ProtoMember(2, DataFormat = DataFormat.FixedSize)] public ulong SteamId { get; set; }
    [ProtoMember(3)] public bool TokenRenewalType { get; set; }
}

[ProtoContract]
public class GenerateAccessTokenForApp_Response
{
    [ProtoMember(1)] public string AccessToken { get; set; } = "";
    [ProtoMember(2)] public string WarningMessage { get; set; } = "";
    [ProtoMember(3)] public int EResult { get; set; }
}

#endregion
