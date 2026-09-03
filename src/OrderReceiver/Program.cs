using Dapr.Client;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDaprClient();

var app = builder.Build();
app.UseCloudEvents();

app.MapPost("/orders", async (Order order, DaprClient dapr, ILogger<Program> log) =>
{
    log.LogInformation("Received order {OrderId} for {Product} x{Quantity}", order.OrderId, order.Product, order.Quantity);
    await dapr.PublishEventAsync("orderpubsub", "orders", order);
    return Results.Accepted();
});

app.MapGet("/health", () => Results.Ok());

app.Run();

record Order(string OrderId, string Product, int Quantity, string CustomerId);
