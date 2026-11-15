using OrderApi.Clients;

namespace OrderApi.Configuration;

public sealed class InventoryOptions
{
    public const string SectionName = "Inventory";

    public string BaseAddress
    {
        get => field;
        set => field = BaseAddressRules.Normalize(value).AbsoluteUri;
    } = "http://127.0.0.1:5080/";

    public int TimeoutSeconds
    {
        get => field;
        set => field = RequirePositive(value, nameof(TimeoutSeconds));
    } = 10;

    public int HandlerLifetimeSeconds
    {
        get => field;
        set => field = RequirePositive(value, nameof(HandlerLifetimeSeconds));
    } = 120;

    public int PooledConnectionLifetimeSeconds
    {
        get => field;
        set => field = RequirePositive(value, nameof(PooledConnectionLifetimeSeconds));
    } = 120;

    private static int RequirePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be a positive number of seconds.");
        }

        return value;
    }
}
