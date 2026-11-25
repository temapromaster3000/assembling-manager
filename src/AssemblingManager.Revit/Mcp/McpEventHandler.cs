using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using Autodesk.Revit.UI;
using AssemblingManager.Revit.Services;

namespace AssemblingManager.Revit.Mcp
{
    internal sealed class McpEventHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<McpBridgeRequest> _queue = new ConcurrentQueue<McpBridgeRequest>();
        private readonly McpToolDispatcher _dispatcher;
        private ExternalEvent _externalEvent;
        private int _raisePending;
        private volatile bool _stopping;

        public McpEventHandler(McpToolDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public void Attach(ExternalEvent externalEvent)
        {
            _externalEvent = externalEvent;
        }

        public bool TryEnqueue(McpBridgeRequest request)
        {
            if (_stopping)
            {
                return false;
            }

            _queue.Enqueue(request);
            RaiseEvent();
            return true;
        }

        public void Execute(UIApplication app)
        {
            Interlocked.Exchange(ref _raisePending, 0);

            while (_queue.TryDequeue(out McpBridgeRequest request))
            {
                if (request.Abandoned)
                {
                    continue;
                }

                Stopwatch stopwatch = Stopwatch.StartNew();
                McpResponse response;

                try
                {
                    object result = _dispatcher.Dispatch(app, request);
                    stopwatch.Stop();
                    response = new McpResponse
                    {
                        RequestId = request.RequestId,
                        Ok = true,
                        Result = JsonSerializer.SerializeToElement(result, McpJson.Options),
                        ElapsedMs = stopwatch.ElapsedMilliseconds
                    };
                }
                catch (McpToolException ex)
                {
                    stopwatch.Stop();
                    response = new McpResponse
                    {
                        RequestId = request.RequestId,
                        Ok = false,
                        Error = new McpError(ex.Code, ex.Message),
                        ElapsedMs = stopwatch.ElapsedMilliseconds
                    };
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    Logger.Error("[MCP] Tool '" + request.Tool + "' failed: " + ex);
                    response = new McpResponse
                    {
                        RequestId = request.RequestId,
                        Ok = false,
                        Error = new McpError(McpErrorCodes.RevitApiError, ex.Message),
                        ElapsedMs = stopwatch.ElapsedMilliseconds
                    };
                }

                request.ResponseJson = JsonSerializer.Serialize(response, McpJson.Options);
                request.Done.Set();
            }

            if (!_queue.IsEmpty)
            {
                RaiseEvent();
            }
        }

        public string GetName()
        {
            return "Assembling Manager MCP Bridge";
        }

        public void Reset()
        {
            _stopping = false;
            Interlocked.Exchange(ref _raisePending, 0);
        }

        public void Stop()
        {
            _stopping = true;
            FailQueued(McpErrorCodes.BridgeStopped, "MCP-мост остановлен.");
        }

        private void RaiseEvent()
        {
            ExternalEvent externalEvent = _externalEvent;
            if (externalEvent == null)
            {
                FailQueued(McpErrorCodes.RevitBusy, "MCP-мост ещё не готов.");
                return;
            }

            if (Interlocked.CompareExchange(ref _raisePending, 1, 0) != 0)
            {
                return;
            }

            try
            {
                ExternalEventRequest status = externalEvent.Raise();
                if (status == ExternalEventRequest.Denied || status == ExternalEventRequest.TimedOut)
                {
                    Interlocked.Exchange(ref _raisePending, 0);
                    FailQueued(
                        McpErrorCodes.RevitBusy,
                        "Revit не принял запрос. Возможно, открыто модальное окно — закрой его и повтори.");
                }
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _raisePending, 0);
                FailQueued(McpErrorCodes.RevitApiError, "Не удалось передать запрос в Revit: " + ex.Message);
            }
        }

        private void FailQueued(string code, string message)
        {
            while (_queue.TryDequeue(out McpBridgeRequest request))
            {
                if (request.Abandoned)
                {
                    continue;
                }

                McpResponse response = new McpResponse
                {
                    RequestId = request.RequestId,
                    Ok = false,
                    Error = new McpError(code, message),
                    ElapsedMs = 0
                };

                request.ResponseJson = JsonSerializer.Serialize(response, McpJson.Options);
                request.Done.Set();
            }
        }
    }
}
