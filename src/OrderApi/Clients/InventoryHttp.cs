using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OrderApi.Clients;

/// <summary>
/// Shared request construction for every lifetime strategy.
/// The strategies differ in who owns the HttpClient and the handler, not in the HTTP call itself.
/// </summary>
public static class InventoryHttp
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task<InventoryCallResult> GetStockAsync(
        HttpClient client,
        string sku,
        string correlationId,
        CancellationToken cancellationToken)
    {
        return SendAsync(client, HttpMethod.Get, InventoryRoutes.Stock(sku), correlationId, content: null, cancellationToken);
    }

    public static Task<InventoryCallResult> ReserveAsync(
        HttpClient client,
        string sku,
        int quantity,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        var json = JsonSerializer.Serialize(new ReservationBody(sku.Trim(), quantity), JsonOptions);
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return SendAsync(client, HttpMethod.Post, InventoryRoutes.Reservations, correlationId, content, cancellationToken);
    }

    public static async Task<InventoryCallResult> SendAsync(
        HttpClient client,
        HttpMethod method,
        string relativeUri,
        string correlationId,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (relativeUri.StartsWith('/'))
        {
            throw new ArgumentException(
                "Relative request URIs must not start with '/'. A leading slash replaces the BaseAddress path.",
                nameof(relativeUri));
        }

        using var request = new HttpRequestMessage(method, relativeUri);
        // Request-specific headers belong on the message. DefaultRequestHeaders is shared by every caller of this client.
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        request.Content = content;

        try
        {
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            var body = response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            request.Options.TryGetValue(InstanceIdHandler.HandlerIdOption, out string? handlerId);
            var requestUri = request.RequestUri ?? throw new InvalidOperationException("The request URI was not set.");

            if (!response.IsSuccessStatusCode)
            {
                throw new InventoryCallException(response.StatusCode, body, requestUri, handlerId);
            }

            return new InventoryCallResult((int)response.StatusCode, ReadInstanceId(body), handlerId, body, requestUri);
        }
        catch (Exception ex) when (ex is InventoryCallException or InventoryTimeoutException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // HttpClient.Timeout cancels its own token. That is independent of the caller's CancellationToken.
            throw new InventoryTimeoutException(
                "The inventory call ended because HttpClient.Timeout elapsed. The caller cancellation token was not canceled.",
                ex);
        }
    }

    private static string? ReadInstanceId(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("instanceId", out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private sealed record ReservationBody(string Sku, int Quantity);
}
