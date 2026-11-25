using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace AssemblingManager.McpServer;

public sealed class BridgeClient
{
    private const int DefaultTimeoutMs = 60000;
    private const int MaxTimeoutMs = 600000;

    public async Task<JsonElement> GetStatusAsync(
        BridgeDiscoveryFile instance,
        CancellationToken cancellationToken)
    {
        return await CallAsync(instance, "status", null, 30000, cancellationToken);
    }

    public async Task<JsonElement> CallAsync(
        BridgeDiscoveryFile instance,
        string tool,
        object? args,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        if (instance == null)
        {
            throw new BridgeToolException("BRIDGE_NOT_FOUND", "MCP-мост не выбран.");
        }

        int bridgeTimeout = Math.Clamp(timeoutMs <= 0 ? DefaultTimeoutMs : timeoutMs, 1000, MaxTimeoutMs);
        int clientTimeout = bridgeTimeout + 5000;

        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(clientTimeout);

        try
        {
            using NamedPipeClientStream pipe = new NamedPipeClientStream(
                ".",
                instance.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync(Math.Min(5000, bridgeTimeout), timeoutSource.Token);

            using StreamReader reader = new StreamReader(
                pipe,
                new UTF8Encoding(false),
                false,
                4096,
                true);

            using StreamWriter writer = new StreamWriter(
                pipe,
                new UTF8Encoding(false),
                4096,
                true)
            {
                AutoFlush = true
            };

            BridgeRequest request = new BridgeRequest
            {
                Token = instance.Token,
                RequestId = Guid.NewGuid().ToString("N"),
                Tool = tool,
                Args = args,
                TimeoutMs = bridgeTimeout
            };

            string requestJson = JsonSerializer.Serialize(request, BridgeDiscovery.JsonOptions);
            await writer.WriteLineAsync(requestJson);
            await writer.FlushAsync();

            string? responseJson = await reader.ReadLineAsync(timeoutSource.Token);
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                throw new BridgeToolException(
                    "BRIDGE_UNAVAILABLE",
                    "MCP-мост Revit закрыл соединение без ответа.");
            }

            BridgeResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<BridgeResponse>(
                    responseJson,
                    BridgeDiscovery.JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new BridgeToolException(
                    "BRIDGE_PROTOCOL_ERROR",
                    "Некорректный ответ MCP-моста: " + ex.Message);
            }

            if (response == null)
            {
                throw new BridgeToolException(
                    "BRIDGE_PROTOCOL_ERROR",
                    "Пустой ответ MCP-моста.");
            }

            if (!response.Ok)
            {
                string code = response.Error?.Code ?? "REVIT_API_ERROR";
                string message = response.Error?.Message ?? "Revit вернул ошибку без описания.";
                throw new BridgeToolException(code, message);
            }

            return response.Result.Clone();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BridgeToolException(
                "TIMEOUT",
                "Revit не ответил за " + bridgeTimeout + " мс. " +
                "Возможно, открыто модальное окно — закрой его и повтори.");
        }
        catch (TimeoutException ex)
        {
            throw new BridgeToolException(
                "BRIDGE_UNAVAILABLE",
                "Не удалось подключиться к MCP-мосту Revit: " + ex.Message);
        }
        catch (IOException ex)
        {
            throw new BridgeToolException(
                "BRIDGE_UNAVAILABLE",
                "Связь с MCP-мостом Revit потеряна: " + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new BridgeToolException(
                "BRIDGE_UNAVAILABLE",
                "Нет доступа к каналу MCP-моста Revit: " + ex.Message);
        }
    }
}
