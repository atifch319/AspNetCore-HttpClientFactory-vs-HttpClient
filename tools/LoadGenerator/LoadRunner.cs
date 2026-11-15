using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace LoadGenerator;

public sealed record LoadSample(double ElapsedMilliseconds, bool Success, int StatusCode);

public sealed record LoadStrategyReport(
    string Strategy,
    int Requests,
    int Successes,
    int Failures,
    double? MinMilliseconds,
    double? MeanMilliseconds,
    double? P50Milliseconds,
    double? P95Milliseconds,
    double? MaxMilliseconds)
{
    public static LoadStrategyReport FromSamples(string strategy, IReadOnlyCollection<LoadSample> samples)
    {
        var latencies = samples.Select(sample => sample.ElapsedMilliseconds).Order().ToArray();
        var successes = samples.Count(sample => sample.Success);
        return new LoadStrategyReport(
            strategy,
            samples.Count,
            successes,
            samples.Count - successes,
            latencies.Length == 0 ? null : latencies[0],
            latencies.Length == 0 ? null : latencies.Average(),
            Percentile(latencies, 0.50),
            Percentile(latencies, 0.95),
            latencies.Length == 0 ? null : latencies[^1]);
    }

    private static double? Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 0)
        {
            return null;
        }

        var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        index = Math.Clamp(index, 0, sorted.Length - 1);
        return sorted[index];
    }
}

public static class LoadRunner
{
    public static async Task<IReadOnlyList<LoadStrategyReport>> RunAsync(LoadOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            UseCookies = false
        };
        using var client = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = options.BaseUrl,
            Timeout = TimeSpan.FromSeconds(5),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        var reports = new List<LoadStrategyReport>(options.Strategies.Count);
        foreach (var strategy in options.Strategies)
        {
            var path = "/api/demo/" + strategy + "/stock/" + Uri.EscapeDataString(options.Sku);
            if (options.Warmup > TimeSpan.Zero)
            {
                await RunPhaseAsync(client, path, options.Concurrency, options.Warmup, sink: null, cancellationToken).ConfigureAwait(false);
            }

            var samples = new System.Collections.Concurrent.ConcurrentBag<LoadSample>();
            await RunPhaseAsync(client, path, options.Concurrency, options.Duration, samples, cancellationToken).ConfigureAwait(false);
            reports.Add(LoadStrategyReport.FromSamples(strategy, samples.ToArray()));
        }

        return reports;
    }

    public static string DescribeEnvironment() =>
        $"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}";

    private static async Task RunPhaseAsync(
        HttpClient client,
        string path,
        int concurrency,
        TimeSpan phase,
        System.Collections.Concurrent.ConcurrentBag<LoadSample>? sink,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, concurrency).Select(_ => WorkerAsync(client, path, started, phase, sink, cancellationToken));
        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private static async Task WorkerAsync(
        HttpClient client,
        string path,
        Stopwatch started,
        TimeSpan phase,
        System.Collections.Concurrent.ConcurrentBag<LoadSample>? sink,
        CancellationToken cancellationToken)
    {
        while (started.Elapsed < phase && !cancellationToken.IsCancellationRequested)
        {
            var sampleClock = Stopwatch.StartNew();
            try
            {
                using var response = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
                sampleClock.Stop();
                sink?.Add(new LoadSample(sampleClock.Elapsed.TotalMilliseconds, response.IsSuccessStatusCode, (int)response.StatusCode));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                sampleClock.Stop();
                sink?.Add(new LoadSample(sampleClock.Elapsed.TotalMilliseconds, false, 0));
            }
        }
    }
}
