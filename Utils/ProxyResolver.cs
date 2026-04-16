using System.Linq;

namespace SteamGuard
{
    /// <summary>
    /// Утилита для определения эффективного прокси для аккаунта
    /// </summary>
    public static class ProxyResolver
    {
        /// <summary>
        /// Определяет какой прокси использовать для аккаунта
        /// </summary>
        /// <param name="account">Аккаунт Steam</param>
        /// <param name="settingsManager">Менеджер настроек</param>
        /// <returns>Прокси для использования или null если прокси не настроен</returns>
        public static MaFileProxy? GetEffectiveProxy(SteamAccount account, SettingsManager? settingsManager)
        {
            // Приоритет 1: Прокси аккаунта (если указан - всегда используется)
            if (account.Proxy?.Data != null && !string.IsNullOrEmpty(account.Proxy.Data.Address))
            {
                AppLogger.Debug($"Using account-specific proxy: {account.Proxy.Data.Address}:{account.Proxy.Data.Port}");
                return account.Proxy;
            }

            // Приоритет 2: Глобальный прокси (только если активен)
            if (settingsManager != null && !string.IsNullOrEmpty(settingsManager.Settings.GlobalProxy))
            {
                var globalProxyName = settingsManager.Settings.GlobalProxy;
                var globalProxySettings = settingsManager.Settings.Proxies.FirstOrDefault(p => p.Name == globalProxyName);

                // ВАЖНО: Используем глобальный прокси только если он активен
                if (globalProxySettings != null && globalProxySettings.IsActive && !string.IsNullOrEmpty(globalProxySettings.Address))
                {
                    var parts = globalProxySettings.Address.Split(':');
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int port))
                    {
                        var proxy = new MaFileProxy
                        {
                            Id = 1,
                            Data = new ProxyData
                            {
                                Protocol = 0,
                                Address = parts[0],
                                Port = port,
                                Username = globalProxySettings.Username,
                                Password = globalProxySettings.Password,
                                AuthEnabled = !string.IsNullOrEmpty(globalProxySettings.Username)
                            }
                        };
                        AppLogger.Debug($"Using active global proxy: {parts[0]}:{port}");
                        return proxy;
                    }
                }
                else if (globalProxySettings != null && !globalProxySettings.IsActive)
                {
                    AppLogger.Debug($"Global proxy '{globalProxyName}' is not active, skipping");
                }
            }

            // Прокси не настроен
            AppLogger.Debug("No proxy configured for this account");
            return null;
        }

        /// <summary>
        /// Проверяет, настроен ли прокси для аккаунта
        /// </summary>
        public static bool HasProxy(SteamAccount account, SettingsManager? settingsManager)
        {
            return GetEffectiveProxy(account, settingsManager) != null;
        }
    }
}
