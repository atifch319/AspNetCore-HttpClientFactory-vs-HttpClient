using System.Collections.Concurrent;
using OrderApi.Clients;

namespace OrderApi.Orders;

public sealed record OrderRecord(
    string Id,
    string Sku,
    int Quantity,
    string InventoryInstanceId,
    string? HandlerInstanceId,
    DateTimeOffset CreatedAt);

public sealed class OrderStore
{
    private readonly ConcurrentDictionary<string, OrderRecord> _orders = new(StringComparer.Ordinal);

    public void Add(OrderRecord order)
    {
        ArgumentNullException.ThrowIfNull(order);
        _orders[order.Id] = order;
    }

    public bool TryGet(string id, out OrderRecord? order) => _orders.TryGetValue(id, out order);
}

/// <summary>
/// The order path uses the typed factory client. The client is transient and this service is scoped,
/// so a singleton does not capture one handler for the life of the process.
/// </summary>
public sealed class OrderService
{
    private readonly FactoryInventoryClient _inventory;
    private readonly OrderStore _store;

    public OrderService(FactoryInventoryClient inventory, OrderStore store)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(store);
        _inventory = inventory;
        _store = store;
    }

    public async Task<OrderRecord> PlaceAsync(string sku, int quantity, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        var reserved = await _inventory
            .ReserveAsync(sku.Trim(), quantity, Guid.NewGuid().ToString("n"), cancellationToken)
            .ConfigureAwait(false);

        var order = new OrderRecord(
            Guid.NewGuid().ToString("n"),
            sku.Trim(),
            quantity,
            reserved.InventoryInstanceId ?? "unknown",
            reserved.HandlerInstanceId,
            DateTimeOffset.UtcNow);

        _store.Add(order);
        return order;
    }
}
