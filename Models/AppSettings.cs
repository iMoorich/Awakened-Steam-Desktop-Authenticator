using System.IO;
using Newtonsoft.Json;

namespace SteamGuard
{
    /// <summary>
    /// Настройки приложения
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Стандартная группа при добавлении аккаунта
        /// </summary>
        public string DefaultGroup { get; set; } = Constants.DefaultGroup;

        /// <summary>
        /// Автоматический ввод 2FA кода
        /// </summary>
        public bool Auto2FA { get; set; } = false;

        /// <summary>
        /// Скрывать логины (показывать только первые и последние символы)
        /// </summary>
        public bool HideLogins { get; set; } = false;

        /// <summary>
        /// Язык интерфейса (ru, en)
        /// </summary>
        public string Language { get; set; } = "ru";

        /// <summary>
        /// Обязательное использование прокси (блокировать запросы если прокси недоступен)
        /// По умолчанию TRUE для безопасности - если настроен прокси, запросы без него блокируются
        /// </summary>
        public bool RequireProxy { get; set; } = true;

        /// <summary>
        /// Глобальный прокси (применяется ко всем аккаунтам без своего прокси)
        /// </summary>
        public string? GlobalProxy { get; set; } = null;

        /// <summary>
        /// Список прокси
        /// </summary>
        public List<ProxySettings> Proxies { get; set; } = new List<ProxySettings>();
    }

    /// <summary>
    /// Настройки прокси
    /// </summary>
    public class ProxySettings
    {
        /// <summary>
        /// Название прокси
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Адрес прокси (host:port)
        /// </summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>
        /// Логин для прокси (если есть)
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Пароль для прокси (если есть)
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Активен ли прокси
        /// </summary>
        public bool IsActive { get; set; } = false;
    }

    /// <summary>
    /// Менеджер настроек
    /// </summary>
    public class SettingsManager
    {
        private readonly string _settingsFile;
        private AppSettings _settings;

        public AppSettings Settings => _settings;

        public SettingsManager(string settingsFile = "app_settings.json")
        {
            // Сохраняем рядом с exe файлом
            var exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            _settingsFile = Path.Combine(exeDir, settingsFile);
            _settings = LoadSettings();
        }

        private AppSettings LoadSettings()
        {
            if (File.Exists(_settingsFile))
            {
                try
                {
                    string json = File.ReadAllText(_settingsFile);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();

                    // Фильтруем пустые прокси при загрузке
                    settings.Proxies = settings.Proxies
                        .Where(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Address))
                        .ToList();

                    return settings;
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Ошибка загрузки настроек из {_settingsFile}", ex);
                    return new AppSettings();
                }
            }
            return new AppSettings();
        }

        public void SaveSettings()
        {
            string json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
            File.WriteAllText(_settingsFile, json);
        }

        public void SetDefaultGroup(string groupName)
        {
            _settings.DefaultGroup = groupName;
            SaveSettings();
        }

        public void AddProxy(ProxySettings proxy)
        {
            _settings.Proxies.Add(proxy);
            SaveSettings();
        }

        public void RemoveProxy(string name)
        {
            _settings.Proxies.RemoveAll(p => p.Name == name);
            SaveSettings();
        }
    }
}
