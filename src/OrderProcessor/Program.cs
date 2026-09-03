using Dapr;
using Dapr.Client;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddDapr();
builder.Services.AddDaprClient();

var app = builder.Build();
app.UseCloudEvents();
app.MapControllers();
app.MapSubscribeHandler();

app.MapPost("/orders", [Topic("orderpubsub", "orders")] async (Order order, DaprClient dapr, ILogger<Program> log) =>
{
    log.LogInformation("Processing order {OrderId}", order.OrderId);

    var state = new OrderState(order.OrderId, order.Product, order.Quantity, order.CustomerId, "Processing", DateTime.UtcNow);
    await dapr.SaveStateAsync("orderstate", $"order-{order.OrderId}", state);

    var fulfillment = new FulfillmentEvent(order.OrderId, order.Product, order.Quantity, order.CustomerId);
    await dapr.PublishEventAsync("orderpubsub", "fulfillment", fulfillment);

    log.LogInformation("Order {OrderId} saved and forwarded to fulfillment", order.OrderId);
    return Results.Ok();
});

app.Run();

record Order(string OrderId, string Product, int Quantity, string CustomerId);
record OrderState(string OrderId, string Product, int Quantity, string CustomerId, string Status, DateTime UpdatedAt);
record FulfillmentEvent(string OrderId, string Product, int Quantity, string CustomerId);
