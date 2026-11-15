using OrderApi.Clients;
using Swashbuckle.AspNetCore;
using OrderApi.Endpoints;
using OrderApi.Orders;

namespace OrderApi;

public sealed class Program
{
    public static void Main(string[] args) => Build(args).Run();

    public static WebApplication Build(string[]? args = null)
    {
        var builder = WebApplication.CreateBuilder(args ?? []);

        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Order API";
                document.Info.Version = "v1";
                document.Info.Description =
                    "Compares HttpClient lifetimes. IHttpClientFactory manages handlers. Connection reuse is what matters.";
                return Task.CompletedTask;
            });
        });

        builder.Services.AddInventoryClients(builder.Configuration);
        builder.Services.AddSingleton<OrderStore>();
        builder.Services.AddScoped<OrderService>();

        var app = builder.Build();

        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "Order API v1");
            options.RoutePrefix = "swagger";
        });

        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "order-api" }))
            .WithName("OrderHealth")
            .WithTags("Health");

        app.MapOrderEndpoints();
        app.MapDemoEndpoints();
        return app;
    }
}
