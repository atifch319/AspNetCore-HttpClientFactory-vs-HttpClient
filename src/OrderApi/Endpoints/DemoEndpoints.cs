using OrderApi.Clients;

namespace OrderApi.Endpoints;

public static class DemoEndpoints
{
    public static WebApplication MapDemoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/demo")
            .WithTags("Lifetime comparison")
            .WithDescription("IHttpClientFactory manages handlers. Connection reuse is what matters.");

        group.MapGet("/per-request/stock/{sku}", async (string sku, PerRequestInventoryClient client, CancellationToken cancellationToken) =>
            await WriteStockAsync(PerRequestInventoryClient.StrategyName, StrategyNotes.PerRequest, sku, client.GetStockAsync, cancellationToken).ConfigureAwait(false))
            .WithName("DemoPerRequestStock")
            .WithSummary("Calls inventory with a new HttpClient and handler, then disposes both.");

        group.MapGet("/factory/stock/{sku}", async (string sku, FactoryInventoryClient client, CancellationToken cancellationToken) =>
            await WriteStockAsync(FactoryInventoryClient.StrategyName, StrategyNotes.Factory, sku, client.GetStockAsync, cancellationToken).ConfigureAwait(false))
            .WithName("DemoFactoryStock")
            .WithSummary("Calls inventory with a new factory-issued HttpClient wrapper and a pooled handler.");

        group.MapGet("/long-lived/stock/{sku}", async (string sku, LongLivedInventoryClient client, CancellationToken cancellationToken) =>
            await WriteStockAsync(LongLivedInventoryClient.StrategyName, StrategyNotes.LongLived, sku, client.GetStockAsync, cancellationToken).ConfigureAwait(false))
            .WithName("DemoLongLivedStock")
            .WithSummary("Calls inventory with the process-wide HttpClient and SocketsHttpHandler.");

        group.MapGet("/named/wrappers/{sku}", async (string sku, NamedInventoryGateway gateway, CancellationToken cancellationToken) =>
        {
            try
            {
                var probe = await gateway.ProbeAsync(sku, Guid.NewGuid().ToString("n"), cancellationToken).ConfigureAwait(false);
                return Results.Ok(new NamedWrapperResponse(
                    StrategyNotes.Named,
                    probe.SameInstance,
                    probe.FirstIdentityHash,
                    probe.SecondIdentityHash,
                    ToPayload(sku, probe.First),
                    ToPayload(sku, probe.Second)));
            }
            catch (Exception ex) when (ex is InventoryCallException or InventoryTimeoutException or HttpRequestException)
            {
                return Problem(ex);
            }
        })
        .WithName("DemoNamedWrappers")
        .WithSummary("Creates two named clients and reports whether the wrappers and handler pipelines match.");

        if (app.Configuration.GetValue("DnsSimulation:Enabled", true))
        {
            MapDnsEndpoints(group);
        }

        return app;
    }

    private static void MapDnsEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/dns/endpoint", (EndpointDirectory directory) =>
            Results.Ok(new DnsEndpointResponse(directory.Current.AbsoluteUri, StrategyNotes.DnsSimulation)))
            .WithName("GetSimulatedEndpoint")
            .WithSummary("Returns the IP endpoint that the next new connection will dial.");

        group.MapPut("/dns/endpoint", (DnsEndpointUpdate? update, EndpointDirectory directory) =>
        {
            if (update is null || string.IsNullOrWhiteSpace(update.BaseAddress))
            {
                return Results.Problem(title: "Invalid endpoint", detail: "baseAddress is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var uri = EndpointDirectory.RequireIpEndpoint(BaseAddressRules.Normalize(update.BaseAddress));
                directory.Set(uri);
                return Results.Ok(new DnsEndpointResponse(directory.Current.AbsoluteUri, StrategyNotes.DnsSimulation));
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(title: "Invalid endpoint", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("SetSimulatedEndpoint")
        .WithSummary("Points the local endpoint directory at another loopback IP and port.");

        group.MapGet("/dns/stock/{sku}", async (string sku, DnsSimulationClient client, CancellationToken cancellationToken) =>
            await WriteStockAsync(DnsSimulationClient.StrategyName, StrategyNotes.DnsSimulation, sku, client.GetStockAsync, cancellationToken).ConfigureAwait(false))
            .WithName("DemoDnsStock")
            .WithSummary("Calls inventory through the endpoint-directory simulation.");
    }

    private static async Task<IResult> WriteStockAsync(
        string strategy,
        string note,
        string sku,
        Func<string, string, CancellationToken, Task<InventoryCallResult>> call,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await call(sku, Guid.NewGuid().ToString("n"), cancellationToken).ConfigureAwait(false);
            return Results.Ok(ToResponse(strategy, sku, result, success: true, error: null, note));
        }
        catch (InventoryCallException ex)
        {
            var status = ex.StatusCode == HttpStatusCode.NotFound
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status502BadGateway;
            var response = new DemoStockResponse(
                strategy,
                sku,
                (int)ex.StatusCode,
                null,
                ex.HandlerInstanceId,
                ex.RequestUri.AbsoluteUri,
                false,
                ex.Body,
                note);
            return Results.Json(response, statusCode: status);
        }
        catch (Exception ex) when (ex is InventoryTimeoutException or HttpRequestException)
        {
            return Problem(ex);
        }
    }

    private static DemoStockResponse ToResponse(
        string strategy,
        string sku,
        InventoryCallResult result,
        bool success,
        string? error,
        string note) =>
        new(
            strategy,
            sku,
            result.StatusCode,
            result.InventoryInstanceId,
            result.HandlerInstanceId,
            result.RequestUri.AbsoluteUri,
            success,
            error,
            note);

    private static DemoStockResponse ToPayload(string sku, InventoryCallResult result) =>
        new(NamedInventoryGateway.Name, sku, result.StatusCode, result.InventoryInstanceId, result.HandlerInstanceId, result.RequestUri.AbsoluteUri, true, null, StrategyNotes.Named);

    private static IResult Problem(Exception exception) => exception switch
    {
        InventoryTimeoutException timeout => Results.Problem(
            title: "Inventory timeout",
            detail: timeout.Message,
            statusCode: StatusCodes.Status504GatewayTimeout),
        HttpRequestException http => Results.Problem(
            title: "Inventory connection failed",
            detail: http.Message,
            statusCode: StatusCodes.Status502BadGateway),
        _ => Results.Problem(title: "Inventory call failed", detail: exception.Message, statusCode: StatusCodes.Status502BadGateway)
    };
}

public sealed record DemoStockResponse(
    string Strategy,
    string Sku,
    int DownstreamStatus,
    string? InventoryInstanceId,
    string? HandlerInstanceId,
    string RequestUri,
    bool Success,
    string? Error,
    string Note);

public sealed record NamedWrapperResponse(
    string Note,
    bool WrappersAreSameInstance,
    int FirstWrapperIdentityHash,
    int SecondWrapperIdentityHash,
    DemoStockResponse First,
    DemoStockResponse Second);

public sealed record DnsEndpointUpdate(string BaseAddress);

public sealed record DnsEndpointResponse(string Current, string Note);
