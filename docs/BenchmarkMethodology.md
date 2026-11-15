# Load test methodology

The load generator measures one local run. It does not ship a throughput, latency, CPU, memory, or socket result, and the README does not quote one. Numbers printed by a run belong to that process, on that machine, at that time.

## Identical work

Each selected strategy is called in order, never overlapping another strategy. For every strategy the generator uses:

- the same Order API base URL
- `GET /api/demo/{strategy}/stock/{sku}`
- the same SKU, default `WIDGET-1`
- the same concurrency, warmup, and measured duration
- one generator-side `SocketsHttpHandler` for the whole run

Warmup samples are discarded. The inventory call is a stock read, so the strategies are not competing to decrement one counter. `WIDGET-1` is seeded with 10000 units anyway.

The generator forces HTTP/1.1 on its own client. The order API's outbound clients also use HTTP/1.1 in this sample. A production comparison that negotiates HTTP/2 would be a different experiment.

## Command

Release build, from the repository root, with the order API and inventory API already running:

```powershell
dotnet run --project tools/LoadGenerator/LoadGenerator.csproj -c Release -- --base-url http://127.0.0.1:5070 --concurrency 10 --duration 10 --warmup 2
```

```bash
dotnet run --project tools/LoadGenerator/LoadGenerator.csproj -c Release -- --base-url http://127.0.0.1:5070 --concurrency 10 --duration 10 --warmup 2
```

`scripts/load-test.ps1` and `scripts/load-test.sh` call that project and apply the same limits.

## Safety limits

The tool rejects a base URL that is not loopback `http` or `https`.

| Option | Default | Allowed |
|---|---|---|
| `--concurrency` | 10 | 1 through 50 |
| `--duration` | 10 seconds | 1 through 30 seconds |
| `--warmup` | 2 seconds | 0 through 10 seconds |

Those caps keep a local run from opening a large amount of traffic. They are not a sizing guide for production.

## Output

For each strategy the process prints the request count, success count, failure count, and latency min, mean, p50, p95, and max in milliseconds. Percentiles are computed from the samples collected in that phase. An empty phase or any failure makes the process exit with code 1. Invalid arguments exit with code 2.

The header line includes the framework description, OS description, and process architecture. It does not include a user name.

## What a run does not show

- It does not prove that the per-request strategy will exhaust ports. Port exhaustion depends on duration, connection rate, and the operating system's `TIME_WAIT` behavior.
- It does not rank the factory against the long-lived client in general. On a loopback HTTP/1.1 path the dominant cost may be local scheduling rather than pool policy.
- It does not measure cross-network RTT, TLS, HTTP/2, or a shared remote dependency.
- A fake `HttpMessageHandler` cannot be used to claim TCP pooling. The generator and the loopback tests in this repo use real sockets for that claim. The generator's report is still an application-level latency report, not a socket dump.

Record the command, the printed environment line, and the printed table together when you keep a result. Do not copy those figures back into this repository as a standing benchmark.
