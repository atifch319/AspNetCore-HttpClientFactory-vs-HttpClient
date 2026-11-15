namespace InventoryApi;

public enum ReserveStatus
{
    Reserved,
    NotFound,
    Insufficient
}

public sealed record ReserveResult(ReserveStatus Status, string? ReservationId, int Remaining);

public sealed class InventoryStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _stock = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WIDGET-1"] = 10_000,
        ["GADGET-2"] = 25
    };

    public bool TryGet(string sku, out int quantity)
    {
        lock (_gate)
        {
            return _stock.TryGetValue(sku, out quantity);
        }
    }

    public ReserveResult TryReserve(string sku, int quantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        lock (_gate)
        {
            if (!_stock.TryGetValue(sku, out var onHand))
            {
                return new ReserveResult(ReserveStatus.NotFound, null, 0);
            }

            if (quantity > onHand)
            {
                return new ReserveResult(ReserveStatus.Insufficient, null, onHand);
            }

            var remaining = onHand - quantity;
            _stock[sku] = remaining;
            return new ReserveResult(ReserveStatus.Reserved, Guid.NewGuid().ToString("n"), remaining);
        }
    }
}
