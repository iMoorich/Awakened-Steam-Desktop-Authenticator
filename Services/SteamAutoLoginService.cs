using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Management;
using System.Text;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace SteamGuard;

public class SteamAutoLoginService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint dwThreadId, EnumThreadDelegate lpfn, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    private const uint WM_GETTEXTLENGTH = 0x000E;
    private const uint WM_GETTEXT = 0x000D;

    private delegate bool EnumThreadDelegate(IntPtr hWnd, IntPtr lParam);

    private bool _isRunning;
    private string? _username;
    private string? _password;
    private string? _guardCode;
    private string? _accountName;
    private int _steamPid; // PID конкретного процесса Steam
    private readonly Action<string>? _onStatusChanged;
    private readonly Action? _onLoginCompleted;
    private DateTime _startTime;

    private enum LoginState
    {
        WaitingForWindow,
        WaitingForCredentialFields,
        EnteringCredentials,
        WaitingFor2FA,
        Entering2FA,
        Completed
    }

    private LoginState _currentState = LoginState.WaitingForWindow;

    public SteamAutoLoginService(Action<string>? onStatusChanged = null, Action? onLoginCompleted = null, int steamPid = 0)
    {
        _onStatusChanged = onStatusChanged;
        _onLoginCompleted = onLoginCompleted;
        _steamPid = steamPid;
    }

    public async void Start(string username, string password, string guardCode, string accountName)
    {
        _username = username;
        _password = password;
        _guardCode = guardCode;
        _accountName = accountName;
        _isRunning = true;
        _currentState = LoginState.WaitingForWindow;
        _startTime = DateTime.Now;

        UpdateStatus("Ожидание окна входа Steam...");

        await Task.Delay(2000);

        UpdateStatus("Поиск окна входа...");

        _ = Task.Run(async () => await MonitorLoop());
    }

    private async Task CloseSteamAsync()
    {
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
    }

    private void StartSteam()
    {
        try
        {
            var steamPath = GetSteamPath();
            if (string.IsNullOrEmpty(steamPath))
            {
                UpdateStatus("Ошибка: Steam не найден");
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(steamPath, "steam.exe"),
                Arguments = "-nofriendsui -vgui -noreactlogin -noverifyfiles -nobootstrapupdate -skipinitialbootstrap -norepairfiles -overridepackageurl -disable-winh264 -language english",
                WorkingDirectory = steamPath,
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            UpdateStatus($"Ошибка запуска Steam: {ex.Message}");
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

    public void Stop()
    {
        _isRunning = false;
    }

    private async Task MonitorLoop()
    {
        UpdateStatus("Ожидание окна входа Steam...");

        while (_isRunning)
        {
            try
            {
                // Проверяем таймаут 30 секунд
                if ((DateTime.Now - _startTime).TotalSeconds > 30)
                {
                    UpdateStatus("Таймаут: не удалось авторизоваться за 30 секунд");
                    _isRunning = false;
                    break;
                }

                var steamProcess = GetSteamProcess();
                if (steamProcess == null)
                {
                    await Task.Delay(100);
                    continue;
                }

                var loginWindow = GetSteamLoginWindow(steamProcess);
                if (loginWindow == null || loginWindow == IntPtr.Zero)
                {
                    await Task.Delay(100);
                    continue;
                }

                // Постоянно удерживаем фокус на окне входа
                SetForegroundWindow(loginWindow.Value);
                BringWindowToTop(loginWindow.Value);

                // Окно найдено
                if (_currentState == LoginState.WaitingForWindow)
                {
                    _currentState = LoginState.WaitingForCredentialFields;
                    UpdateStatus("Окно входа найдено!");
                }

                using var automation = new UIA3Automation();
                var window = automation.FromHandle(loginWindow.Value);
                if (window == null)
                {
                    await Task.Delay(50);
                    continue;
                }

                var inputs = FindElementsByType(window, ControlType.Edit);
                var buttons = FindElementsByType(window, ControlType.Button);

                // Проверяем состояние входа
                if (_currentState == LoginState.WaitingForCredentialFields || _currentState == LoginState.EnteringCredentials)
                {
                    if (inputs.Count >= 2 && buttons.Count >= 1)
                    {
                        if (_currentState == LoginState.WaitingForCredentialFields)
                        {
                            _currentState = LoginState.EnteringCredentials;
                            UpdateStatus("Ввод данных...");

                            try
                            {
                                window.Focus();

                                inputs[0].AsTextBox().Text = _username!;
                                inputs[1].AsTextBox().Text = _password!;
                                buttons[0].AsButton().Invoke();

                                _currentState = LoginState.WaitingFor2FA;
                                UpdateStatus("Ожидание 2FA...");
                            }
                            catch (Exception ex)
                            {
                                UpdateStatus($"Ошибка ввода: {ex.Message}");
                                _currentState = LoginState.WaitingForCredentialFields;
                            }
                        }
                    }
                }

                // Проверяем 2FA (только если НЕ на экране логина)
                if (_currentState == LoginState.WaitingFor2FA || _currentState == LoginState.Entering2FA)
                {
                    // КРИТИЧЕСКИ ВАЖНО: Проверяем что мы НЕ на экране логина
                    // Экран логина: 2 поля Edit + 1 кнопка
                    // Экран 2FA старый: 0 полей Edit + 5 кнопок
                    // Экран 2FA новый: 4-6 полей Edit + НЕ 1 кнопка

                    bool isLoginScreen = (inputs.Count == 2 && buttons.Count == 1);

                    if (isLoginScreen)
                    {
                        UpdateStatus("Все еще на экране логина, пропускаем ввод 2FA");
                        await Task.Delay(50);
                        continue;
                    }

                    UpdateStatus($"Экран изменился: {inputs.Count} полей, {buttons.Count} кнопок - это экран 2FA");
                    // Парсим логин из окна Steam
                    var loginFromWindow = ParseLoginFromWindow(window);
                    if (!string.IsNullOrEmpty(loginFromWindow))
                    {
                        UpdateStatus($"Обнаружен логин в окне: {loginFromWindow}");
                    }

                    // Ищем кнопку "Enter a code instead"
                    var codeButton = FindEnterCodeButton(window);
                    if (codeButton != null)
                    {
                        UpdateStatus("Нажатие 'Enter a code'...");
                        try
                        {
                            codeButton.Patterns.Invoke.Pattern.Invoke();
                        }
                        catch
                        {
                            try
                            {
                                codeButton.AsButton().Invoke();
                            }
                            catch { }
                        }
                        await Task.Delay(200);
                    }

                    // Проверяем поля 2FA
                    inputs = FindElementsByType(window, ControlType.Edit);
                    buttons = FindElementsByType(window, ControlType.Button);

                    UpdateStatus($"2FA элементы: {inputs.Count} полей, {buttons.Count} кнопок");

                    // Старый UI: 5 кнопок
                    if (buttons.Count == 5 && inputs.Count == 0)
                    {
                        if (_currentState == LoginState.WaitingFor2FA)
                        {
                            _currentState = LoginState.Entering2FA;
                            UpdateStatus("Ввод 2FA (старый UI)...");

                            try
                            {
                                buttons[0].AsButton().Invoke();
                                await Task.Delay(50);

                                UpdateStatus($"Ввод кода напрямую: {_guardCode}");
                                Keyboard.Type(_guardCode!);

                                _currentState = LoginState.Completed;
                                UpdateStatus("Готово!");
                                _isRunning = false;
                                _onLoginCompleted?.Invoke();
                            }
                            catch (Exception ex)
                            {
                                UpdateStatus($"Ошибка 2FA: {ex.Message}");
                                _currentState = LoginState.WaitingFor2FA;
                            }
                        }
                    }

                    // React UI: 4-6 полей или любое количество полей ввода
                    if (inputs.Count >= 1 && buttons.Count != 5)
                    {
                        if (_currentState == LoginState.WaitingFor2FA)
                        {
                            _currentState = LoginState.Entering2FA;
                            UpdateStatus($"Ввод 2FA ({inputs.Count} полей)...");

                            try
                            {
                                UpdateStatus($"Сортировка {inputs.Count} полей по X координате...");
                                inputs.Sort((a, b) => (int)(a.BoundingRectangle.X - b.BoundingRectangle.X));

                                var firstInput = inputs[0];
                                UpdateStatus($"Фокус на первое поле: {firstInput.Name}");
                                firstInput.Focus();
                                await Task.Delay(50);

                                UpdateStatus($"Ввод кода напрямую: {_guardCode}");

                                // Вводим код напрямую через Keyboard.Type вместо буфера обмена
                                Keyboard.Type(_guardCode!);

                                await Task.Delay(100);

                                _currentState = LoginState.Completed;
                                UpdateStatus("Готово!");
                                _isRunning = false;
                                _onLoginCompleted?.Invoke();
                            }
                            catch (Exception ex)
                            {
                                UpdateStatus($"Ошибка 2FA: {ex.Message}");
                                UpdateStatus($"StackTrace: {ex.StackTrace}");
                                _currentState = LoginState.WaitingFor2FA;
                            }
                        }
                    }
                }

                await Task.Delay(50);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Ошибка: {ex.Message}");
                await Task.Delay(100);
            }
        }
    }

    private AutomationElement? FindEnterCodeButton(AutomationElement window)
    {
        try
        {
            var document = window.FindFirstDescendant(e => e.ByControlType(ControlType.Document));
            if (document == null)
                return null;

            var allElements = document.FindAllDescendants();

            foreach (var el in allElements)
            {
                var name = el.Name ?? "";
                if (name.ToLower().Contains("enter a code instead") ||
                    name.ToLower().Contains("enter a code") ||
                    name.ToLower().Contains("code instead"))
                {
                    return el;
                }
            }
        }
        catch { }

        return null;
    }

    private string? ParseLoginFromWindow(AutomationElement window)
    {
        try
        {
            var document = window.FindFirstDescendant(e => e.ByControlType(ControlType.Document));
            if (document == null)
                return null;

            var allElements = document.FindAllDescendants();

            // Ищем текст с логином (обычно отображается как "Signing in as username")
            foreach (var el in allElements)
            {
                var name = el.Name ?? "";

                // Проверяем различные варианты текста
                if (name.ToLower().Contains("signing in as") ||
                    name.ToLower().Contains("sign in as") ||
                    name.ToLower().Contains("войти как"))
                {
                    // Извлекаем логин из текста
                    var parts = name.Split(new[] { "as ", "как " }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                    {
                        return parts[1].Trim();
                    }
                }

                // Также проверяем просто текстовые элементы, которые могут содержать логин
                if (el.ControlType == ControlType.Text && !string.IsNullOrWhiteSpace(name))
                {
                    // Если текст похож на логин (не содержит пробелов и спецсимволов)
                    if (!name.Contains(" ") && name.Length > 3 && name.Length < 30)
                    {
                        // Проверяем что это не служебный текст
                        var lowerName = name.ToLower();
                        if (!lowerName.Contains("steam") &&
                            !lowerName.Contains("sign") &&
                            !lowerName.Contains("login") &&
                            !lowerName.Contains("password"))
                        {
                            return name;
                        }
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private Process? GetSteamProcess()
    {
        // Если указан конкретный PID, ищем только его
        if (_steamPid > 0)
        {
            try
            {
                var process = Process.GetProcessById(_steamPid);
                if (process.ProcessName.ToLower() == "steam")
                {
                    return process;
                }
            }
            catch
            {
                // Процесс не найден или завершен
            }
            return null;
        }

        // Иначе возвращаем первый найденный процесс Steam
        var processes = Process.GetProcessesByName("steam");
        return processes.Length > 0 ? processes[0] : null;
    }

    private IntPtr? GetSteamLoginWindow(Process steamProcess)
    {
        try
        {
            foreach (var child in GetChildProcesses(steamProcess))
            {
                if (child.ProcessName == "steamwebhelper")
                {
                    foreach (var hWnd in EnumerateProcessWindowHandles(child))
                    {
                        var text = GetWindowTextRaw(hWnd);
                        if (text.Contains("Steam") && text.Length > 5)
                            return hWnd;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private static IEnumerable<Process> GetChildProcesses(Process process)
    {
        var children = new List<Process>();
        try
        {
            using var mos = new ManagementObjectSearcher($"Select * From Win32_Process Where ParentProcessID={process.Id}");
            foreach (ManagementObject mo in mos.Get())
            {
                try
                {
                    children.Add(Process.GetProcessById(Convert.ToInt32(mo["ProcessID"])));
                }
                catch { }
            }
        }
        catch { }
        return children;
    }

    private static IEnumerable<IntPtr> EnumerateProcessWindowHandles(Process process)
    {
        var handles = new List<IntPtr>();
        try
        {
            foreach (ProcessThread thread in process.Threads)
            {
                EnumThreadWindows((uint)thread.Id, (hWnd, _) =>
                {
                    if (IsWindowVisible(hWnd))
                        handles.Add(hWnd);
                    return true;
                }, IntPtr.Zero);
            }
        }
        catch { }
        return handles;
    }

    private static string GetWindowTextRaw(IntPtr hwnd)
    {
        try
        {
            var length = (int)SendMessage(hwnd, WM_GETTEXTLENGTH, IntPtr.Zero, null!);
            var sb = new StringBuilder(length + 1);
            SendMessage(hwnd, WM_GETTEXT, (IntPtr)sb.Capacity, sb);
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static List<AutomationElement> FindElementsByType(AutomationElement root, ControlType type)
    {
        var elements = new List<AutomationElement>();
        foreach (var el in root.FindAllDescendants())
        {
            if (el.ControlType == type)
                elements.Add(el);
        }
        return elements;
    }

    private void UpdateStatus(string message)
    {
        AppLogger.Info($"[AutoLogin] {message}");
        _onStatusChanged?.Invoke(message);
    }
}
