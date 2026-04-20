using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SteamGuard
{
    public partial class MainForm
    {
        private async Task RefreshConfirmationsAsync()
        {
            if (_confirmationService == null || _accountManager.CurrentAccount == null) return;

            var account = _accountManager.CurrentAccount;

            try
            {
                var confirmations = await _confirmationService.GetConfirmationsAsync();
                SendToJS("UpdateConfirmations", new { confirmations });
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ошибка получения подтверждений: {ex.Message}");

                if (ex.Message.Contains("needauth") || ex.Message.Contains("401") || ex.Message.Contains("Unauthorized"))
                {
                    try
                    {
                        AppLogger.Info("Обнаружена ошибка авторизации, обновляем сессию...");
                        await SessionRefreshService.RefreshSessionAsync(account);

                        _confirmationService?.Dispose();
                        if (_authenticator != null)
                            _confirmationService = new ConfirmationService(account, _authenticator, _settingsManager);

                        if (_confirmationService == null)
                        {
                            SendToJS("Error", new { message = "Confirmation service is not available" });
                            return;
                        }

                        var confirmations = await _confirmationService.GetConfirmationsAsync();
                        SendToJS("UpdateConfirmations", new { confirmations });
                    }
                    catch (Exception refreshEx)
                    {
                        AppLogger.Error($"Не удалось обновить сессию и получить подтверждения: {refreshEx.Message}");
                        SendToJS("Error", new { });
                    }
                }
                else
                {
                    SendToJS("Error", new { message = ex.Message });
                }
            }
        }

        private async Task AcceptAllConfirmationsAsync()
        {
            if (_confirmationService == null) return;

            try
            {
                bool success = await _confirmationService.AcceptAllConfirmationsAsync();
                SendToJS("ConfirmationsAccepted", new { success });
                await RefreshConfirmationsAsync();
            }
            catch (Exception ex)
            {
                SendToJS("Error", new { message = ex.Message });
            }
        }

        private async Task AcceptConfirmationAsync(string confirmationId)
        {
            if (_confirmationService == null) return;

            try
            {
                var confirmations = await _confirmationService.GetConfirmationsAsync();
                var confirmation = confirmations.FirstOrDefault(c => c.Id.ToString() == confirmationId);

                if (confirmation != null)
                {
                    bool success = await _confirmationService.AcceptConfirmationAsync(confirmation);
                    SendToJS("ConfirmationAccepted", new { success });
                }
            }
            catch (Exception ex)
            {
                SendToJS("Error", new { message = ex.Message });
            }
        }

        private async Task DenyConfirmationAsync(string confirmationId)
        {
            if (_confirmationService == null) return;

            try
            {
                var confirmations = await _confirmationService.GetConfirmationsAsync();
                var confirmation = confirmations.FirstOrDefault(c => c.Id.ToString() == confirmationId);

                if (confirmation != null)
                {
                    bool success = await _confirmationService.DenyConfirmationAsync(confirmation);
                    SendToJS("ConfirmationDenied", new { success });
                }
            }
            catch (Exception ex)
            {
                SendToJS("Error", new { message = ex.Message });
            }
        }

        private async Task RefreshTradesAsync()
        {
            if (_tradeService == null || _accountManager.CurrentAccount == null) return;

            try
            {
                var trades = await _tradeService.GetActiveTradesAsync();
                SendToJS("UpdateTrades", trades);
            }
            catch (Exception ex)
            {
                SendToJS("Error", new { message = ex.Message });
            }
        }

        private async Task RefreshMarketAsync()
        {
            if (_marketService == null || _accountManager.CurrentAccount == null) return;

            try
            {
                var listings = await _marketService.GetActiveListingsAsync();
                SendToJS("UpdateMarketListings", listings);
            }
            catch (Exception ex)
            {
                SendToJS("Error", new { message = ex.Message });
            }
        }

        private async Task AcceptTradeAsync(string tradeId)
        {
            if (_tradeService == null) return;

            try
            {
                bool success = await _tradeService.AcceptTradeAsync(tradeId);
                if (success)
                {
                    SendToJS("TradeAccepted", new { tradeId, success });
                }
                else
                {
                    SendToJS("Error", new { });
                }
            }
            catch (Exception ex)
            {
                SendToJS("Error", new { message = ex.Message });
            }
        }

        private async Task DeclineTradeAsync(string tradeId)
        {
            if (_tradeService == null) return;

            try
            {
                bool success = await _tradeService.DeclineTradeAsync(tradeId);
                if (success)
                {
                    SendToJS("TradeDeclined", new { tradeId, success });
                }
                else
                {
                    SendToJS("Error", new { });
                }
            }
            catch (Exception ex)
            {
                SendToJS("Error", new { message = ex.Message });
            }
        }

        private async Task CancelListingAsync(string listingId)
        {
            if (_marketService == null) return;

            try
            {
                bool success = await _marketService.CancelListingAsync(listingId);
                if (success)
                {
                    SendToJS("ListingCancelled", new { listingId, success });
                }
                else
                {
                    SendToJS("Error", new { });
                }
            }
            catch (Exception ex)
            {
                SendToJS("Error", new { message = ex.Message });
            }
        }

        private void SendToJS(string type, object data)
        {
            if (webView?.CoreWebView2 != null)
            {
                var message = new Dictionary<string, object>
                {
                    ["type"] = type,
                    ["data"] = data
                };
                string json = JsonConvert.SerializeObject(message);
                webView.CoreWebView2.PostWebMessageAsJson(json);
            }
        }

        private string? _pendingLoginAccount;
        private bool _pendingLoginForWallet;

        private async Task RefreshSession(string accountName, bool silentForWallet = false)
        {
            var account = _accountManager.Accounts.FirstOrDefault(a => a.Username == accountName);
            if (account != null)
            {
                try
                {
                    if (await SessionRefreshService.RefreshSessionAsync(account))
                    {
                        account.HasSession = true;
                        _accountManager.SaveAccountSettings(account);
                        UpdateAccountsList();

                        if (silentForWallet)
                        {
                            await GetWalletBalance(accountName);
                        }
                        else
                        {
                            SendToJS("SessionRefreshed", new { });
                        }
                        return;
                    }

                    var loginResult = await SessionLoginService.FullLoginAsync(
                        account.Username,
                        account.Password ?? "",
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

                        if (silentForWallet)
                        {
                            await GetWalletBalance(accountName);
                        }
                        else
                        {
                            SendToJS("SessionRefreshed", new { });
                        }
                    }
                    else if (loginResult.NeedsEmailCode)
                    {
                        _pendingLoginAccount = accountName;
                        SendToJS("RequestEmailCode", new { });
                    }
                    else if (loginResult.Error == "Нет пароля для авторизации" || string.IsNullOrEmpty(account.Password))
                    {
                        _pendingLoginAccount = accountName;
                        _pendingLoginForWallet = silentForWallet;
                        SendToJS("RequestPassword", new { accountName });
                    }
                    else
                    {
                        SendToJS("Error", new { message = loginResult.Error });
                    }
                }
                catch (Exception ex)
                {
                    SendToJS("Error", new { message = ex.Message });
                }
            }
        }

        private async Task RefreshSessionForWallet(string accountName)
        {
            var account = _accountManager.Accounts.FirstOrDefault(a => a.Username == accountName);
            if (account == null) return;

            try
            {
                if (await SessionRefreshService.RefreshSessionAsync(account))
                {
                    account.HasSession = true;
                    _accountManager.SaveAccountSettings(account);
                    UpdateAccountsList();
                    AppLogger.Info($"Сессия обновлена через RefreshToken для {accountName}");
                    await GetWalletBalance(accountName);
                    return;
                }

                var loginResult = await SessionLoginService.FullLoginAsync(
                    account.Username,
                    account.Password ?? "",
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
                    AppLogger.Info($"Сессия успешно обновлена для {accountName}");
                    await GetWalletBalance(accountName);
                }
                else if (loginResult.NeedsEmailCode)
                {
                    _pendingLoginAccount = accountName;
                    _pendingLoginForWallet = true;
                    SendToJS("RequestEmailCode", new { });
                }
                else
                {
                    SendToJS("WalletBalanceError", new { accountName, message = loginResult.Error ?? "Ошибка авторизации" });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ошибка обновления сессии для {accountName}", ex);
                SendToJS("WalletBalanceError", new { accountName, message = ex.Message });
            }
        }

        private async void SubmitEmailCodeForSession(string code)
        {
            if (string.IsNullOrEmpty(_pendingLoginAccount)) return;

            var account = _accountManager.Accounts.FirstOrDefault(a => a.Username == _pendingLoginAccount);
            if (account == null) return;

            try
            {
                var loginResult = await SessionLoginService.FullLoginAsync(
                    account.Username,
                    account.Password ?? "",
                    account.SharedSecret,
                    code
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
                    SendToJS("SessionRefreshed", new { });
                }
                else
                {
                    SendToJS("Error", new { message = loginResult.Error });
                }
            }
            catch (Exception ex)
            {
                SendToJS("Error", new { message = ex.Message });
            }

            _pendingLoginAccount = null;
        }

        private async Task ExecuteScriptAsync(string script)
        {
            if (webView?.CoreWebView2 != null)
            {
                try
                {
                    await webView.ExecuteScriptAsync(script);
                }
                catch { }
            }
        }

        private async Task AutoLoginAsync(string accountName)
        {
            try
            {
                var account = _accountManager.Accounts.Find(a => a.Username == accountName);
                if (account == null)
                {
                    SendToJS("ShowToast", new { message = "Аккаунт не найден" });
                    return;
                }

                if (string.IsNullOrEmpty(account.Username) || string.IsNullOrEmpty(account.Password))
                {
                    SendToJS("ShowToast", new { message = "Отсутствует логин или пароль в настройках аккаунта" });
                    return;
                }

                string guardCode = "";
                if (!string.IsNullOrEmpty(account.SharedSecret))
                {
                    var authenticator = new SteamAuthenticator(account.SharedSecret);
                    guardCode = authenticator.GenerateCode();
                }

                if (string.IsNullOrEmpty(guardCode))
                {
                    SendToJS("ShowToast", new { message = "Не удалось сгенерировать 2FA код" });
                    return;
                }

                SendToJS("ShowToast", new { message = $"Запуск автологина для {accountName}..." });

                var steamProcesses = Process.GetProcessesByName("steam");
                foreach (var proc in steamProcesses)
                {
                    try
                    {
                        proc.Kill();
                        await proc.WaitForExitAsync();
                    }
                    catch { }
                }

                await Task.Delay(2000);

                var steamPath = GetSteamPath();
                if (string.IsNullOrEmpty(steamPath))
                {
                    SendToJS("ShowToast", new { message = "Steam не найден" });
                    return;
                }

                var steamExe = System.IO.Path.Combine(steamPath, "steam.exe");
                if (!System.IO.File.Exists(steamExe))
                {
                    SendToJS("ShowToast", new { message = "Steam не найден по пути: " + steamExe });
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = steamExe,
                    UseShellExecute = false,
                    WorkingDirectory = steamPath
                };

                var process = Process.Start(startInfo);
                if (process == null)
                {
                    SendToJS("ShowToast", new { message = "Не удалось запустить Steam" });
                    return;
                }

                int steamPid = process.Id;

                SendToJS("ShowToast", new { message = $"Запуск автологина для {accountName}..." });

                var autoLoginService = new SteamAutoLoginService(
                    onStatusChanged: status =>
                    {
                        AppLogger.Info($"[AutoLogin] {status}");
                    },
                    onLoginCompleted: () =>
                    {
                        AppLogger.Info("[AutoLogin] Авторизация завершена!");
                        SendToJS("ShowToast", new { message = "Авторизация завершена!" });
                    },
                    steamPid: steamPid
                );

                autoLoginService.Start(account.Username, account.Password, guardCode, accountName);
            }
            catch (Exception ex)
            {
                SendToJS("ShowToast", new { message = $"Ошибка: {ex.Message}" });
            }
        }

        private string? GetSteamPath()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                return key?.GetValue("SteamPath")?.ToString();
            }
            catch
            {
                return @"C:\Program Files (x86)\Steam";
            }
        }
    }
}
