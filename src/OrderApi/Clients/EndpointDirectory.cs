using System.Net;

namespace OrderApi.Clients;

/// <summary>
/// In-process stand-in for a DNS answer. It stores an IP endpoint and nothing else.
/// Operating-system resolvers, public DNS, and DNS TTL are not involved.
/// </summary>
public sealed class EndpointDirectory
{
    private Uri _current;

    public EndpointDirectory(Uri initial)
    {
        _current = RequireIpEndpoint(initial);
    }

    public Uri Current => Volatile.Read(ref _current);

    public void Set(Uri endpoint) => Volatile.Write(ref _current, RequireIpEndpoint(endpoint));

    public static Uri RequireIpEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException("The simulated endpoint must be an absolute http URI.", nameof(endpoint));
        }

        if (!IPAddress.TryParse(endpoint.Host, out _))
        {
            throw new ArgumentException(
                "The simulated endpoint host must be an IP address. This directory is not operating-system DNS.",
                nameof(endpoint));
        }

        if (!endpoint.Authority.Contains(':', StringComparison.Ordinal) || endpoint.Port is < 1 or > 65535)
        {
            throw new ArgumentException("The simulated endpoint must include an explicit port.", nameof(endpoint));
        }

        return endpoint;
    }
}
