using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WatchingEye.Services;

namespace WatchingEye
{
    public class ExternalWebServer
    {
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly HttpListener _listener;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly string _password;
        private readonly int _port;

        private static readonly ConcurrentDictionary<string, DateTime> _activeTokens = new();
        private static Timer? _tokenCleanupTimer;

        public ExternalWebServer(ILogger logger, IJsonSerializer jsonSerializer, int port, string password)
        {
            _logger = logger;
            _jsonSerializer = jsonSerializer;
            _password = password;
            _port = port;
            _cancellationTokenSource = new CancellationTokenSource();

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{port}/");
        }

        public string Start()
        {
            try
            {
                _tokenCleanupTimer = new Timer(CleanupExpiredTokens, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
                _listener.Start();
                var prefix = _listener.Prefixes.First();
                _logger.Info($"[ExternalWebServer] Started and listening on {prefix}.");
                Task.Run(() => Listen(_cancellationTokenSource.Token));
                return $"Running on port {_port}";
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 5)
            {
                var errorMsg = $"Error: Access Denied. On Windows, please run this command as an Administrator: netsh http add urlacl url=http://*:{_port}/ user=\"Everyone\". On Linux/Docker, ensure the port is available and Emby has network permissions. See the plugin configuration page for more details.";
                _logger.Error(errorMsg);
                return errorMsg;
            }
            catch (Exception ex)
            {
                var errorMsg = $"[ExternalWebServer] Failed to start: {ex.Message}";
                _logger.ErrorException(errorMsg, ex);
                return errorMsg;
            }
        }

        public void Stop()
        {
            if (!_listener.IsListening) return;
            _cancellationTokenSource.Cancel();
            _tokenCleanupTimer?.Dispose();
            _listener.Stop();
            _listener.Close();
            _logger.Info("[ExternalWebServer] Stopped.");
        }

        private void CleanupExpiredTokens(object? state)
        {
            var now = DateTime.UtcNow;
            var expiredTokens = _activeTokens.Where(p => p.Value < now).Select(p => p.Key).ToList();
            foreach (var token in expiredTokens)
            {
                _activeTokens.TryRemove(token, out _);
            }
        }

        private async Task Listen(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    await ProcessRequest(context);
                }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { break; }
                catch (Exception ex)
                {
                    _logger.ErrorException("[ExternalWebServer] Error processing request.", ex);
                }
            }
        }

        private async Task ProcessRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            if (request.Url == null)
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.OutputStream.Close();
                return;
            }

            string requestBody;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            try
            {
                if (request.Url.AbsolutePath.StartsWith("/api/"))
                {
                    response.ContentType = "application/json";
                    ApiRequest? apiRequest = null;
                    if (!string.IsNullOrEmpty(requestBody))
                    {
                        apiRequest = _jsonSerializer.DeserializeFromString<ApiRequest>(requestBody);
                    }

                    if (request.Url.AbsolutePath == "/api/login")
                    {
                        await HandleLoginRequest(apiRequest, response);
                        return;
                    }

                    if (string.IsNullOrEmpty(_password) || apiRequest?.Token == null || !_activeTokens.TryGetValue(apiRequest.Token, out var expiry) || expiry < DateTime.UtcNow)
                    {
                        response.StatusCode = (int)HttpStatusCode.Unauthorized;
                        await WriteResponse(response, "{\"error\":\"Unauthorized\"}");
                        return;
                    }

                    await HandleApiRequest(request.Url.AbsolutePath, apiRequest, response);
                }
                else
                {
                    await HandleFileRequest(request.Url.AbsolutePath, response);
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException($"[ExternalWebServer] Error during request to {request.Url}:", ex);
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await WriteResponse(response, "{\"error\":\"An internal server error occurred.\"}");
            }
            finally
            {
                response.OutputStream.Close();
            }
        }

        private async Task HandleLoginRequest(ApiRequest? apiRequest, HttpListenerResponse response)
        {
            if (string.IsNullOrEmpty(_password) || apiRequest?.Password != _password)
            {
                response.StatusCode = (int)HttpStatusCode.Unauthorized;
                await WriteResponse(response, "{\"error\":\"Invalid password\"}");
                return;
            }

            var token = Guid.NewGuid().ToString();
            var expiry = DateTime.UtcNow.AddHours(8);
            _activeTokens[token] = expiry;

            var result = new { token, expires = expiry };
            var jsonResponse = _jsonSerializer.SerializeToString(result);
            response.StatusCode = (int)HttpStatusCode.OK;
            await WriteResponse(response, jsonResponse);
        }

        private async Task HandleApiRequest(string path, ApiRequest? apiRequest, HttpListenerResponse response)
        {
            if (apiRequest == null)
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                await WriteResponse(response, "{\"error\":\"Invalid request body.\"}");
                return;
            }

            object result;
            switch (path)
            {
                case "/api/status":
                    result = WatchTimeManager.GetLimitedUsersStatus();
                    break;
                case "/api/extend":
                    WatchTimeManager.ExtendTimeForUser(apiRequest.UserId, apiRequest.Minutes);
                    result = new { message = "Time extended successfully." };
                    break;
                case "/api/timeout":
                    WatchTimeManager.TimeOutUser(apiRequest.UserId, apiRequest.Minutes);
                    result = new { message = "User timed out successfully." };
                    break;
                case "/api/cleartimeout":
                    WatchTimeManager.ClearTimeOutForUser(apiRequest.UserId);
                    result = new { message = "User time-out cleared." };
                    break;
                case "/api/reset":
                    WatchTimeManager.ResetWatchTimeForUser(apiRequest.UserId);
                    result = new { message = "User time reset successfully." };
                    break;
                case "/api/updatelimits":
                    var success = Plugin.UpdateUserLimits(apiRequest.UserId, apiRequest.DailyLimitMinutes, apiRequest.WeeklyLimitHours, apiRequest.MonthlyLimitHours);
                    result = new { message = success ? "User limits updated successfully." : "Failed to update user limits." };
                    break;
                default:
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    await WriteResponse(response, "{\"error\":\"API endpoint not found.\"}");
                    return;
            }

            response.StatusCode = (int)HttpStatusCode.OK;
            var jsonResponse = _jsonSerializer.SerializeToString(result);
            await WriteResponse(response, jsonResponse);
        }

        private async Task HandleFileRequest(string path, HttpListenerResponse response)
        {
            var resourcePath = path == "/" ? "ExternalAdmin.html" : path.TrimStart('/');
            var resourceName = $"{GetType().Namespace}.Web.{resourcePath}";
            var assembly = GetType().GetTypeInfo().Assembly;

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    await WriteResponse(response, "404 Not Found");
                    return;
                }

                response.StatusCode = (int)HttpStatusCode.OK;
                response.ContentType = GetContentType(resourcePath);
                await stream.CopyToAsync(response.OutputStream);
            }
        }

        private string GetContentType(string path)
        {
            if (path.EndsWith(".html")) return "text/html";
            if (path.EndsWith(".js")) return "application/javascript";
            if (path.EndsWith(".css")) return "text/css";
            return "application/octet-stream";
        }

        private async Task WriteResponse(HttpListenerResponse response, string content)
        {
            var buffer = Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private class ApiRequest
        {
            public string? Password { get; set; }
            public string? Token { get; set; }
            public string UserId { get; set; } = string.Empty;
            public int Minutes { get; set; }
            public int DailyLimitMinutes { get; set; }
            public int WeeklyLimitHours { get; set; }
            public int MonthlyLimitHours { get; set; }
        }
    }
}