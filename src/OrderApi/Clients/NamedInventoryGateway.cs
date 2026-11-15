using System.Runtime.CompilerServices;

namespace OrderApi.Clients;

/// <summary>
/// Named-client example. CreateClient is called at the point of use and returns a new wrapper every time.
/// Disposing that wrapper does not dispose the pooled handler.
/// </summary>
public sealed class NamedInventoryGateway
{
    public const string Name = "inventory-named";

    private readonly IHttpClientFactory _factory;

    public NamedInventoryGateway(IHttpClientFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task<WrapperProbeResult> ProbeAsync(string sku, string correlationId, CancellationToken cancellationToken)
    {
        var first = _factory.CreateClient(Name);
        var second = _factory.CreateClient(Name);
        var sameInstance = ReferenceEquals(first, second);

        try
        {
            var firstResult = await InventoryHttp
                .GetStockAsync(first, sku, correlationId + "-1", cancellationToken)
                .ConfigureAwait(false);
            var secondResult = await InventoryHttp
                .GetStockAsync(second, sku, correlationId + "-2", cancellationToken)
                .ConfigureAwait(false);

            return new WrapperProbeResult(
                sameInstance,
                RuntimeHelpers.GetHashCode(first),
                RuntimeHelpers.GetHashCode(second),
                firstResult,
                secondResult);
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }
    }
}

public sealed record WrapperProbeResult(
    bool SameInstance,
    int FirstIdentityHash,
    int SecondIdentityHash,
    InventoryCallResult First,
    InventoryCallResult Second);
