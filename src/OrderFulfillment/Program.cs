using Dapr;
using Dapr.Client;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddDapr();
builder.Services.AddDaprClient();

var app = builder.Build();
app.UseCloudEvents();
app.MapControllers();
app.MapSubscribeHandler();

app.MapPost("/fulfillment", [Topic("orderpubsub", "fulfillment")] async (FulfillmentEvent ev, DaprClient dapr, ILogger<Program> log) =>
{
    log.LogInformation("Fulfilling order {OrderId}: {Product} x{Quantity} for customer {CustomerId}",
        ev.OrderId, ev.Product, ev.Quantity, ev.CustomerId);

    var state = new FulfillmentState(ev.OrderId, "Fulfilled", DateTime.UtcNow);
    await dapr.SaveStateAsync("orderstate", $"fulfillment-{ev.OrderId}", state);

    log.LogInformation("Order {OrderId} fulfilled", ev.OrderId);
    return Results.Ok();
});

app.Run();

record FulfillmentEvent(string OrderId, string Product, int Quantity, string CustomerId);
record FulfillmentState(string OrderId, string Status, DateTime FulfilledAt);
