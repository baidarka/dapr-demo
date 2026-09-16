using Dapr.Client;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDaprClient();

var app = builder.Build();
app.UseCloudEvents();

app.MapPost("/orders", async (Order order, DaprClient dapr, ILogger<Program> log) =>
{
    log.LogInformation("Received order {OrderId} for {Product} x{Quantity}", order.OrderId, order.Product, order.Quantity);

    try
    {
        // Publish is covered by the "orderpubsub" outbound retry/circuit-breaker
        // policy in resiliency.yaml, so transient broker hiccups are retried
        // by the sidecar before this call fails.
        await dapr.PublishEventAsync("orderpubsub", "orders", order);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Failed to publish order {OrderId} after retries; caller should retry the request", order.OrderId);
        return Results.Problem("Order could not be accepted, please retry.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Accepted();
});

app.MapGet("/health", () => Results.Ok());

app.Run();

record Order(string OrderId, string Product, int Quantity, string CustomerId);
