using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace SteamGuard
{
    public partial class MainForm
    {
        private void UpdateCodes()
        {
            if (_authenticator == null) return;

            try
            {
                var codes = _authenticator.GetCodes();
                var codesObj = new
                {
                    previous = codes.Previous,
                    current = codes.Current,
                    next = codes.Next
                };

                SendToJS("UpdateCodes", new { codes = codesObj, timeLeft = _timeLeft });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка генерации кода: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateAccountsList()
        {
            var accountsData = _accountManager.Accounts.Select(a => new
            {
                username = a.Username,
                password = a.Password,
                group = a.Group,
                hasSession = a.HasSession,
                autoTrade = a.AutoTrade,
                autoMarket = a.AutoMarket,
                hasProxy = a.Proxy != null || !string.IsNullOrEmpty(_settingsManager.Settings.GlobalProxy),
                isFavorite = a.IsFavorite,
                steamId = a.SteamId.ToString(),
                sharedSecret = a.SharedSecret,
                identitySecret = a.IdentitySecret,
                revocationCode = a.RevocationCode,
                deviceId = a.DeviceId,
                balance = a.Balance
            }).ToList();

            SendToJS("UpdateAccounts", new
            {
                accounts = accountsData,
                hideLogins = _settingsManager.Settings.HideLogins,
                language = _settingsManager.Settings.Language
            });
        }

        private void SwitchAccount(string accountName)
        {
            var account = _accountManager.Accounts.FirstOrDefault(a => a.Username == accountName);

            if (account != null)
            {
                _accountManager.SetCurrentAccount(account);

                try
                {
                    _authenticator = new SteamAuthenticator(account.SharedSecret);
                    InitializeServices();
                    UpdateCodes();

                    SendToJS("AccountSwitched", new { username = accountName });
                }
                catch (Exception ex)
                {
                    SendToJS("Error", new { message = ex.Message });
                }
            }
        }

        private void GenerateCodeForAccount(string accountName)
        {
            var account = _accountManager.Accounts.FirstOrDefault(a => a.Username == accountName);
            if (account != null && !string.IsNullOrEmpty(account.SharedSecret))
            {
                try
                {
                    var authenticator = new SteamAuthenticator(account.SharedSecret);
                    var code = authenticator.GenerateCode();
                    SendToJS("CodeGenerated", new { accountName, code });
                }
                catch (Exception ex)
                {
                    SendToJS("Error", new { message = $"Ошибка генерации кода: {ex.Message}" });
                }
            }
        }

        private void InitializeServices()
        {
            if (_accountManager.CurrentAccount != null && _authenticator != null)
            {
                _confirmationService = new ConfirmationService(
                    _accountManager.CurrentAccount,
                    _authenticator,
                    _settingsManager);

                _tradeService = new TradeService(_accountManager.CurrentAccount, _settingsManager);
                _marketService = new MarketService(_accountManager.CurrentAccount, _settingsManager);
                _walletService = new WalletService(_accountManager.CurrentAccount, _settingsManager);
            }
        }

        private async Task<bool> IsSessionValidAsync(SteamAccount account)
        {
            if (string.IsNullOrEmpty(account.Session?.AccessToken) ||
                string.IsNullOrEmpty(account.Session?.SteamLoginSecure))
            {
                return false;
            }

            try
            {
                var handler = new System.Net.Http.HttpClientHandler();
                var cookieContainer = new System.Net.CookieContainer();

                cookieContainer.Add(new System.Net.Cookie("sessionid", account.Session.SessionId ?? "")
                {
                    Domain = "steamcommunity.com",
                    Path = "/"
                });
                cookieContainer.Add(new System.Net.Cookie("steamLoginSecure", account.Session.SteamLoginSecure)
                {
                    Domain = "steamcommunity.com",
                    Path = "/"
                });

                handler.CookieContainer = cookieContainer;

                using var client = new System.Net.Http.HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(Constants.SessionValidationTimeoutSeconds);
                var response = await client.GetAsync("https://steamcommunity.com/actions/GetNotificationCounts");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> TryRefreshSessionAsync(SteamAccount account)
        {
            if (await SessionRefreshService.RefreshSessionAsync(account))
            {
                account.HasSession = true;
                account.SteamId = account.Session?.SteamId > 0 ? account.Session.SteamId : account.SteamId;
                _accountManager.SaveAccountSettings(account);
                UpdateAccountsList();
                return true;
            }

            if (!string.IsNullOrEmpty(account.Password))
            {
                var loginResult = await SessionLoginService.FullLoginAsync(
                    account.Username,
                    account.Password,
                    account.SharedSecret
                );

                if (loginResult.Success)
                {
                    account.Session = new MaFileSession
                    {
                        AccessToken = loginResult.AccessToken ?? "",
                        RefreshToken = loginResult.RefreshToken ?? "",
                        SteamLoginSecure = loginResult.SteamLoginSecure ?? "",
                        SessionId = loginResult.SessionId ?? "",
                        SteamId = loginResult.SteamId
                    };
                    account.SteamId = loginResult.SteamId;
                    account.HasSession = true;
                    _accountManager.SaveAccountSettings(account);
                    UpdateAccountsList();
                    return true;
                }
            }

            return false;
        }

        private void ImportMaFile(string content)
        {
            try
            {
                var maFileData = JsonConvert.DeserializeObject<SteamAccount>(content);

                if (maFileData != null && !string.IsNullOrEmpty(maFileData.Username))
                {
                    _accountManager.AddAccount(maFileData);
                    UpdateAccountsList();
                    SendToJS("AccountAdded", new { });
                }
            }
            catch (Exception ex)
            {
                SendToJS("Error", new { message = ex.Message });
            }
        }

        private void ToggleFavorite(string accountName)
        {
            var account = _accountManager.Accounts.FirstOrDefault(a => a.Username == accountName);
            if (account == null) return;

            account.IsFavorite = !account.IsFavorite;
            _accountManager.SaveAccountSettings(account);
            UpdateAccountsList();
        }

        private async Task GetWalletBalance(string accountName)
        {
            try
            {
                var account = _accountManager.Accounts.FirstOrDefault(a => a.Username == accountName);
                if (account == null)
                {
                    SendToJS("WalletBalanceError", new { accountName });
                    return;
                }

                bool needsSessionRefresh = false;

                if (!account.HasSession)
                {
                    needsSessionRefresh = true;
                    AppLogger.Warn($"Нет активной сессии для {accountName}", "WalletService");
                }
                else
                {
                    using var walletService = new WalletService(account, _settingsManager);
                    var walletInfo = await walletService.GetWalletInfoAsync();

                    if (walletInfo != null && walletInfo.IsSessionExpired)
                    {
                        needsSessionRefresh = true;
                        AppLogger.Warn($"Сессия устарела для {accountName}");
                    }
                    else if (walletInfo != null && !walletInfo.IsSessionExpired)
                    {
                        AppLogger.Info($"Баланс получен: {walletInfo.BalanceFormatted}");

                        var cleanBalance = walletInfo.BalanceFormatted
                            .Replace(",--", "")
                            .Replace(" руб.", "₽")
                            .Replace("руб.", "₽")
                            .Replace(" руб", "₽")
                            .Replace("руб", "₽");
                        account.Balance = cleanBalance;
                        _accountManager.SaveAccountSettings(account);

                        SendToJS("WalletBalanceReceived", new
                        {
                            accountName,
                            balance = walletInfo.Balance,
                            balanceFormatted = walletInfo.BalanceFormatted,
                            currencyCode = walletInfo.CurrencyCode,
                            currencySymbol = walletInfo.CurrencySymbol
                        });
                        return;
                    }
                }

                if (needsSessionRefresh)
                {
                    if (string.IsNullOrEmpty(account.Password))
                    {
                        _pendingLoginAccount = accountName;
                        _pendingLoginForWallet = true;
                        SendToJS("RequestPassword", new { accountName });
                        return;
                    }

                    AppLogger.Info($"Обновление сессии в фоне для {accountName}...");
                    await RefreshSessionForWallet(accountName);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ошибка получения баланса для {accountName}", ex);
                SendToJS("WalletBalanceError", new { accountName, message = ex.Message });
            }
        }

        private void SendGroupsToJS()
        {
            var groups = _accountManager.GetGroups();
            SendToJS("ApplyGroups", new { groups });
        }

        private void RemoveAccount(string accountName)
        {
            var account = _accountManager.Accounts.FirstOrDefault(a => a.Username == accountName);
            if (account != null)
            {
                _accountManager.RemoveAccount(account);

                if (_accountManager.CurrentAccount?.Username == accountName)
                {
                    if (_accountManager.Accounts.Count > 0)
                    {
                        _accountManager.SetCurrentAccount(_accountManager.Accounts[0]);
                        _authenticator = new SteamAuthenticator(_accountManager.CurrentAccount.SharedSecret);
                        InitializeServices();
                        UpdateCodes();
                    }
                    else
                    {
                        _accountManager.SetCurrentAccount(null!);
                        _authenticator = null;
                    }
                }

                UpdateAccountsList();
                SendGroupsToJS();
                SendToJS("AccountRemoved", new { });
            }
        }
    }
}
