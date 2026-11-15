namespace OrderApi.Clients;

public static class InventoryRoutes
{
    public static string Stock(string sku)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        // No leading slash: a leading slash would replace the base path instead of appending to it.
        return "api/inventory/" + Uri.EscapeDataString(sku.Trim());
    }

    public const string Reservations = "api/reservations";
}
