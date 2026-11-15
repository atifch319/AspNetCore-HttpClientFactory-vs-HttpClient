using Swashbuckle.AspNetCore;

namespace InventoryApi;

public sealed class InstanceOptions
{
    public const string SectionName = "Instance";

    public string Id
    {
        get => field;
        set => field = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Instance id is required.", nameof(value))
            : value.Trim();
    } = "inventory-a";
}

public sealed record ReservationRequest(string Sku, int Quantity);

public sealed class Program
{
    public static void Main(string[] args) => Build(args).Run();

    public static WebApplication Build(string[]? args = null)
    {
        var builder = WebApplication.CreateBuilder(args ?? []);
        builder.Services.AddOptions<InstanceOptions>()
            .Bind(builder.Configuration.GetSection(InstanceOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Id), "Instance:Id is required.")
            .ValidateOnStart();
        builder.Services.AddSingleton<InventoryStore>();
        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Inventory API";
                document.Info.Version = "v1";
                document.Info.Description = "Local inventory simulator used by the HttpClient lifetime sample. It does not call the public internet.";
                return Task.CompletedTask;
            });
        });

        var app = builder.Build();
        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "Inventory API v1");
            options.RoutePrefix = "swagger";
        });

        app.MapGet("/health", (Microsoft.Extensions.Options.IOptions<InstanceOptions> options) =>
            Results.Ok(new { status = "ok", service = "inventory-api", instanceId = options.Value.Id }))
            .WithName("InventoryHealth")
            .WithTags("Health");

        app.MapGet("/api/inventory/{sku}", (string sku, InventoryStore store, Microsoft.Extensions.Options.IOptions<InstanceOptions> options) =>
        {
            if (!store.TryGet(sku, out var quantity))
            {
                return Results.Problem(title: "SKU was not found", statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Ok(new
            {
                sku,
                quantityOnHand = quantity,
                instanceId = options.Value.Id
            });
        })
        .WithName("GetStock")
        .WithTags("Inventory");

        app.MapPost("/api/reservations", (ReservationRequest? request, InventoryStore store, Microsoft.Extensions.Options.IOptions<InstanceOptions> options) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Sku) || request.Quantity <= 0)
            {
                return Results.Problem(
                    title: "Invalid reservation",
                    detail: "SKU is required and quantity must be positive.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var result = store.TryReserve(request.Sku, request.Quantity);
            return result.Status switch
            {
                ReserveStatus.NotFound => Results.Problem(title: "SKU was not found", statusCode: StatusCodes.Status404NotFound),
                ReserveStatus.Insufficient => Results.Problem(
                    title: "Insufficient stock",
                    detail: $"On hand: {result.Remaining}.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.Ok(new
                {
                    reservationId = result.ReservationId,
                    sku = request.Sku.Trim(),
                    quantity = request.Quantity,
                    remaining = result.Remaining,
                    instanceId = options.Value.Id
                })
            };
        })
        .WithName("ReserveStock")
        .WithTags("Inventory");

        return app;
    }
}
