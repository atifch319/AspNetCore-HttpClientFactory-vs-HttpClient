using System.Net;
using OrderApi.Clients;

namespace OrderApi.Configuration;

public sealed class DnsSimulationOptions
{
    public const string SectionName = "DnsSimulation";

    public bool Enabled { get; set; } = true;

    public string LogicalHost
    {
        get => field;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value.Contains('/', StringComparison.Ordinal))
            {
                throw new ArgumentException("LogicalHost must be a hostname such as inventory.local.", nameof(value));
            }

            if (IPAddress.TryParse(value, out _))
            {
                throw new ArgumentException(
                    "LogicalHost must be a hostname. Put the IP address in the endpoint directory.",
                    nameof(value));
            }

            field = value.Trim();
        }
    } = "inventory.local";

    public string InitialBaseAddress
    {
        get => field;
        set => field = BaseAddressRules.Normalize(value).AbsoluteUri;
    } = "http://127.0.0.1:5080/";

    public int PooledConnectionLifetimeSeconds
    {
        get => field;
        set => field = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "PooledConnectionLifetimeSeconds must be positive.");
    } = 10;

    public int TimeoutSeconds
    {
        get => field;
        set => field = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "TimeoutSeconds must be positive.");
    } = 10;
}
