namespace OrderApi.Clients;

/// <summary>
/// Typed client. The factory injects a new HttpClient wrapper each time this class is created.
/// The wrapper does not own the handler. The factory pools handlers and rotates them on HandlerLifetime.
/// </summary>
public sealed class FactoryInventoryClient
{
    public const string Name = "inventory-typed";
    public const string StrategyName = "factory";

    private readonly HttpClient _http;

    public FactoryInventoryClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    public Task<InventoryCallResult> GetStockAsync(string sku, string correlationId, CancellationToken cancellationToken) =>
        InventoryHttp.GetStockAsync(_http, sku, correlationId, cancellationToken);

    public Task<InventoryCallResult> ReserveAsync(
        string sku,
        int quantity,
        string correlationId,
        CancellationToken cancellationToken) =>
        InventoryHttp.ReserveAsync(_http, sku, quantity, correlationId, cancellationToken);
}
