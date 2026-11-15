using LoadGenerator;

namespace HttpClientLifetimes.Tests;

[Trait("Kind", "FakeHandler")]
public class LoadOptionsTests
{
    [Fact]
    public void Defaults_target_the_three_strategies_with_the_same_budget()
    {
        var options = LoadOptions.Parse(["--base-url", "http://127.0.0.1:5070"]);

        Assert.Equal(new[] { "per-request", "factory", "long-lived" }, options.Strategies);
        Assert.Equal(10, options.Concurrency);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Duration);
        Assert.Equal(TimeSpan.FromSeconds(2), options.Warmup);
        Assert.Equal("WIDGET-1", options.Sku);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("http://10.0.0.8:5070")]
    public void Non_loopback_targets_are_rejected(string baseUrl)
    {
        var error = Assert.Throws<LoadOptionsException>(() => LoadOptions.Parse(["--base-url", baseUrl]));
        Assert.Contains("loopback", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unsafe_local_limits_are_rejected()
    {
        Assert.Throws<LoadOptionsException>(() => LoadOptions.Parse(
            ["--base-url", "http://127.0.0.1:5070", "--concurrency", "51"]));
        Assert.Throws<LoadOptionsException>(() => LoadOptions.Parse(
            ["--base-url", "http://127.0.0.1:5070", "--duration", "31"]));
    }
}
