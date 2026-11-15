#!/usr/bin/env bash
set -euo pipefail

base_url="http://127.0.0.1:5070"
concurrency=10
duration=10
warmup=2
sku="WIDGET-1"
strategies="per-request,factory,long-lived"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --base-url) base_url="$2"; shift 2 ;;
    --concurrency) concurrency="$2"; shift 2 ;;
    --duration) duration="$2"; shift 2 ;;
    --warmup) warmup="$2"; shift 2 ;;
    --sku) sku="$2"; shift 2 ;;
    --strategies) strategies="$2"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

if [[ "$concurrency" -lt 1 || "$concurrency" -gt 50 ]]; then
  echo "Concurrency must be from 1 through 50." >&2
  exit 2
fi

if [[ "$duration" -lt 1 || "$duration" -gt 30 ]]; then
  echo "Duration must be from 1 through 30 seconds." >&2
  exit 2
fi

if [[ "$warmup" -lt 0 || "$warmup" -gt 10 ]]; then
  echo "Warmup must be from 0 through 10 seconds." >&2
  exit 2
fi

case "$base_url" in
  http://127.0.0.1:*|http://localhost:*|http://[::1]:*|https://127.0.0.1:*|https://localhost:*|https://[::1]:*) ;;
  *) echo "Load tests are limited to loopback http or https URLs." >&2; exit 2 ;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dotnet run --project "$script_dir/../tools/LoadGenerator/LoadGenerator.csproj" -c Release -- \
  --base-url "$base_url" \
  --concurrency "$concurrency" \
  --duration "$duration" \
  --warmup "$warmup" \
  --sku "$sku" \
  --strategies "$strategies"
