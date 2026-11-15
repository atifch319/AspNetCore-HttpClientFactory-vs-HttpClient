using System.Net;

namespace OrderApi.Clients;

public sealed record InventoryCallResult(
    int StatusCode,
    string? InventoryInstanceId,
    string? HandlerInstanceId,
    string Body,
    Uri RequestUri);

public sealed class InventoryCallException : Exception
{
    public InventoryCallException(HttpStatusCode statusCode, string body, Uri requestUri, string? handlerInstanceId)
        : base($"Inventory returned {(int)statusCode} ({statusCode}) for {requestUri}.")
    {
        StatusCode = statusCode;
        Body = body;
        RequestUri = requestUri;
        HandlerInstanceId = handlerInstanceId;
    }

    public HttpStatusCode StatusCode { get; }

    public string Body { get; }

    public Uri RequestUri { get; }

    public string? HandlerInstanceId { get; }
}

public sealed class InventoryTimeoutException : Exception
{
    public InventoryTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
