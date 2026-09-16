using Dapr;
using Dapr.Client;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddDapr();
builder.Services.AddDaprClient();

var app = builder.Build();
app.UseCloudEvents();
app.MapControllers();
app.MapSubscribeHandler();

app.MapPost("/orders", [Topic("orderpubsub", "orders", "orders-deadletter", false)] async (Order order, DaprClient dapr, ILogger<Program> log) =>
{
    var stateKey = $"order-{order.OrderId}";

    // Redis streams (via processingTimeout/redeliverInterval) and the sidecar's
    // inbound retry policy both redeliver this message if a prior attempt never
    // acked it, so the same order can arrive more than once. Check existing
    // state first so a redelivery doesn't re-publish a duplicate fulfillment event.
    var (existing, etag) = await dapr.GetStateAndETagAsync<OrderState>("orderstate", stateKey);
    if (existing is not null)
    {
        log.LogInformation("Order {OrderId} already processed (status {Status}); skipping duplicate delivery", order.OrderId, existing.Status);
        return Results.Ok();
    }

    log.LogInformation("Processing order {OrderId}", order.OrderId);

    try
    {
        var state = new OrderState(order.OrderId, order.Product, order.Quantity, order.CustomerId, "Processing", DateTime.UtcNow);
        var saved = await dapr.TrySaveStateAsync("orderstate", stateKey, state, etag);
        if (!saved)
        {
            log.LogInformation("Order {OrderId} was concurrently processed by another delivery; skipping", order.OrderId);
            return Results.Ok();
        }

        var fulfillment = new FulfillmentEvent(order.OrderId, order.Product, order.Quantity, order.CustomerId);
        await dapr.PublishEventAsync("orderpubsub", "fulfillment", fulfillment);

        log.LogInformation("Order {OrderId} saved and forwarded to fulfillment", order.OrderId);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        // Non-2xx here tells Dapr to RETRY the delivery (subject to the
        // "orderpubsub" inbound retry policy) rather than drop the message.
        // Once retries are exhausted, Dapr routes it to orders-deadletter.
        log.LogError(ex, "Transient failure processing order {OrderId}; will retry", order.OrderId);
        return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/orders/deadletter", [Topic("orderpubsub", "orders-deadletter")] async (Order order, DaprClient dapr, ILogger<Program> log) =>
{
    // Last resort: persist the failure instead of silently dropping the message,
    // so it stays visible in state for investigation/manual replay.
    log.LogError("Order {OrderId} exhausted retries and was dead-lettered", order.OrderId);
    var state = new OrderState(order.OrderId, order.Product, order.Quantity, order.CustomerId, "Failed", DateTime.UtcNow);
    await dapr.SaveStateAsync("orderstate", $"order-{order.OrderId}", state);
    return Results.Ok();
});

app.Run();

record Order(string OrderId, string Product, int Quantity, string CustomerId);
record OrderState(string OrderId, string Product, int Quantity, string CustomerId, string Status, DateTime UpdatedAt);
record FulfillmentEvent(string OrderId, string Product, int Quantity, string CustomerId);
