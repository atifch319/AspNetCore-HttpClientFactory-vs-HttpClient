# AspNetCore-HttpClientFactory-vs-HttpClient

IHttpClientFactory manages handlers. Connection reuse is what matters.

This is a .NET 10 / C# 14 sample that calls one local inventory API three ways:

1. **Educational anti-pattern.** A new `HttpClient` and a new `SocketsHttpHandler` for every outgoing request. The handler is disposed with the call, so its pool is discarded. That is a poor default for a long-running service. It is not a claim that every `new HttpClient()` is wrong: a short-lived process, or one client kept for the life of the process, is a different design.
2. **Usually a good fit for ASP.NET Core.** `IHttpClientFactory` returns a new `HttpClient` wrapper from each `CreateClient` call and pools the handler underneath it. Named and typed clients are both included.
3. **Also a valid production option.** One long-lived `HttpClient` whose `SocketsHttpHandler.PooledConnectionLifetime` bounds how long a pooled connection stays reusable. That lifetime does not abort a request that is already running.

`HttpClient` does not monitor DNS TTL. A hostname is resolved when a connection is established. The DNS section below is a local endpoint directory, not operating-system DNS.

## Projects

```
src/OrderApi          Order API, three strategies, named client, DNS simulation
src/InventoryApi      Loopback inventory simulator
tools/LoadGenerator   Local load generator with fixed safety caps
tests/HttpClientLifetimes.Tests
docs                  Lifetime, pooling, DNS, tradeoffs, and load-test method
scripts               load-test.ps1 and load-test.sh
```

| URL | Process |
|---|---|
| http://127.0.0.1:5080 | Inventory API, instance `inventory-a` |
| http://127.0.0.1:5070 | Order API |
| http://127.0.0.1:5070/swagger | Swagger UI |
| http://127.0.0.1:5070/openapi/v1.json | OpenAPI document |
| http://127.0.0.1:5080/swagger | Inventory Swagger UI |

Seeded SKUs are `WIDGET-1` (10000 on hand) and `GADGET-2` (25 on hand). Configuration uses `127.0.0.1` only. There are no secrets in the repo.

## Fresh clone

Install the .NET SDK 10.0.401 or a later 10.0 feature band. `global.json` selects that band. The APIs and tests do not call the public internet.

```powershell
git clone <repository-url> AspNetCore-HttpClientFactory-vs-HttpClient
cd AspNetCore-HttpClientFactory-vs-HttpClient
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

```bash
git clone <repository-url> AspNetCore-HttpClientFactory-vs-HttpClient
cd AspNetCore-HttpClientFactory-vs-HttpClient
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

Start the inventory API, then the order API, from the repository root. Use two terminals. `dotnet run` reads `Properties/launchSettings.json` and binds the loopback ports in the table above.

```powershell
dotnet run --project src/InventoryApi/InventoryApi.csproj -c Release
dotnet run --project src/OrderApi/OrderApi.csproj -c Release
```

```bash
dotnet run --project src/InventoryApi/InventoryApi.csproj -c Release
dotnet run --project src/OrderApi/OrderApi.csproj -c Release
```

## The three approaches

| Approach | What it owns | Endpoint |
|---|---|---|
| Educational anti-pattern | A new `HttpClient` and a new `SocketsHttpHandler` per call, then both are disposed | `GET /api/demo/per-request/stock/{sku}` |
| `IHttpClientFactory` typed client | A new wrapper per resolution; the factory pools the handler | `GET /api/demo/factory/stock/{sku}` |
| Long-lived client | One process-wide `HttpClient` and `SocketsHttpHandler` with `PooledConnectionLifetime` | `GET /api/demo/long-lived/stock/{sku}` |

Related routes, still on the order API:

| Route | What it shows |
|---|---|
| `GET /api/demo/named/wrappers/{sku}` | Two named `CreateClient` calls. The wrappers are different objects. While the current handler is active, the handler ids match. |
| `POST /api/orders` | Places an order through the typed factory client. Body: `{"sku":"WIDGET-1","quantity":1}`. |
| `GET /api/orders/{id}` | Returns an order stored in this process. |
| `GET /health` | Process health on both APIs. |
| `GET /api/inventory/{sku}` | Inventory stock, including `instanceId`. |
| `POST /api/reservations` | Inventory reservation. |

The handler instance id in a demo response identifies the handler pipeline. It is not a TCP connection id. Call the per-request route twice and the ids differ. Call the factory route twice, or the long-lived route twice, and the id stays the same while that pipeline is current. `wrappersAreSameInstance` on the named route is false: `CreateClient` does not return one shared `HttpClient`.

```powershell
Invoke-RestMethod http://127.0.0.1:5070/api/demo/per-request/stock/WIDGET-1
Invoke-RestMethod http://127.0.0.1:5070/api/demo/factory/stock/WIDGET-1
Invoke-RestMethod http://127.0.0.1:5070/api/demo/long-lived/stock/WIDGET-1
Invoke-RestMethod http://127.0.0.1:5070/api/demo/named/wrappers/WIDGET-1
Invoke-RestMethod -Method Post http://127.0.0.1:5070/api/orders -ContentType application/json -Body '{"sku":"WIDGET-1","quantity":1}'
```

```bash
curl -s http://127.0.0.1:5070/api/demo/per-request/stock/WIDGET-1
curl -s http://127.0.0.1:5070/api/demo/factory/stock/WIDGET-1
curl -s http://127.0.0.1:5070/api/demo/long-lived/stock/WIDGET-1
curl -s http://127.0.0.1:5070/api/demo/named/wrappers/WIDGET-1
curl -s -X POST http://127.0.0.1:5070/api/orders -H "Content-Type: application/json" -d '{"sku":"WIDGET-1","quantity":1}'
```

## Tests

The suite has 31 tests. A publication check on 15 November 2026 recorded 31 passed, 0 failed, and 0 skipped. That is a result from that run, not a promise about another machine.

Fake `HttpMessageHandler` tests do not open TCP connections. They verify per-request handler creation and disposal, including after an HTTP error, relative URL joining, the leading-slash base-path rule, HTTP 404 and 500 handling, caller cancellation as distinct from `HttpClient.Timeout`, one correlation id per parallel call on a shared client, and double disposal of the long-lived client. Factory tests verify that `CreateClient` returns distinct wrappers, that those wrappers share the current handler, that `HandlerLifetime` does not replace a handler still held by an existing client, and that a singleton capturing a typed client keeps the old handler after rotation. Load-option tests reject non-loopback URLs and concurrency or duration above the local caps. The endpoint directory rejects hostnames and URLs without an explicit port. The inventory store test checks that concurrent reservations do not oversell `GADGET-2`.

Loopback TCP tests use real sockets. They verify one new connection per per-request call, one connection reused by the long-lived client for sequential requests, one connection reused across distinct factory wrappers, and that a later sequential burst does not open one connection per request. Cookie tests use a real server: a rotated handler starts with an empty `CookieContainer`, and a client that still holds the old handler keeps its cookies. The order, inventory, three-strategy, and short load-generator tests host Kestrel on an ephemeral loopback port.

One test loads the order API in the in-memory test host and checks `/health`, `/openapi/v1.json`, and `/swagger/index.html`. No test calls the public internet. No test uses operating-system DNS.

## Endpoint-directory simulation

This section is a local simulation. It is not operating-system DNS, and the tests do not resolve a public name or wait on a DNS TTL.

`HttpClient` resolves a hostname when it opens a connection. It does not monitor DNS TTL. In this demo the request URI stays on the logical host `inventory.local`, which is only the connection-pool key. `ConnectCallback` dials the IP and port currently stored in an in-memory `EndpointDirectory`. The directory rejects hostnames. A pooled connection keeps the peer it already has until a new connection is established. `PooledConnectionLifetime` does not abort a request that is already running.

The simulation lifetime in `appsettings.json` is 10 seconds so a walkthrough does not wait on the 120 second production-style setting. See [docs/DnsRefresh.md](docs/DnsRefresh.md).

Terminal 3, a second inventory instance:

```powershell
$env:Instance__Id = "inventory-b"
dotnet run --project src/InventoryApi/InventoryApi.csproj -c Release --no-launch-profile -- --urls http://127.0.0.1:5081
```

```bash
Instance__Id=inventory-b dotnet run --project src/InventoryApi/InventoryApi.csproj -c Release --no-launch-profile -- --urls http://127.0.0.1:5081
```

Then, against the order API started above:

```powershell
Invoke-RestMethod http://127.0.0.1:5070/api/demo/dns/stock/WIDGET-1
Invoke-RestMethod -Method Put http://127.0.0.1:5070/api/demo/dns/endpoint -ContentType application/json -Body '{"baseAddress":"http://127.0.0.1:5081/"}'
Invoke-RestMethod http://127.0.0.1:5070/api/demo/dns/stock/WIDGET-1
```

The call immediately after the directory update still reports `inventory-a` when the pooled connection is reused. A call made after the simulation's `PooledConnectionLifetime` reports `inventory-b`. An active request is not cancelled when that lifetime elapses. The automated test is the deterministic form of this sequence; it does not sleep for a fixed assertion.

## Load test

Start both APIs first. All three strategies use the same SKU, path shape, concurrency, warmup, and duration. The generator forces HTTP/1.1, refuses non-loopback URLs, and refuses concurrency above 50 or a measured duration above 30 seconds. It does not prove port exhaustion, and a loopback result is not a cross-network ranking. Method: [docs/BenchmarkMethodology.md](docs/BenchmarkMethodology.md).

Repeatable Release command:

```powershell
dotnet run --project tools/LoadGenerator/LoadGenerator.csproj -c Release -- --base-url http://127.0.0.1:5070 --concurrency 10 --duration 10 --warmup 2
powershell -File scripts/load-test.ps1 -Concurrency 10 -Duration 10 -Warmup 2
```

```bash
dotnet run --project tools/LoadGenerator/LoadGenerator.csproj -c Release -- --base-url http://127.0.0.1:5070 --concurrency 10 --duration 10 --warmup 2
bash scripts/load-test.sh --concurrency 10 --duration 10 --warmup 2
```

One local sample was collected with the shorter command below. It is not a performance guarantee, and the 10-second command above was not the command that produced it.

```powershell
powershell -File scripts/load-test.ps1 -Concurrency 4 -Duration 3 -Warmup 1
```

That process printed `.NET 10.0.12; Microsoft Windows 10.0.26300; X64`, started at `2026-11-15T13:47:13.3171886+00:00`, and called `http://127.0.0.1:5070/` for SKU `WIDGET-1` with concurrency 4, warmup 1 second, and duration 3 seconds. Warmup samples were discarded. Every request succeeded.

| Strategy | Requests | Failures | Latency ms min / mean / p50 / p95 / max |
|---|---|---|---|
| per-request | 2715 | 0 | 1.991 / 4.414 / 3.826 / 7.972 / 20.6 |
| factory | 6818 | 0 | 0.621 / 1.759 / 1.29 / 3.935 / 29.739 |
| long-lived | 12613 | 0 | 0.387 / 0.951 / 0.68 / 2.038 / 30.279 |

## Docker Compose

Optional. The inventory service and the order API are defined in `docker-compose.yml`. The DNS simulation is disabled there because it dials IP addresses, while Compose service discovery is real DNS.

```powershell
docker compose config
docker compose up --build
```

The sample's checked-in verification does not require containers to be started.

## Further reading

- [Client lifetimes](docs/ClientLifetimes.md)
- [Handler pooling](docs/HandlerPooling.md)
- [DNS and the local simulation](docs/DnsRefresh.md)
- [Production tradeoffs](docs/ProductionTradeoffs.md)
- [Load-test method](docs/BenchmarkMethodology.md)

This repository is licensed under the [MIT License](LICENSE).
