using Dapr;
using Dapr.Client;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddDapr();
builder.Services.AddDaprClient();

var app = builder.Build();
app.UseCloudEvents();
app.MapControllers();
app.MapSubscribeHandler();

app.MapPost("/fulfillment", [Topic("orderpubsub", "fulfillment", "fulfillment-deadletter", false)] async (FulfillmentEvent ev, DaprClient dapr, ILogger<Program> log) =>
{
    var stateKey = $"fulfillment-{ev.OrderId}";

    // Same at-least-once caveat as OrderProcessor: guard against reprocessing
    // a redelivered fulfillment event.
    var (existing, etag) = await dapr.GetStateAndETagAsync<FulfillmentState>("orderstate", stateKey);
    if (existing is not null)
    {
        log.LogInformation("Order {OrderId} already fulfilled; skipping duplicate delivery", ev.OrderId);
        return Results.Ok();
    }

    log.LogInformation("Fulfilling order {OrderId}: {Product} x{Quantity} for customer {CustomerId}",
        ev.OrderId, ev.Product, ev.Quantity, ev.CustomerId);

    try
    {
        var state = new FulfillmentState(ev.OrderId, "Fulfilled", DateTime.UtcNow);
        var saved = await dapr.TrySaveStateAsync("orderstate", stateKey, state, etag);
        if (!saved)
        {
            log.LogInformation("Order {OrderId} was concurrently fulfilled by another delivery; skipping", ev.OrderId);
            return Results.Ok();
        }

        log.LogInformation("Order {OrderId} fulfilled", ev.OrderId);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        // Non-2xx tells Dapr to RETRY (per the "orderpubsub" inbound retry
        // policy); once exhausted it routes to fulfillment-deadletter.
        log.LogError(ex, "Transient failure fulfilling order {OrderId}; will retry", ev.OrderId);
        return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/fulfillment/deadletter", [Topic("orderpubsub", "fulfillment-deadletter")] async (FulfillmentEvent ev, DaprClient dapr, ILogger<Program> log) =>
{
    log.LogError("Fulfillment for order {OrderId} exhausted retries and was dead-lettered", ev.OrderId);
    var state = new FulfillmentState(ev.OrderId, "Failed", DateTime.UtcNow);
    await dapr.SaveStateAsync("orderstate", $"fulfillment-{ev.OrderId}", state);
    return Results.Ok();
});

app.Run();

record FulfillmentEvent(string OrderId, string Product, int Quantity, string CustomerId);
record FulfillmentState(string OrderId, string Status, DateTime FulfilledAt);
