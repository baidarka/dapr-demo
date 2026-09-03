# DAPR Demo

A .NET 9 Dapr demo on Kubernetes: an order-processing pipeline that receives an order, processes it (saves state), and triggers fulfillment — demonstrating pub/sub messaging and state management via the Dapr sidecar pattern.

## Architecture

Three ASP.NET Core Minimal API services communicate exclusively through the Dapr sidecar:

```
HTTP Client
    │  POST /orders
    ▼
OrderReceiver  ──publish──▶  [orderpubsub / orders topic]
                                        │
                                        ▼
                              OrderProcessor  ──save──▶  [orderstate]
                                        │
                                        └──publish──▶  [orderpubsub / fulfillment topic]
                                                                    │
                                                                    ▼
                                                         OrderFulfillment  ──save──▶  [orderstate]
```

| Service | Role |
|---|---|
| `OrderReceiver` | HTTP entry point — accepts `POST /orders`, publishes to the `orders` pub/sub topic |
| `OrderProcessor` | Subscribes to `orders`, saves order state, publishes to the `fulfillment` topic |
| `OrderFulfillment` | Subscribes to `fulfillment`, records the final fulfillment state |

**Dapr components:**
- `orderpubsub` — Redis-backed pub/sub broker
- `orderstate` — Redis-backed state store

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9)
- [Dapr CLI](https://docs.dapr.io/getting-started/install-dapr-cli/)
- Docker (for Kubernetes deploy)
- A local Redis instance on port 6379, or a Kubernetes cluster

## Local Development

Initialize Dapr locally (installs the Dapr runtime and starts a local Redis container):

```bash
dapr init
```

Start all three services with their sidecars in one command:

```bash
dapr run -f dapr.yaml
```

Send a test order to the receiver (sidecar HTTP port defaults to 3500, app port is 5010):

```bash
curl -X POST http://localhost:5010/orders \
  -H "Content-Type: application/json" \
  -d '{"orderId":"1","product":"Widget","quantity":5,"customerId":"cust-42"}'
```

Watch the logs in the terminal — you should see each service pick up and forward the message in sequence.

Query saved state directly via the Dapr sidecar:

```bash
# Dapr HTTP port for order-processor in multi-app run defaults to 3501
curl http://localhost:3501/v1.0/state/orderstate/order-1
```

## Kubernetes Deploy

Install Dapr into your cluster (one-time setup):

```bash
dapr init --kubernetes --wait
```

Build the service images:

```bash
docker build -t order-receiver:latest    -f src/OrderReceiver/Dockerfile    src/OrderReceiver/
docker build -t order-processor:latest   -f src/OrderProcessor/Dockerfile   src/OrderProcessor/
docker build -t order-fulfillment:latest -f src/OrderFulfillment/Dockerfile src/OrderFulfillment/
```

If using a local cluster (e.g. kind), load the images:

```bash
kind load docker-image order-receiver:latest order-processor:latest order-fulfillment:latest
```

Apply all manifests:

```bash
kubectl apply -f deploy/namespace.yaml
kubectl apply -f deploy/redis.yaml
kubectl apply -f components/k8s/
kubectl apply -f deploy/
```

Forward the receiver port to test:

```bash
kubectl port-forward -n dapr-demo svc/order-receiver 5010:80
curl -X POST http://localhost:5010/orders \
  -H "Content-Type: application/json" \
  -d '{"orderId":"1","product":"Widget","quantity":5,"customerId":"cust-42"}'
```

Check logs across services:

```bash
kubectl logs -n dapr-demo -l app=order-receiver   -c order-receiver   -f
kubectl logs -n dapr-demo -l app=order-processor  -c order-processor  -f
kubectl logs -n dapr-demo -l app=order-fulfillment -c order-fulfillment -f
```

## Repository Layout

```
src/
  OrderReceiver/      # Entry point: accepts HTTP orders, publishes to pub/sub
  OrderProcessor/     # Subscribes to 'orders', saves state, publishes to 'fulfillment'
  OrderFulfillment/   # Subscribes to 'fulfillment', records final state
components/
  local/              # Dapr component YAML for local dev (Redis on localhost)
  k8s/                # Dapr component YAML for Kubernetes
deploy/               # Kubernetes manifests (namespace, Redis, service Deployments)
dapr.yaml             # Multi-App Run: starts all three services locally
dapr-demo.sln         # .NET solution file
```

## Extending the Pipeline

To add a new service:

1. Scaffold and add the Dapr SDK:
   ```bash
   dotnet new webapi -n MyService -o src/MyService --no-openapi
   dotnet add src/MyService/MyService.csproj package Dapr.AspNetCore
   dotnet sln add src/MyService/MyService.csproj
   ```

2. In `Program.cs`, register Dapr and add a subscription endpoint:
   ```csharp
   builder.Services.AddControllers().AddDapr();
   builder.Services.AddDaprClient();
   // ...
   app.UseCloudEvents();
   app.MapControllers();
   app.MapSubscribeHandler();

   app.MapPost("/my-topic", [Topic("orderpubsub", "my-topic")] async (MyMessage msg, DaprClient dapr) =>
   {
       // process and optionally publish to the next topic
   });
   ```

3. Add a `Dockerfile` (copy from an existing service).

4. Add an entry in `dapr.yaml` for local dev and a Deployment manifest in `deploy/` for Kubernetes.
