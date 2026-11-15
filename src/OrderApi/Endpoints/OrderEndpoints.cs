using OrderApi.Clients;
using OrderApi.Orders;

namespace OrderApi.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders").WithTags("Orders");

        group.MapPost("/", async (PlaceOrderRequest? request, OrderService orders, CancellationToken cancellationToken) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Sku) || request.Quantity <= 0)
            {
                return Results.Problem(
                    title: "Invalid order",
                    detail: "SKU is required and quantity must be positive.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var order = await orders.PlaceAsync(request.Sku, request.Quantity, cancellationToken).ConfigureAwait(false);
                return Results.Created($"/api/orders/{order.Id}", order);
            }
            catch (InventoryCallException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return Results.Problem(title: "SKU was not found", detail: ex.Body, statusCode: StatusCodes.Status404NotFound);
            }
            catch (InventoryCallException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                return Results.Problem(title: "Insufficient stock", detail: ex.Body, statusCode: StatusCodes.Status409Conflict);
            }
            catch (InventoryCallException ex)
            {
                return Results.Problem(title: "Inventory call failed", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
            catch (InventoryTimeoutException ex)
            {
                return Results.Problem(title: "Inventory timeout", detail: ex.Message, statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (HttpRequestException ex)
            {
                return Results.Problem(title: "Inventory connection failed", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .WithName("PlaceOrder")
        .WithSummary("Reserves stock through the typed IHttpClientFactory client and stores the order.");

        group.MapGet("/{id}", (string id, OrderStore store) =>
        {
            return store.TryGet(id, out var order) && order is not null
                ? Results.Ok(order)
                : Results.Problem(title: "Order was not found", statusCode: StatusCodes.Status404NotFound);
        })
        .WithName("GetOrder")
        .WithSummary("Returns an order created by this process.");

        return app;
    }
}

public sealed record PlaceOrderRequest(string Sku, int Quantity);
