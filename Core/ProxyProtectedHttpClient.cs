using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SteamGuard
{
    /// <summary>
    /// Обертка над HttpClient с защитой от утечки IP при падении прокси
    /// </summary>
    public class ProxyProtectedHttpClient : IDisposable
    {
        private readonly HttpClient _client;
        private readonly string? _proxyAddress;
        private readonly int? _proxyPort;
        private readonly bool _requireProxy;

        public ProxyProtectedHttpClient(HttpClient client, string? proxyAddress = null, int? proxyPort = null, bool requireProxy = false)
        {
            _client = client;
            _proxyAddress = proxyAddress;
            _proxyPort = proxyPort;
            _requireProxy = requireProxy;
        }

        /// <summary>
        /// Проверяет доступность прокси перед выполнением запроса
        /// </summary>
        private async Task<bool> CheckProxyAvailability()
        {
            if (string.IsNullOrEmpty(_proxyAddress) || !_proxyPort.HasValue)
            {
                return true; // Прокси не настроен
            }

            try
            {
                using var tcpClient = new System.Net.Sockets.TcpClient();
                var connectTask = tcpClient.ConnectAsync(_proxyAddress, _proxyPort.Value);
                var timeoutTask = Task.Delay(3000); // 3 секунды таймаут

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    AppLogger.Error($"Proxy check timeout: {_proxyAddress}:{_proxyPort}");
                    return false;
                }

                if (connectTask.IsFaulted)
                {
                    AppLogger.Error($"Proxy connection failed: {_proxyAddress}:{_proxyPort} - {connectTask.Exception?.Message}");
                    return false;
                }

                AppLogger.Debug($"Proxy is available: {_proxyAddress}:{_proxyPort}");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Proxy check error: {_proxyAddress}:{_proxyPort} - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Выполняет GET запрос с проверкой прокси
        /// </summary>
        public async Task<HttpResponseMessage> GetAsync(string requestUri)
        {
            if (_requireProxy && !string.IsNullOrEmpty(_proxyAddress))
            {
                var isProxyAvailable = await CheckProxyAvailability();
                if (!isProxyAvailable)
                {
                    throw new ProxyException($"Proxy is not available: {_proxyAddress}:{_proxyPort}. Request blocked to prevent IP leak.");
                }
            }

            try
            {
                return await _client.GetAsync(requestUri);
            }
            catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
            {
                AppLogger.Error($"Request failed, possibly proxy issue: {ex.Message}");
                throw new ProxyException($"Request failed through proxy {_proxyAddress}:{_proxyPort}. IP leak prevented.", ex);
            }
        }

        /// <summary>
        /// Выполняет POST запрос с проверкой прокси
        /// </summary>
        public async Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content)
        {
            if (_requireProxy && !string.IsNullOrEmpty(_proxyAddress))
            {
                var isProxyAvailable = await CheckProxyAvailability();
                if (!isProxyAvailable)
                {
                    throw new ProxyException($"Proxy is not available: {_proxyAddress}:{_proxyPort}. Request blocked to prevent IP leak.");
                }
            }

            try
            {
                return await _client.PostAsync(requestUri, content);
            }
            catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
            {
                AppLogger.Error($"Request failed, possibly proxy issue: {ex.Message}");
                throw new ProxyException($"Request failed through proxy {_proxyAddress}:{_proxyPort}. IP leak prevented.", ex);
            }
        }

        /// <summary>
        /// Выполняет запрос с проверкой прокси
        /// </summary>
        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            if (_requireProxy && !string.IsNullOrEmpty(_proxyAddress))
            {
                var isProxyAvailable = await CheckProxyAvailability();
                if (!isProxyAvailable)
                {
                    throw new ProxyException($"Proxy is not available: {_proxyAddress}:{_proxyPort}. Request blocked to prevent IP leak.");
                }
            }

            try
            {
                return await _client.SendAsync(request);
            }
            catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
            {
                AppLogger.Error($"Request failed, possibly proxy issue: {ex.Message}");
                throw new ProxyException($"Request failed through proxy {_proxyAddress}:{_proxyPort}. IP leak prevented.", ex);
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }

    /// <summary>
    /// Исключение при проблемах с прокси
    /// </summary>
    public class ProxyException : Exception
    {
        public ProxyException(string message) : base(message) { }
        public ProxyException(string message, Exception innerException) : base(message, innerException) { }
    }
}
