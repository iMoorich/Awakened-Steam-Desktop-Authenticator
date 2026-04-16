using System;
using System.IO;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace SteamGuard
{
    /// <summary>
    /// Уровни логирования
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3
    }

    /// <summary>
    /// Асинхронный логгер с ротацией файлов
    /// </summary>
    public static class AppLogger
    {
        private static readonly string _logDirectory;
        private static readonly object _lock = new object();
        private static readonly ConcurrentQueue<LogEntry> _logQueue = new();
        private static readonly CancellationTokenSource _cts = new();
        private static Task? _writerTask;

        // Настройки
        private static LogLevel _minLevel = LogLevel.Debug;
        private static readonly long _maxFileSize = 10 * 1024 * 1024; // 10 MB
        private static readonly int _maxBackupFiles = 5;

        static AppLogger()
        {
            _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(_logDirectory);

            // Запускаем фоновый поток для записи логов
            _writerTask = Task.Run(ProcessLogQueue);
        }

        /// <summary>
        /// Установить минимальный уровень логирования
        /// </summary>
        public static void SetMinLevel(LogLevel level)
        {
            _minLevel = level;
        }

        public static void Debug(string message, string? context = null)
        {
            Log(LogLevel.Debug, message, context);
        }

        public static void Info(string message, string? context = null)
        {
            Log(LogLevel.Info, message, context);
        }

        public static void Warn(string message, string? context = null)
        {
            Log(LogLevel.Warn, message, context);
        }

        public static void Error(string message, Exception? ex = null, string? context = null)
        {
            var fullMessage = ex != null
                ? $"{message}: {ex.Message}\n{ex.StackTrace}"
                : message;
            Log(LogLevel.Error, fullMessage, context);
        }

        private static void Log(LogLevel level, string message, string? context)
        {
            if (level < _minLevel) return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Context = context,
                ThreadId = Thread.CurrentThread.ManagedThreadId
            };

            _logQueue.Enqueue(entry);
        }

        private static async Task ProcessLogQueue()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    if (_logQueue.TryDequeue(out var entry))
                    {
                        await WriteToFile(entry);
                    }
                    else
                    {
                        await Task.Delay(100, _cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Игнорируем ошибки записи
                }
            }

            // Записываем оставшиеся логи при завершении
            while (_logQueue.TryDequeue(out var entry))
            {
                try
                {
                    await WriteToFile(entry);
                }
                catch { }
            }
        }

        private static async Task WriteToFile(LogEntry entry)
        {
            var logFile = GetCurrentLogFile();

            // Проверяем размер файла и делаем ротацию если нужно
            if (File.Exists(logFile))
            {
                var fileInfo = new FileInfo(logFile);
                if (fileInfo.Length > _maxFileSize)
                {
                    RotateLogFiles(logFile);
                }
            }

            var contextStr = string.IsNullOrEmpty(entry.Context) ? "" : $" [{entry.Context}]";
            var line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level,-5}] [T{entry.ThreadId}]{contextStr} {entry.Message}";

            lock (_lock)
            {
                File.AppendAllText(logFile, line + Environment.NewLine);
            }
        }

        private static string GetCurrentLogFile()
        {
            return Path.Combine(_logDirectory, $"app_{DateTime.Now:yyyyMMdd}.log");
        }

        private static void RotateLogFiles(string currentFile)
        {
            try
            {
                var directory = Path.GetDirectoryName(currentFile)!;
                var fileName = Path.GetFileNameWithoutExtension(currentFile);
                var extension = Path.GetExtension(currentFile);

                // Удаляем самый старый бэкап если достигли лимита
                var oldestBackup = Path.Combine(directory, $"{fileName}.{_maxBackupFiles}{extension}");
                if (File.Exists(oldestBackup))
                {
                    File.Delete(oldestBackup);
                }

                // Сдвигаем все бэкапы на 1
                for (int i = _maxBackupFiles - 1; i >= 1; i--)
                {
                    var source = Path.Combine(directory, $"{fileName}.{i}{extension}");
                    var dest = Path.Combine(directory, $"{fileName}.{i + 1}{extension}");
                    if (File.Exists(source))
                    {
                        File.Move(source, dest, true);
                    }
                }

                // Переименовываем текущий файл в .1
                var firstBackup = Path.Combine(directory, $"{fileName}.1{extension}");
                File.Move(currentFile, firstBackup, true);
            }
            catch
            {
                // Игнорируем ошибки ротации
            }
        }

        /// <summary>
        /// Очистить старые логи (старше указанного количества дней)
        /// </summary>
        public static void CleanOldLogs(int daysToKeep = 7)
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                var files = Directory.GetFiles(_logDirectory, "app_*.log*");

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime < cutoffDate)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки очистки
            }
        }

        /// <summary>
        /// Остановить логгер и дождаться записи всех логов
        /// </summary>
        public static async Task ShutdownAsync()
        {
            _cts.Cancel();
            if (_writerTask != null)
            {
                await _writerTask;
            }
        }

        public static string LogDirectory => _logDirectory;
    }

    internal class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Context { get; set; }
        public int ThreadId { get; set; }
    }
}
