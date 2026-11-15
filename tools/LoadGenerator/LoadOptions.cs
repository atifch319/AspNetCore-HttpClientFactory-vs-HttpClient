namespace LoadGenerator;

public sealed record LoadOptions(
    Uri BaseUrl,
    int Concurrency,
    TimeSpan Duration,
    TimeSpan Warmup,
    string Sku,
    IReadOnlyList<string> Strategies)
{
    public const int MaxConcurrency = 50;
    public const int MaxDurationSeconds = 30;
    public const int MaxWarmupSeconds = 10;

    public static readonly string[] DefaultStrategies = ["per-request", "factory", "long-lived"];

    public static LoadOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? baseUrl = null;
        var concurrency = 10;
        var durationSeconds = 10;
        var warmupSeconds = 2;
        var sku = "WIDGET-1";
        string[]? strategies = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--help" or "-h")
            {
                throw new LoadOptionsException(HelpText);
            }

            string Value()
            {
                if (i + 1 >= args.Length)
                {
                    throw new LoadOptionsException($"Missing value for {arg}.");
                }

                return args[++i];
            }

            switch (arg)
            {
                case "--base-url":
                    baseUrl = Value();
                    break;
                case "--concurrency":
                    concurrency = ParseInt(Value(), arg);
                    break;
                case "--duration":
                    durationSeconds = ParseInt(Value(), arg);
                    break;
                case "--warmup":
                    warmupSeconds = ParseInt(Value(), arg);
                    break;
                case "--sku":
                    sku = Value();
                    break;
                case "--strategies":
                    strategies = Value().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
                default:
                    throw new LoadOptionsException($"Unknown argument '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new LoadOptionsException("Pass --base-url with an absolute loopback http or https URI.");
        }

        if (uri.Scheme is not ("http" or "https") || !uri.IsLoopback)
        {
            throw new LoadOptionsException("The load generator only calls loopback http or https URLs.");
        }

        if (concurrency is < 1 or > MaxConcurrency)
        {
            throw new LoadOptionsException($"--concurrency must be from 1 through {MaxConcurrency}.");
        }

        if (durationSeconds is < 1 or > MaxDurationSeconds)
        {
            throw new LoadOptionsException($"--duration must be from 1 through {MaxDurationSeconds} seconds.");
        }

        if (warmupSeconds is < 0 or > MaxWarmupSeconds)
        {
            throw new LoadOptionsException($"--warmup must be from 0 through {MaxWarmupSeconds} seconds.");
        }

        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new LoadOptionsException("--sku is required.");
        }

        var selected = strategies is null || strategies.Length == 0 ? DefaultStrategies : strategies;
        foreach (var strategy in selected)
        {
            if (!DefaultStrategies.Contains(strategy, StringComparer.Ordinal))
            {
                throw new LoadOptionsException($"Unknown strategy '{strategy}'. Use {string.Join(", ", DefaultStrategies)}.");
            }
        }

        return new LoadOptions(uri, concurrency, TimeSpan.FromSeconds(durationSeconds), TimeSpan.FromSeconds(warmupSeconds), sku.Trim(), selected);
    }

    public const string HelpText =
        """
        Load generator for the three local HttpClient strategies.

        dotnet run --project tools/LoadGenerator/LoadGenerator.csproj -c Release -- --base-url http://127.0.0.1:5070 --concurrency 10 --duration 10 --warmup 2

        Options:
          --base-url     Loopback Order API URL. Required.
          --concurrency  Parallel workers. Default 10. Maximum 50.
          --duration     Measured seconds per strategy. Default 10. Maximum 30.
          --warmup       Unmeasured seconds per strategy. Default 2. Maximum 10.
          --sku          Inventory SKU. Default WIDGET-1.
          --strategies   Comma-separated subset of per-request,factory,long-lived.

        The three strategies use the same path shape, SKU, concurrency, warmup, and duration.
        The tool prints only the samples it collected. It does not publish a ranking.
        """;

    private static int ParseInt(string value, string name)
    {
        if (!int.TryParse(value, out var parsed))
        {
            throw new LoadOptionsException($"{name} must be an integer.");
        }

        return parsed;
    }
}

public sealed class LoadOptionsException : Exception
{
    public LoadOptionsException(string message) : base(message)
    {
    }
}
