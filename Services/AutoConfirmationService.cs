using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SteamGuard
{
    /// <summary>
    /// Сервис автоматического подтверждения трейдов и маркета
    /// </summary>
    public class AutoConfirmationService : IDisposable
    {
        private readonly AccountManager _accountManager;
        private readonly SettingsManager _settingsManager;
        private System.Threading.Timer? _timer;
        private bool _isRunning = false;
        private readonly int _checkIntervalMs = 30000; // 30 секунд

        public AutoConfirmationService(AccountManager accountManager, SettingsManager settingsManager)
        {
            _accountManager = accountManager;
            _settingsManager = settingsManager;
        }

        public void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _timer = new System.Threading.Timer(async _ => await CheckAndConfirmAsync(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(_checkIntervalMs));
            AppLogger.Info("AutoConfirmationService started");
        }

        public void Stop()
        {
            _isRunning = false;
            _timer?.Dispose();
            _timer = null;
            AppLogger.Info("AutoConfirmationService stopped");
        }

        private async Task CheckAndConfirmAsync()
        {
            if (!_isRunning) return;

            try
            {
                // Проходим по всем аккаунтам с включенным автоподтверждением
                var accountsToCheck = _accountManager.Accounts
                    .Where(a => (a.AutoTrade || a.AutoMarket) && a.HasSession && !string.IsNullOrEmpty(a.SharedSecret))
                    .ToList();

                foreach (var account in accountsToCheck)
                {
                    try
                    {
                        await ProcessAccountAsync(account);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error($"Ошибка обработки аккаунта {account.Username}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Ошибка в AutoConfirmationService", ex);
            }
        }

        private async Task ProcessAccountAsync(SteamAccount account)
        {
            var authenticator = new SteamAuthenticator(account.SharedSecret);
            var confirmationService = new ConfirmationService(account, authenticator, _settingsManager);

            try
            {
                var confirmations = await confirmationService.GetConfirmationsAsync();

                if (confirmations == null || confirmations.Count == 0)
                    return;

                foreach (var confirmation in confirmations)
                {
                    bool shouldConfirm = false;

                    // Проверяем тип подтверждения
                    if (account.AutoTrade && confirmation.IntType == 2) // Trade
                    {
                        shouldConfirm = true;
                        AppLogger.Info($"[{account.Username}] Автоподтверждение трейда: {confirmation.Id}");
                    }
                    else if (account.AutoMarket && confirmation.IntType == 3) // Market
                    {
                        shouldConfirm = true;
                        AppLogger.Info($"[{account.Username}] Автоподтверждение маркета: {confirmation.Id}");
                    }

                    if (shouldConfirm)
                    {
                        bool success = await confirmationService.AcceptConfirmationAsync(confirmation);
                        if (success)
                        {
                            AppLogger.Info($"[{account.Username}] Подтверждение {confirmation.Id} принято");
                        }
                        else
                        {
                            AppLogger.Warn($"[{account.Username}] Не удалось принять подтверждение {confirmation.Id}");
                        }

                        // Небольшая задержка между подтверждениями
                        await Task.Delay(1000);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[{account.Username}] Ошибка автоподтверждения", ex);

                // Если ошибка авторизации - пробуем обновить сессию
                if (ex.Message.Contains("needauth") || ex.Message.Contains("401") || ex.Message.Contains("Unauthorized"))
                {
                    AppLogger.Info($"[{account.Username}] Попытка обновить сессию...");
                    await SessionRefreshService.RefreshSessionAsync(account);
                }
            }
            finally
            {
                confirmationService?.Dispose();
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
