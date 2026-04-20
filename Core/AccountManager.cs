using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SteamGuard
{
    public class AccountManager
    {
        private readonly string _mafileDirectory;
        private List<SteamAccount> _accounts = new();
        private List<string> _virtualGroups = new();

        public List<SteamAccount> Accounts => _accounts;
        public SteamAccount? CurrentAccount { get; private set; }

        public AccountManager(string mafileDirectory = "mafile")
        {
            _mafileDirectory = mafileDirectory;
        }

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
                        if (account.Group == Constants.DefaultGroupInternal)
                            account.Group = Constants.DefaultGroup;

                        if (account.SteamId == 0 && account.Session?.SteamId > 0)
                        {
                            account.SteamId = account.Session.SteamId;
                        }

                        AppLogger.Debug($"Loaded account {account.Username}, SteamId: {account.SteamId}");
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

            _accounts = _accounts.GroupBy(a => a.Username).Select(g => g.First()).ToList();
        }

        private void SaveMaFile(SteamAccount account)
        {
            string maFilePath = Path.Combine(_mafileDirectory, $"{account.Username}.mafile");

            AppLogger.Info($"Saving account {account.Username}: AutoTrade={account.AutoTrade}, AutoMarket={account.AutoMarket}");

            if (!string.IsNullOrEmpty(account.Group) && _virtualGroups.Contains(account.Group))
            {
                _virtualGroups.Remove(account.Group);
            }

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

        public List<string> GetGroups()
        {
            var realGroups = _accounts
                .Select(a => a.Group ?? "Без группы")
                .Distinct()
                .ToList();

            return realGroups.Concat(_virtualGroups).Distinct().OrderBy(g => g).ToList();
        }

        public void AddVirtualGroup(string groupName)
        {
            if (!string.IsNullOrWhiteSpace(groupName) && !_virtualGroups.Contains(groupName))
            {
                _virtualGroups.Add(groupName);
                AppLogger.Info($"Virtual group created: {groupName}");
            }
        }
    }
}
