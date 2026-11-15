using System.Globalization;

namespace LoadGenerator;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = LoadOptions.Parse(args);
            Console.WriteLine("HttpClient lifetime load generator");
            Console.WriteLine(LoadRunner.DescribeEnvironment());
            Console.WriteLine($"Started (UTC): {DateTimeOffset.UtcNow:O}");
            Console.WriteLine($"Target: {options.BaseUrl}");
            Console.WriteLine($"SKU: {options.Sku}");
            Console.WriteLine($"Concurrency: {options.Concurrency}; warmup: {options.Warmup.TotalSeconds.ToString(CultureInfo.InvariantCulture)}s; duration: {options.Duration.TotalSeconds.ToString(CultureInfo.InvariantCulture)}s");
            Console.WriteLine("Warmup samples are discarded. Figures below are from this run only.");
            Console.WriteLine();

            var reports = await LoadRunner.RunAsync(options, CancellationToken.None).ConfigureAwait(false);
            var failed = false;
            foreach (var report in reports)
            {
                Console.WriteLine($"strategy: {report.Strategy}");
                Console.WriteLine($"requests: {report.Requests}");
                Console.WriteLine($"successes: {report.Successes}");
                Console.WriteLine($"failures: {report.Failures}");
                Console.WriteLine(
                    "latency_ms min/mean/p50/p95/max: " +
                    $"{Format(report.MinMilliseconds)}/{Format(report.MeanMilliseconds)}/{Format(report.P50Milliseconds)}/{Format(report.P95Milliseconds)}/{Format(report.MaxMilliseconds)}");
                Console.WriteLine();
                failed |= report.Failures > 0 || report.Requests == 0;
            }

            return failed ? 1 : 0;
        }
        catch (LoadOptionsException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static string Format(double? value) =>
        value is null ? "n/a" : value.Value.ToString("0.###", CultureInfo.InvariantCulture);
}
