# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

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

**Dapr components** (defined in `components/`):
- `orderpubsub` — Redis-backed pub/sub broker
- `orderstate` — Redis-backed state store

**Key Dapr patterns used**:
- `DaprClient.PublishEventAsync` for publishing messages
- `[Topic("orderpubsub", "topic-name")]` attribute + `app.MapSubscribeHandler()` for subscriptions
- `DaprClient.SaveStateAsync` for state persistence

## Commands

### Build
```bash
dotnet build
```

### Local dev (requires Dapr CLI + a local Redis on port 6379)
```bash
# Start all three services with their Dapr sidecars
dapr run -f dapr.yaml

# Send a test order
curl -X POST http://localhost:5010/orders \
  -H "Content-Type: application/json" \
  -d '{"orderId":"1","product":"Widget","quantity":5,"customerId":"cust-42"}'
```

### Kubernetes deploy
```bash
# Prerequisites: Dapr must be installed in the cluster
# dapr init --kubernetes

kubectl apply -f deploy/namespace.yaml
kubectl apply -f deploy/redis.yaml
kubectl apply -f components/k8s/
kubectl apply -f deploy/

# Build and load images (example for kind)
docker build -t order-receiver:latest -f src/OrderReceiver/Dockerfile src/OrderReceiver/
docker build -t order-processor:latest -f src/OrderProcessor/Dockerfile src/OrderProcessor/
docker build -t order-fulfillment:latest -f src/OrderFulfillment/Dockerfile src/OrderFulfillment/
```

### Query state directly via Dapr sidecar (local dev)
```bash
# Dapr sidecar for order-processor runs on port 3501 by default in multi-app run
curl http://localhost:3501/v1.0/state/orderstate/order-1
```

## File Layout

```
src/
  OrderReceiver/    # Entry point: accepts HTTP orders, publishes to pub/sub
  OrderProcessor/   # Subscribes to 'orders', saves state, publishes to 'fulfillment'
  OrderFulfillment/ # Subscribes to 'fulfillment', records final state
components/
  local/            # Dapr component YAML for local dev (Redis on localhost)
  k8s/              # Dapr component YAML for Kubernetes
deploy/             # Kubernetes manifests (namespace, Redis, service deployments)
dapr.yaml           # Multi-App Run: starts all three services locally
```

## Extending

To add a new service to the pipeline:
1. `dotnet new webapi -n MyService -o src/MyService --no-openapi`
2. `dotnet add src/MyService/MyService.csproj package Dapr.AspNetCore`
3. Add `AddControllers().AddDapr()`, `MapSubscribeHandler()`, and a `[Topic]` endpoint in `Program.cs`
4. Add an entry in `dapr.yaml` and a Deployment in `deploy/`
