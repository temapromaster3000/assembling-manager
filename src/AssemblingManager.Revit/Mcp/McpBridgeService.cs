using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using AssemblingManager.Revit.Services;

namespace AssemblingManager.Revit.Mcp
{
    internal sealed class McpBridgeService : IDisposable
    {
        public const int ProtocolVersion = 1;

        private const int MaxMessageLength = 1024 * 1024;
        private const int DefaultTimeoutMs = 60000;
        private const int MaxTimeoutMs = 600000;
        private const int ClientReadTimeoutMs = 15000;

        private readonly McpSettings _settings;
        private readonly string _revitVersion;
        private readonly string _userName;
        private readonly McpToolDispatcher _dispatcher;
        private readonly McpEventHandler _handler;
        private readonly ExternalEvent _externalEvent;
        private readonly object _sync = new object();

        private CancellationTokenSource _cts;
        private NamedPipeServerStream _acceptPipe;
        private McpDiscoveryFile _discovery;
        private string _pipeName;
        private string _token;
        private DateTime _startedUtc;
        private volatile bool _running;
        private string _lastError;
        private bool _disposed;

        public McpBridgeService(string revitVersion)
        {
            _settings = McpSettings.Load();
            _revitVersion = string.IsNullOrWhiteSpace(revitVersion) ? "unknown" : revitVersion;
            _userName = Environment.UserName;
            _dispatcher = new McpToolDispatcher(this);
            _handler = new McpEventHandler(_dispatcher);
            _externalEvent = ExternalEvent.Create(_handler);
            _handler.Attach(_externalEvent);
        }

        public McpSettings Settings
        {
            get { return _settings; }
        }

        public bool IsRunning
        {
            get { return _running; }
        }

        public string PipeName
        {
            get { return _pipeName; }
        }

        public DateTime StartedUtc
        {
            get { return _startedUtc; }
        }

        public string LastError
        {
            get { return _lastError; }
        }

        public void StartIfEnabled()
        {
            if (_settings.Enabled)
            {
                Start();
            }
        }

        public void UpdateSettings(bool enabled, bool allowModelChanges)
        {
            _settings.Enabled = enabled;
            _settings.AllowModelChanges = enabled && allowModelChanges;
            _settings.Save();

            if (enabled)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }

        public void Start()
        {
            if (_disposed || _running)
            {
                return;
            }

            try
            {
                _lastError = null;
                _handler.Reset();

                int pid = McpDiscoveryFile.GetCurrentProcessId();
                _pipeName = "AssemblingManager.Mcp." + pid + "." + Guid.NewGuid().ToString("N");
                _token = McpDiscoveryFile.GenerateToken();
                _startedUtc = DateTime.UtcNow;
                _discovery = new McpDiscoveryFile
                {
                    Pid = pid,
                    PipeName = _pipeName,
                    Token = _token,
                    PluginVersion = McpDiscoveryFile.GetPluginVersion(),
                    RevitVersion = _revitVersion,
                    UserName = _userName,
                    StartedUtc = _startedUtc
                };

                _cts = new CancellationTokenSource();
                _running = true;
                Task.Run(() => AcceptLoopAsync(_cts.Token));
                _discovery.Write();

                Logger.Info("[MCP] Bridge started. Pipe=" + _pipeName + ", Revit=" + _revitVersion);
            }
            catch (Exception ex)
            {
                _running = false;
                _lastError = ex.Message;
                Logger.Error("[MCP] Could not start bridge: " + ex);

                try
                {
                    _discovery?.Delete();
                }
                catch (Exception)
                {
                }

                _discovery = null;

                try
                {
                    _cts?.Dispose();
                }
                catch (Exception)
                {
                }

                _cts = null;
            }
        }

        public void Stop()
        {
            if (!_running && _discovery == null)
            {
                _handler.Stop();
                return;
            }

            _running = false;

            try
            {
                _cts?.Cancel();
            }
            catch (Exception)
            {
            }

            lock (_sync)
            {
                if (_acceptPipe != null)
                {
                    try
                    {
                        _acceptPipe.Dispose();
                    }
                    catch (Exception)
                    {
                    }

                    _acceptPipe = null;
                }
            }

            _handler.Stop();
            _discovery?.Delete();
            _discovery = null;

            try
            {
                _cts?.Dispose();
            }
            catch (Exception)
            {
            }

            _cts = null;
            Logger.Info("[MCP] Bridge stopped.");
        }

        public void RecordDocumentInfo(string title, string pathName)
        {
            McpDiscoveryFile discovery = _discovery;
            if (discovery == null)
            {
                return;
            }

            if (string.Equals(discovery.DocumentTitle, title, StringComparison.Ordinal) &&
                string.Equals(discovery.DocumentPath, pathName, StringComparison.Ordinal))
            {
                return;
            }

            discovery.DocumentTitle = title;
            discovery.DocumentPath = pathName;
            discovery.Write();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();

            try
            {
                _externalEvent?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warn("[MCP] Could not dispose external event: " + ex.Message);
            }
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream pipe = null;

                try
                {
                    pipe = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        10,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    lock (_sync)
                    {
                        _acceptPipe = pipe;
                    }

                    await pipe.WaitForConnectionAsync();

                    lock (_sync)
                    {
                        if (ReferenceEquals(_acceptPipe, pipe))
                        {
                            _acceptPipe = null;
                        }
                    }

                    NamedPipeServerStream connectedPipe = pipe;
                    pipe = null;
                    _ = Task.Run(() => HandleClientAsync(connectedPipe));
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    Logger.Warn("[MCP] Accept failed: " + ex.Message);
                    try
                    {
                        pipe?.Dispose();
                    }
                    catch (Exception)
                    {
                    }

                    await Task.Delay(500);
                }
            }
        }

        private async Task HandleClientAsync(NamedPipeServerStream pipe)
        {
            using (pipe)
            {
                try
                {
                    using (StreamReader reader = new StreamReader(
                               pipe,
                               new UTF8Encoding(false),
                               false,
                               4096,
                               true))
                    using (StreamWriter writer = new StreamWriter(
                               pipe,
                               new UTF8Encoding(false),
                               4096,
                               true))
                    {
                        writer.AutoFlush = true;

                        Task<string> readTask = reader.ReadLineAsync();
                        Task completed = await Task.WhenAny(readTask, Task.Delay(ClientReadTimeoutMs));
                        if (completed != readTask)
                        {
                            return;
                        }

                        string line = await readTask;
                        if (line == null)
                        {
                            return;
                        }

                        if (line.Length > MaxMessageLength)
                        {
                            await WriteError(
                                writer,
                                null,
                                McpErrorCodes.InvalidRequest,
                                "Запрос слишком большой.");
                            return;
                        }

                        McpRequest request;
                        try
                        {
                            request = JsonSerializer.Deserialize<McpRequest>(line, McpJson.Options);
                        }
                        catch (JsonException ex)
                        {
                            await WriteError(
                                writer,
                                null,
                                McpErrorCodes.InvalidRequest,
                                "Некорректный JSON: " + ex.Message);
                            return;
                        }

                        if (request == null)
                        {
                            await WriteError(
                                writer,
                                null,
                                McpErrorCodes.InvalidRequest,
                                "Пустой запрос.");
                            return;
                        }

                        string requestId = string.IsNullOrWhiteSpace(request.RequestId)
                            ? Guid.NewGuid().ToString("N")
                            : request.RequestId;

                        if (request.Protocol != ProtocolVersion)
                        {
                            await WriteError(
                                writer,
                                requestId,
                                McpErrorCodes.InvalidRequest,
                                "Несовпадение версии протокола: ожидается " + ProtocolVersion + ".");
                            return;
                        }

                        if (!FixedTimeEquals(request.Token, _token))
                        {
                            await WriteError(
                                writer,
                                requestId,
                                McpErrorCodes.Unauthorized,
                                "Неверный токен MCP-моста.");
                            return;
                        }

                        if (string.IsNullOrWhiteSpace(request.Tool))
                        {
                            await WriteError(
                                writer,
                                requestId,
                                McpErrorCodes.InvalidRequest,
                                "Не указан инструмент.");
                            return;
                        }

                        if (string.Equals(request.Tool, "ping", StringComparison.OrdinalIgnoreCase))
                        {
                            McpResponse pingResponse = new McpResponse
                            {
                                RequestId = requestId,
                                Ok = true,
                                Result = JsonSerializer.SerializeToElement(
                                    new
                                    {
                                        pong = true,
                                        pid = McpDiscoveryFile.GetCurrentProcessId(),
                                        startedUtc = _startedUtc
                                    },
                                    McpJson.Options)
                            };
                            await WriteResponse(writer, pingResponse);
                            return;
                        }

                        int timeoutMs = Clamp(request.TimeoutMs <= 0 ? DefaultTimeoutMs : request.TimeoutMs, 1000, MaxTimeoutMs);
                        McpBridgeRequest bridgeRequest = new McpBridgeRequest
                        {
                            RequestId = requestId,
                            Tool = request.Tool,
                            Args = request.Args,
                            TimeoutMs = timeoutMs
                        };

                        if (!_handler.TryEnqueue(bridgeRequest))
                        {
                            await WriteError(
                                writer,
                                requestId,
                                McpErrorCodes.BridgeStopped,
                                "MCP-мост остановлен.");
                            return;
                        }

                        Stopwatch stopwatch = Stopwatch.StartNew();
                        bool finished = bridgeRequest.Done.Wait(timeoutMs);
                        stopwatch.Stop();

                        if (!finished)
                        {
                            bridgeRequest.Abandoned = true;
                            Logger.Warn(
                                "[MCP] Tool '" + request.Tool + "' timed out after " +
                                stopwatch.ElapsedMilliseconds + " ms.");
                            await WriteError(
                                writer,
                                requestId,
                                McpErrorCodes.Timeout,
                                "Revit не ответил за " + timeoutMs + " мс. " +
                                "Возможно, открыто модальное окно — закрой его и повтори. " +
                                "Если операция уже началась, она может продолжиться в Revit.");
                            return;
                        }

                        if (string.IsNullOrEmpty(bridgeRequest.ResponseJson))
                        {
                            await WriteError(
                                writer,
                                requestId,
                                McpErrorCodes.RevitApiError,
                                "Пустой ответ от Revit.");
                            return;
                        }

                        await writer.WriteLineAsync(bridgeRequest.ResponseJson);
                        Logger.Debug(
                            "[MCP] Tool '" + request.Tool + "' completed in " +
                            stopwatch.ElapsedMilliseconds + " ms.");
                    }
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                catch (Exception ex)
                {
                    Logger.Warn("[MCP] Client handling failed: " + ex.Message);
                }
            }
        }

        private static async Task WriteError(
            StreamWriter writer,
            string requestId,
            string code,
            string message)
        {
            McpResponse response = new McpResponse
            {
                RequestId = requestId,
                Ok = false,
                Error = new McpError(code, message)
            };

            await WriteResponse(writer, response);
        }

        private static async Task WriteResponse(StreamWriter writer, McpResponse response)
        {
            string json = JsonSerializer.Serialize(response, McpJson.Options);
            await writer.WriteLineAsync(json);
            await writer.FlushAsync();
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            byte[] leftBytes = Encoding.UTF8.GetBytes(left);
            byte[] rightBytes = Encoding.UTF8.GetBytes(right);
            if (leftBytes.Length != rightBytes.Length)
            {
                return false;
            }

            int difference = 0;
            for (int i = 0; i < leftBytes.Length; i++)
            {
                difference |= leftBytes[i] ^ rightBytes[i];
            }

            return difference == 0;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }
    }
}
