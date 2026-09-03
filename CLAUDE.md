# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Dapr (Distributed Application Runtime) demo repository. This code demoes the use of DAPR to receive a message, process the message and trigger a separate process.

## Tooling

Use .Net, and Kubernetes.
Take inspiration from <https://github.com/dapr/samples/tree/master/>

## Dapr

Dapr provides building blocks for distributed applications: service invocation, pub/sub messaging, state management, secret stores, and bindings. Services interact with Dapr via its sidecar (HTTP on port 3500, gRPC on port 50001 by default).

The Dapr CLI is used to run services locally with the sidecar attached:

```bash
dapr run --app-id <service-name> --app-port <port> -- <start-command>
```

For multi-service orchestration, a `dapr.yaml` (Multi-App Run) file at the repo root defines all services and their Dapr configuration.
