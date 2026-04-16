using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Text;
using System.Net.Http;

namespace SteamGuard
{
    public partial class MainForm : Form
    {
        private WebView2? webView;
        private AccountManager _accountManager;
        private SettingsManager _settingsManager;
        private SteamAuthenticator? _authenticator;
        private ConfirmationService? _confirmationService;
        private TradeService? _tradeService;
        private MarketService? _marketService;
        private WalletService? _walletService;
        private AutoConfirmationService? _autoConfirmationService;
        private System.Windows.Forms.Timer? _codeTimer;
        private int _timeLeft = 30;
        private readonly string _mafileDirectory;

        public MainForm()
        {
            InitializeComponent();

            // Очистка старых логов при запуске
            AppLogger.CleanOldLogs(7);
            AppLogger.Info("Application started", "MainForm");

            _mafileDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Constants.MaFileDirectory);

            // Создаём папку mafile если её нет
            if (!Directory.Exists(_mafileDirectory))
            {
                Directory.CreateDirectory(_mafileDirectory);
            }

            _accountManager = new AccountManager(_mafileDirectory);
            _accountManager.LoadAccounts();
            _settingsManager = new SettingsManager();

            // Инициализируем сервис автоподтверждения
            _autoConfirmationService = new AutoConfirmationService(_accountManager, _settingsManager);
            _autoConfirmationService.Start();

            InitializeCodeTimer();
        }

        private void InitializeComponent()
        {
            this.Text = "Awakened Steam Desktop Authenticator";
            this.Size = new Size(Constants.WindowWidth, Constants.WindowHeight);
            this.MinimumSize = new Size(Constants.WindowWidth, Constants.WindowHeight);
            this.MaximumSize = new Size(Constants.WindowWidth, Constants.WindowHeight);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.Black;
            this.Padding = new Padding(0);

            // Load icon
            try
            {
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Constants.IconFileName);
                if (File.Exists(iconPath))
                    this.Icon = new System.Drawing.Icon(iconPath);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Не удалось загрузить иконку: {ex.Message}");
            }

            webView = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.Black
            };

            webView.NavigationCompleted += WebView_NavigationCompleted;
            webView.WebMessageReceived += WebView_WebMessageReceived;
            webView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;

            this.Controls.Add(webView);
            this.Load += MainForm_Load;
        }

        private void WebView_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.IsSuccess && webView?.CoreWebView2 != null)
            {
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            }
        }

        private async void MainForm_Load(object? sender, EventArgs e)
        {
            try
            {
                await webView?.EnsureCoreWebView2Async(null)!;

                string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Constants.IndexHtmlPath);

                if (File.Exists(htmlPath))
                {
                    webView!.Source = new Uri(htmlPath);
                }
                else
                {
                    MessageBox.Show($"Не найден файл index.html", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                // Initialize services after accounts are set
                InitializeServices();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            UpdateAccountsList();

            if (_accountManager.CurrentAccount != null && _authenticator != null)
            {
                UpdateCodes();
            }
        }

        private async void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string json = e.WebMessageAsJson;

            try
            {
                var message = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                string type = message?["type"]?.ToString() ?? "";

                switch (type)
                {
                    case "RefreshCodes":
                        UpdateCodes();
                        break;

                    case "SwitchAccount":
                        string accountName = message?["accountName"]?.ToString() ?? "";
                        SwitchAccount(accountName);
                        break;

                    case "GenerateCodeForAccount":
                        string targetAccountName = message?["accountName"]?.ToString() ?? "";
                        GenerateCodeForAccount(targetAccountName);
                        break;

                    case "OpenUrl":
                        string url = message?["url"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(url))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = url,
                                UseShellExecute = true
                            });
                        }
                        break;

                    case "RefreshAccounts":
                        UpdateAccountsList();
                        break;

                    case "AddAccount":
                        var accountObj = message?["account"];
                        if (accountObj != null)
                        {
                            var account = JsonConvert.DeserializeObject<SteamAccount>(accountObj.ToString() ?? "");
                            if (account != null)
                            {
                                _accountManager.AddAccount(account);
                                UpdateAccountsList();
                                SendToJS("AccountAdded", new { });
                            }
                        }
                        break;

                    case "ImportMaFile":
                        string content = message?["content"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(content))
                        {
                            ImportMaFile(content);
                        }
                        break;

                    case "RefreshConfirmations":
                        _ = RefreshConfirmationsAsync();
                        break;

                    case "AcceptAllConfirmations":
                        _ = AcceptAllConfirmationsAsync();
                        break;

                    case "AcceptConfirmation":
                        string confId = message?["confirmationId"]?.ToString() ?? "";
                        _ = AcceptConfirmationAsync(confId);
                        break;

                    case "DenyConfirmation":
                        string denyId = message?["confirmationId"]?.ToString() ?? "";
                        _ = DenyConfirmationAsync(denyId);
                        break;

                    case "RefreshTrades":
                        _ = RefreshTradesAsync();
                        break;

                    case "AcceptTrade":
                        string tradeId = message?["tradeId"]?.ToString() ?? "";
                        _ = AcceptTradeAsync(tradeId);
                        break;

                    case "DeclineTrade":
                        string declineTradeId = message?["tradeId"]?.ToString() ?? "";
                        _ = DeclineTradeAsync(declineTradeId);
                        break;

                    case "RefreshMarket":
                        _ = RefreshMarketAsync();
                        break;

                    case "CancelListing":
                        string listingId = message?["listingId"]?.ToString() ?? "";
                        _ = CancelListingAsync(listingId);
                        break;

                    case "MinimizeWindow":
                        this.WindowState = FormWindowState.Minimized;
                        break;

                    case "CloseWindow":
                        this.Close();
                        break;

                    case "DragWindow":
                        int deltaX = Convert.ToInt32(message?["deltaX"] ?? 0);
                        int deltaY = Convert.ToInt32(message?["deltaY"] ?? 0);
                        this.Location = new Point(this.Location.X + deltaX, this.Location.Y + deltaY);
                        break;

                    case "OpenAccountSettings":
                        string settingsAccount = message?["accountName"]?.ToString() ?? "";
                        OpenAccountSettingsDialog(settingsAccount);
                        break;

                    case "UpdateAccountSettings":
                        HandleUpdateAccountSettings(message!);
                        break;

                    case "AddAccountDialog":
                        ShowAddAccountDialog();
                        break;

                    case "SubmitLoginCredentials":
                        HandleLoginCredentials(message!);
                        break;

                    case "SubmitEmailCode":
                        HandleEmailCode(message!);
                        break;

                    case "SubmitGuardCode":
                        HandleGuardCode(message!);
                        break;

                    case "SubmitPassword":
                        await HandlePasswordSubmit(message!);
                        break;

                    case "CreateGroupDialog":
                        ShowCreateGroupDialog();
                        break;

                    case "CreateGroup":
                        HandleCreateGroup(message!);
                        break;

                    case "OpenSettings":
                        ShowSettingsDialog();
                        break;

                    case "SaveSettings":
                        HandleSaveSettings(message!);
                        break;

                    case "CopyRevocationCode":
                        string revCode = message?["code"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(revCode))
                        {
                            this.Invoke(() => Clipboard.SetText(revCode));
                        }
                        break;

                    case "GetGroups":
                        SendGroupsToJS();
                        break;

                    case "RemoveAccount":
                        string removeAccount = message?["accountName"]?.ToString() ?? "";
                        RemoveAccount(removeAccount);
                        break;

                    case "RefreshSession":
                        string sessionAccount = message?["accountName"]?.ToString() ?? "";
                        RefreshSession(sessionAccount);
                        break;

                    case "GetWalletBalance":
                        string walletAccount = message?["accountName"]?.ToString() ?? "";
                        await GetWalletBalance(walletAccount);
                        break;

                    case "ToggleFavorite":
                        string favoriteAccount = message?["accountName"]?.ToString() ?? "";
                        ToggleFavorite(favoriteAccount);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void InitializeCodeTimer()
        {
            _codeTimer = new System.Windows.Forms.Timer();
            _codeTimer.Interval = Constants.CodeTimerIntervalMs;
            _codeTimer.Tick += CodeTimer_Tick;
            _codeTimer.Start();
        }

        private void CodeTimer_Tick(object? sender, EventArgs e)
        {
            _timeLeft--;

            if (_timeLeft <= 0)
            {
                _timeLeft = Constants.TotpPeriodSeconds;
                UpdateCodes();
            }

            _ = ExecuteScriptAsync($"updateTimer({_timeLeft})");
        }

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

            SendToJS("UpdateAccounts", new {
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
                    SendToJS("CodeGenerated", new { accountName = accountName, code = code });
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

        /// <summary>
        /// Проверить, действительна ли сессия
        /// </summary>
        private async Task<bool> IsSessionValidAsync(SteamAccount account)
        {
            // Сессия недействительна, если нет токенов
            if (string.IsNullOrEmpty(account.Session?.AccessToken) ||
                string.IsNullOrEmpty(account.Session?.SteamLoginSecure))
            {
                return false;
            }

            // Пробуем быстрый запрос к Steam для проверки сессии
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

                // Простой запрос для проверки сессии
                var response = await client.GetAsync("https://steamcommunity.com/actions/GetNotificationCounts");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Попытаться обновить сессию
        /// </summary>
        private async Task<bool> TryRefreshSessionAsync(SteamAccount account)
        {
            // 1. Пробуем через RefreshToken
            if (await SessionRefreshService.RefreshSessionAsync(account))
            {
                account.HasSession = true;
                account.SteamId = account.Session.SteamId > 0 ? account.Session.SteamId : account.SteamId;
                _accountManager.SaveAccountSettings(account);
                UpdateAccountsList();
                return true;
            }

            // 2. Если нет RefreshToken или он истёк - пробуем полную авторизацию
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
                        SteamId = (long)loginResult.SteamId
                    };
                    account.SteamId = (long)loginResult.SteamId;
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

            // Обновляем список аккаунтов в UI
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

                // Шаг 2: Проверяем валидность сессии
                bool needsSessionRefresh = false;

                if (!account.HasSession)
                {
                    needsSessionRefresh = true;
                    AppLogger.Warn($"Нет активной сессии для {accountName}", "WalletService");
                }
                else
                {
                    // Проверяем, не устарела ли сессия
                    using var walletService = new WalletService(account, _settingsManager);
                    var walletInfo = await walletService.GetWalletInfoAsync();

                    if (walletInfo != null && walletInfo.IsSessionExpired)
                    {
                        needsSessionRefresh = true;
                        AppLogger.Warn($"Сессия устарела для {accountName}");
                    }
                    else if (walletInfo != null && !walletInfo.IsSessionExpired)
                    {
                        // Шаг 7: Сессия валидна - выводим баланс
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

                // Шаг 3: Если сессия не валидна - получаем её
                if (needsSessionRefresh)
                {
                    // Шаг 4: Проверяем наличие пароля
                    if (string.IsNullOrEmpty(account.Password))
                    {
                        // Нет пароля - показываем модалку
                        _pendingLoginAccount = accountName;
                        _pendingLoginForWallet = true;
                        SendToJS("RequestPassword", new { accountName });
                        return;
                    }
                    else
                    {
                        // Есть пароль - обновляем сессию в фоне
                        AppLogger.Info($"Обновление сессии в фоне для {accountName}...");
                        await RefreshSessionForWallet(accountName);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ошибка получения баланса для {accountName}", ex);
                SendToJS("WalletBalanceError", new { accountName, message = ex.Message });
            }
        }

        private async Task RefreshConfirmationsAsync()
        {
            if (_confirmationService == null || _accountManager.CurrentAccount == null) return;

            var account = _accountManager.CurrentAccount;

            try
            {
                var confirmations = await _confirmationService.GetConfirmationsAsync();
                SendToJS("UpdateConfirmations", new { confirmations = confirmations });
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Ошибка получения подтверждений: {ex.Message}");

                // Если ошибка связана с авторизацией, пробуем обновить сессию
                if (ex.Message.Contains("needauth") || ex.Message.Contains("401") || ex.Message.Contains("Unauthorized"))
                {
                    try
                    {
                        AppLogger.Info("Обнаружена ошибка авторизации, обновляем сессию...");
                        await SessionRefreshService.RefreshSessionAsync(account);

                        // Пересоздаем сервис с новой сессией
                        _confirmationService?.Dispose();
                        _confirmationService = new ConfirmationService(account, _authenticator, _settingsManager);

                        // Повторяем запрос
                        var confirmations = await _confirmationService.GetConfirmationsAsync();
                        SendToJS("UpdateConfirmations", new { confirmations = confirmations });
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
                    SendToJS("ConfirmationAccepted", new { success = success });
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
                    SendToJS("ConfirmationDenied", new { success = success });
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

            var account = _accountManager.CurrentAccount;

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

            var account = _accountManager.CurrentAccount;

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

        // ===== ДИАЛОГИ =====

        /// <summary>
        /// Открыть настройки аккаунта (двойной клик)
        /// </summary>
        private void OpenAccountSettingsDialog(string accountName)
        {
            var account = _accountManager.Accounts.FirstOrDefault(a => a.Username == accountName);
            if (account == null) return;

            var groups = _accountManager.GetGroups();
            var proxies = _settingsManager.Settings.Proxies ?? new List<ProxySettings>();
            
            SendToJS("ShowAccountSettings", new
            {
                account = new
                {
                    username = account.Username,
                    group = account.Group,
                    hasSession = account.HasSession,
                    autoTrade = account.AutoTrade,
                    autoMarket = account.AutoMarket,
                    proxy = account.Proxy?.Data?.Address != null ? $"{account.Proxy.Data.Address}:{account.Proxy.Data.Port}" : "",
                    revocationCode = account.RevocationCode ?? ""
                },
                groups = groups,
                proxies = proxies.Select(p => new { name = p.Name ?? "", address = p.Address ?? "" }).ToList()
            });
        }

        /// <summary>
        /// Обновить настройки аккаунта
        /// </summary>
        private void HandleUpdateAccountSettings(Dictionary<string, object> message)
        {
            try
            {
                string accountName = message["accountName"]?.ToString() ?? "";
                AppLogger.Info($"HandleUpdateAccountSettings called for account: {accountName}");

                var account = _accountManager.Accounts.FirstOrDefault(a => a.Username == accountName);
                if (account == null)
                {
                    AppLogger.Warn($"Account not found: {accountName}");
                    return;
                }

                account.Group = message["group"]?.ToString() ?? account.Group;
                account.AutoTrade = Convert.ToBoolean(message["autoTrade"] ?? false);
                account.AutoMarket = Convert.ToBoolean(message["autoMarket"] ?? false);

                // Обновляем прокси
                string proxyStr = message["proxy"]?.ToString() ?? "";
                AppLogger.Info($"Proxy string received: '{proxyStr}'");

                if (!string.IsNullOrEmpty(proxyStr) && proxyStr != "Без прокси")
                {
                    string proxyAddress = proxyStr;
                    string? proxyUsername = null;
                    string? proxyPassword = null;

                    // Проверяем, это адрес или название прокси
                    if (!proxyStr.Contains(":") || !int.TryParse(proxyStr.Split(':').Last(), out _))
                    {
                        // Это название прокси, ищем в настройках
                        var proxySettings = _settingsManager.Settings.Proxies.FirstOrDefault(p => p.Name == proxyStr);
                        if (proxySettings != null && !string.IsNullOrEmpty(proxySettings.Address))
                        {
                            proxyAddress = proxySettings.Address;
                            proxyUsername = proxySettings.Username;
                            proxyPassword = proxySettings.Password;
                        }
                        else
                        {
                            AppLogger.Warn($"Proxy '{proxyStr}' not found in settings");
                            account.Proxy = null;
                            goto SaveAccount;
                        }
                    }

                    // Парсим адрес прокси
                    var parts = proxyAddress.Split(':');
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int port))
                    {
                        account.Proxy = new MaFileProxy
                        {
                            Id = 1,
                            Data = new ProxyData
                            {
                                Protocol = 0,
                                Address = parts[0],
                                Port = port,
                                Username = proxyUsername,
                                Password = proxyPassword,
                                AuthEnabled = !string.IsNullOrEmpty(proxyUsername)
                            }
                        };
                        AppLogger.Debug($"Proxy set for {accountName}: {parts[0]}:{port}");
                    }
                    else
                    {
                        AppLogger.Warn($"Invalid proxy address format: {proxyAddress}");
                        account.Proxy = null;
                    }
                }
                else
                {
                    account.Proxy = null;
                    AppLogger.Debug($"Proxy removed for {accountName}");
                }

                SaveAccount:

                _accountManager.SaveAccountSettings(account);
                UpdateAccountsList();

                // Перезапускаем сервис автоподтверждения для применения изменений
                _autoConfirmationService?.Stop();
                _autoConfirmationService?.Start();

                SendToJS("AccountSettingsSaved", new { success = true });
            }
            catch (Exception ex)
            {
                SendToJS("Error", new { message = ex.Message });
            }
        }

        /// <summary>
        /// Показать диалог добавления аккаунта
        /// </summary>
        private void ShowAddAccountDialog()
        {
            var groups = _accountManager.GetGroups();
            SendToJS("ShowAddAccountDialog", new {
                groups = groups,
                defaultGroup = _settingsManager.Settings.DefaultGroup
            });
        }

        /// <summary>
        /// Обработать логин/пароль
        /// </summary>
        private async void HandleLoginCredentials(Dictionary<string, object> message)
        {
            try
            {
                string login = message["login"]?.ToString() ?? "";
                string password = message["password"]?.ToString() ?? "";
                string group = message["group"]?.ToString() ?? _settingsManager.Settings.DefaultGroup;

                if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
                {
                    SendToJS("Error", new { });
                    return;
                }

                // Сохраняем данные для последующих шагов
                _newAccountLogin = login;
                _newAccountPassword = password;
                _newAccountGroup = group;
                _accountLinker = new SteamGuardEnrollment();

                // Начинаем авторизацию
                var (success, error, needsEmailCode, confirmType) = await _accountLinker.StartLoginAsync(login, password);

                if (!success)
                {
                    SendToJS("Error", new { message = $"Ошибка авторизации: {error}" });
                    return;
                }

                // Запрашиваем код с почты или email
                if (needsEmailCode || confirmType == (int)AuthConfirmationType.EmailCode || confirmType == (int)AuthConfirmationType.EmailConfirmation)
                {
                    SendToJS("RequestEmailCode", new { });
                }
                else
                {
                    SendToJS("Error", new { message = $"Неподдерживаемый тип подтверждения: {confirmType}" });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error in HandleLoginCredentials", ex);
                SendToJS("Error", new { message = ex.Message });
            }
        }

        /// <summary>
        /// Обработать код с почты для авторизации
        /// </summary>
        private async void HandleEmailCode(Dictionary<string, object> message)
        {
            try
            {
                string code = message["code"]?.ToString() ?? "";

                // Если это обновление сессии
                if (!string.IsNullOrEmpty(_pendingLoginAccount))
                {
                    SubmitEmailCodeForSession(code);
                    return;
                }

                // Если это добавление нового аккаунта
                if (_accountLinker != null)
                {
                    // Подтверждаем email код
                    var (emailSuccess, emailError) = await _accountLinker.SubmitEmailCodeAsync(code);

                    if (!emailSuccess)
                    {
                        SendToJS("Error", new { message = $"Ошибка подтверждения email: {emailError}" });
                        return;
                    }

                    // Добавляем аутентификатор
                    var (success, error, sharedSecret, revocationCode, uri, serverTime, tokenGid, identitySecret, secret1, confirmType)
                        = await _accountLinker.AddAuthenticatorAsync();

                    if (!success)
                    {
                        SendToJS("Error", new { message = $"Не удалось добавить аутентификатор: {error}" });
                        return;
                    }

                    // Сохраняем данные аутентификатора
                    _newAccountAuthData = new AddAuthenticatorResult
                    {
                        SharedSecret = sharedSecret ?? "",
                        IdentitySecret = identitySecret,
                        RevocationCode = revocationCode ?? "",
                        Uri = uri ?? "",
                        ServerTime = serverTime,
                        TokenGid = tokenGid ?? "",
                        Secret1 = secret1,
                        ConfirmType = confirmType
                    };

                    // Запрашиваем код подтверждения
                    SendToJS("RequestGuardCode", new { });
                }
                else
                {
                    SendToJS("Error", new { });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error in HandleEmailCode", ex);
                SendToJS("Error", new { message = ex.Message });
            }
        }

        /// <summary>
        /// Обработать код для Steam Guard
        /// </summary>
        private async void HandleGuardCode(Dictionary<string, object> message)
        {
            try
            {
                string confirmationCode = message["code"]?.ToString() ?? "";

                if (_accountLinker == null || _newAccountAuthData == null)
                {
                    SendToJS("Error", new { });
                    return;
                }

                // Финализируем аутентификатор
                var (success, error) = await _accountLinker.FinalizeAuthenticatorAsync(confirmationCode);

                if (!success)
                {
                    SendToJS("Error", new { message = $"Не удалось финализировать аутентификатор: {error}" });
                    return;
                }

                // Создаем аккаунт с реальными данными
                var newAccount = new SteamAccount
                {
                    Username = _newAccountLogin ?? "",
                    Group = _newAccountGroup ?? _settingsManager.Settings.DefaultGroup,
                    SharedSecret = _newAccountAuthData.SharedSecret ?? "",
                    IdentitySecret = _newAccountAuthData.IdentitySecret != null
                        ? Convert.ToBase64String(_newAccountAuthData.IdentitySecret)
                        : "",
                    RevocationCode = _newAccountAuthData.RevocationCode ?? "",
                    SteamId = (long)_accountLinker.SteamId,
                    DeviceId = _accountLinker.DeviceId,
                    HasSession = true
                };

                _accountManager.AddAccount(newAccount);
                UpdateAccountsList();

                // Показываем R-код
                SendToJS("ShowRevocationCode", new
                {
                    code = newAccount.RevocationCode,
                    account = newAccount.Username,
                    message = "Аккаунт успешно добавлен! Сохраните R-код для восстановления."
                });

                // Очищаем временные данные
                _accountLinker?.Dispose();
                _accountLinker = null;
                _newAccountLogin = null;
                _newAccountPassword = null;
                _newAccountGroup = null;
                _newAccountAuthData = null;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error in HandleGuardCode", ex);
                SendToJS("Error", new { message = ex.Message });
            }
        }

        /// <summary>
        /// Обработка введённого пароля для обновления сессии
        /// </summary>
        private async Task HandlePasswordSubmit(Dictionary<string, object> message)
        {
            try
            {
                string password = message["password"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(_pendingLoginAccount))
                {
                    SendToJS("Error", new { });
                    return;
                }

                var account = _accountManager.Accounts.FirstOrDefault(a => a.Username == _pendingLoginAccount);
                if (account == null)
                {
                    SendToJS("Error", new { });
                    _pendingLoginAccount = null;
                    return;
                }

                // Пробуем авторизоваться с введённым паролем
                var loginResult = await SessionLoginService.FullLoginAsync(
                    account.Username,
                    password,
                    account.SharedSecret
                );

                if (loginResult.Success)
                {
                    // Сохраняем сессию
                    account.Session = new MaFileSession
                    {
                        AccessToken = loginResult.AccessToken ?? "",
                        RefreshToken = loginResult.RefreshToken ?? "",
                        SteamLoginSecure = loginResult.SteamLoginSecure ?? "",
                        SessionId = loginResult.SessionId ?? "",
                        SteamId = (long)loginResult.SteamId
                    };
                    account.SteamId = (long)loginResult.SteamId;
                    account.HasSession = true;

                    // Сохраняем пароль в mafile
                    account.Password = password;
                    _accountManager.SaveAccountSettings(account);

                    UpdateAccountsList();

                    if (_pendingLoginForWallet)
                    {
                        // Если авторизация была для баланса - запрашиваем баланс
                        _pendingLoginForWallet = false;
                        _pendingLoginAccount = null;
                        SendToJS("PasswordSuccess", new { }); // Пустое сообщение, чтобы закрыть модалку
                        await GetWalletBalance(account.Username);
                    }
                    else
                    {
                        // Обычное обновление сессии - показываем уведомление
                        SendToJS("PasswordSuccess", new { });
                        _pendingLoginAccount = null;
                    }
                }
                else
                {
                    if (loginResult.NeedsEmailCode)
                    {
                        // Нужен код с почты
                        SendToJS("RequestEmailCode", new { });
                    }
                    else
                    {
                        // Отправляем ошибку, но не закрываем модалку
                        SendToJS("PasswordError", new { message = loginResult.Error ?? "Неверный пароль. Попробуйте ещё раз." });
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error in HandlePasswordSubmit", ex);
                SendToJS("Error", new { message = ex.Message });
                _pendingLoginAccount = null;
            }
        }

        /// <summary>
        /// Генерация R-кода (заглушка)
        /// </summary>
        private string GenerateRevocationCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 5)
                .Select(s => s[random.Next(s.Length)]).ToArray()) + "-" +
                   new string(Enumerable.Repeat(chars, 5)
                .Select(s => s[random.Next(s.Length)]).ToArray()) + "-" +
                   new string(Enumerable.Repeat(chars, 5)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        /// <summary>
        /// Показать диалог создания группы
        /// </summary>
        private void ShowCreateGroupDialog()
        {
            SendToJS("ShowCreateGroupDialog", new { });
        }

        /// <summary>
        /// Обработать создание группы
        /// </summary>
        private void HandleCreateGroup(Dictionary<string, object> message)
        {
            try
            {
                string groupName = message["groupName"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(groupName))
                {
                    SendToJS("Error", new { });
                    return;
                }

                // Группы теперь автоматически формируются из аккаунтов
                UpdateAccountsList();
                SendToJS("GroupCreated", new { success = true, groupName = groupName });
            }
            catch (Exception ex)
            {
                SendToJS("Error", new { message = ex.Message });
            }
        }

        /// <summary>
        /// Показать диалог настроек
        /// </summary>
        private void ShowSettingsDialog()
        {
            var groups = _accountManager.GetGroups();
            SendToJS("ShowSettingsDialog", new
            {
                defaultGroup = _settingsManager.Settings.DefaultGroup,
                groups = groups,
                auto2FA = _settingsManager.Settings.Auto2FA,
                hideLogins = _settingsManager.Settings.HideLogins,
                language = _settingsManager.Settings.Language,
                proxies = _settingsManager.Settings.Proxies.Select(p => new
                {
                    name = p.Name ?? "",
                    address = p.Address ?? "",
                    username = p.Username ?? "",
                    password = p.Password ?? "",
                    isActive = p.IsActive
                }).ToList()
            });
        }

        /// <summary>
        /// Сохранить настройки
        /// </summary>
        private void HandleSaveSettings(Dictionary<string, object> message)
        {
            try
            {
                string defaultGroup = message["defaultGroup"]?.ToString() ?? "";
                bool hideLogins = Convert.ToBoolean(message["hideLogins"] ?? false);
                string language = message["language"]?.ToString() ?? "ru";

                // Обновляем все настройки перед сохранением
                _settingsManager.Settings.DefaultGroup = defaultGroup;
                _settingsManager.Settings.HideLogins = hideLogins;
                _settingsManager.Settings.Language = language;

                // Handle proxies
                if (message.TryGetValue("proxies", out var proxiesObj) && proxiesObj != null)
                {
                    var proxiesJson = proxiesObj.ToString();
                    AppLogger.Info($"Proxies JSON received: {proxiesJson}");
                    var proxies = JsonConvert.DeserializeObject<List<ProxySettings>>(proxiesJson ?? "[]");

                    // Фильтруем пустые прокси
                    _settingsManager.Settings.Proxies = (proxies ?? new List<ProxySettings>())
                        .Where(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Address))
                        .ToList();

                    AppLogger.Info($"Proxies count after deserialization and filtering: {_settingsManager.Settings.Proxies.Count}");

                    // Устанавливаем GlobalProxy на основе активного прокси
                    var activeProxy = _settingsManager.Settings.Proxies.FirstOrDefault(p => p.IsActive);
                    _settingsManager.Settings.GlobalProxy = activeProxy?.Name;
                }

                // Сохраняем все настройки одним вызовом
                _settingsManager.SaveSettings();
                AppLogger.Info("Settings saved successfully");

                // Обновляем список аккаунтов чтобы индикаторы прокси обновились
                UpdateAccountsList();

                SendToJS("SettingsSaved", new { success = true });
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error saving settings", ex);
                SendToJS("Error", new { message = ex.Message });
            }
        }

        /// <summary>
        /// Отправить список групп в JS
        /// </summary>
        private void SendGroupsToJS()
        {
            var groups = _accountManager.GetGroups();
            SendToJS("ApplyGroups", new { groups = groups });
        }

        /// <summary>
        /// Удалить аккаунт
        /// </summary>
        private void RemoveAccount(string accountName)
        {
            var account = _accountManager.Accounts.FirstOrDefault(a => a.Username == accountName);
            if (account != null)
            {

                _accountManager.RemoveAccount(account);

                // Если удалили текущий аккаунт - переключиться на первый
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

        /// <summary>
        /// Обновить сессию аккаунта
        /// </summary>
        private async Task RefreshSession(string accountName, bool silentForWallet = false)
        {
            var account = _accountManager.Accounts.FirstOrDefault(a => a.Username == accountName);
            if (account != null)
            {
                try
                {
                    // 1. Пробуем через RefreshToken
                    if (await SessionRefreshService.RefreshSessionAsync(account))
                    {
                        account.HasSession = true;
                        _accountManager.SaveAccountSettings(account);
                        UpdateAccountsList();

                        if (silentForWallet)
                        {
                            // Повторно запрашиваем баланс после обновления сессии
                            await GetWalletBalance(accountName);
                        }
                        else
                        {
                            SendToJS("SessionRefreshed", new { });
                        }
                        return;
                    }

                    // 2. Если нет RefreshToken - пробуем полную авторизацию
                    var loginResult = await SessionLoginService.FullLoginAsync(
                        account.Username,
                        account.Password ?? "",
                        account.SharedSecret
                    );

                    if (loginResult.Success)
                    {
                        // Сохраняем сессию в аккаунт
                        account.Session = new MaFileSession
                        {
                            AccessToken = loginResult.AccessToken ?? "",
                            RefreshToken = loginResult.RefreshToken ?? "",
                            SteamLoginSecure = loginResult.SteamLoginSecure ?? "",
                            SessionId = loginResult.SessionId ?? "",
                            SteamId = (long)loginResult.SteamId
                        };
                        account.SteamId = (long)loginResult.SteamId;
                        account.HasSession = true;
                        _accountManager.SaveAccountSettings(account);
                        UpdateAccountsList();

                        if (silentForWallet)
                        {
                            // Повторно запрашиваем баланс после обновления сессии
                            await GetWalletBalance(accountName);
                        }
                        else
                        {
                            SendToJS("SessionRefreshed", new { });
                        }
                    }
                    else if (loginResult.NeedsEmailCode)
                    {
                        // Нужен код с почты - показываем модалку
                        _pendingLoginAccount = accountName;
                        SendToJS("RequestEmailCode", new { });
                    }
                    else if (loginResult.Error == "Нет пароля для авторизации" || string.IsNullOrEmpty(account.Password))
                    {
                        // Запрашиваем пароль через модалку
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

        // Аккаунт ожидающий ввода email кода
        private string? _pendingLoginAccount;
        private bool _pendingLoginForWallet; // Флаг что авторизация для получения баланса

        /// <summary>
        /// Обновить сессию в фоне для получения баланса (когда пароль уже есть)
        /// </summary>
        private async Task RefreshSessionForWallet(string accountName)
        {
            var account = _accountManager.Accounts.FirstOrDefault(a => a.Username == accountName);
            if (account == null) return;

            try
            {
                // 1. Пробуем через RefreshToken
                if (await SessionRefreshService.RefreshSessionAsync(account))
                {
                    account.HasSession = true;
                    _accountManager.SaveAccountSettings(account);
                    UpdateAccountsList();
                    AppLogger.Info($"Сессия обновлена через RefreshToken для {accountName}");

                    // Шаг 7: Получаем баланс после успешного обновления сессии
                    await GetWalletBalance(accountName);
                    return;
                }

                // 2. Если нет RefreshToken - пробуем полную авторизацию с паролем
                var loginResult = await SessionLoginService.FullLoginAsync(
                    account.Username,
                    account.Password ?? "",
                    account.SharedSecret
                );

                if (loginResult.Success)
                {
                    // Шаг 6: Сохраняем сессию в аккаунт
                    account.Session = new MaFileSession
                    {
                        AccessToken = loginResult.AccessToken ?? "",
                        RefreshToken = loginResult.RefreshToken ?? "",
                        SteamLoginSecure = loginResult.SteamLoginSecure ?? "",
                        SessionId = loginResult.SessionId ?? "",
                        SteamId = (long)loginResult.SteamId
                    };
                    account.SteamId = (long)loginResult.SteamId;
                    account.HasSession = true;
                    _accountManager.SaveAccountSettings(account);
                    UpdateAccountsList();
                    AppLogger.Info($"Сессия успешно обновлена для {accountName}");

                    // Шаг 7: Получаем баланс после успешного обновления сессии
                    await GetWalletBalance(accountName);
                }
                else if (loginResult.NeedsEmailCode)
                {
                    // Нужен код с почты - показываем модалку
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

        // Данные для добавления нового аккаунта
        private SteamGuardEnrollment? _accountLinker;
        private string? _newAccountLogin;
        private string? _newAccountPassword;
        private string? _newAccountGroup;
        private AddAuthenticatorResult? _newAccountAuthData;

        /// <summary>
        /// Обработать код с почты для авторизации (при обновлении сессии)
        /// </summary>
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
                        SteamId = (long)loginResult.SteamId
                    };
                    account.SteamId = (long)loginResult.SteamId;
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                this.Capture = false;
                var msg = Message.Create(this.Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
                this.WndProc(ref msg);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            AppLogger.Info("Application closing", "MainForm");

            _codeTimer?.Stop();
            _codeTimer?.Dispose();
            _autoConfirmationService?.Dispose();
            _confirmationService?.Dispose();

            // Запускаем shutdown логгера в фоне без ожидания
            _ = Task.Run(async () => await AppLogger.ShutdownAsync());

            base.OnFormClosing(e);
        }
    }
}
