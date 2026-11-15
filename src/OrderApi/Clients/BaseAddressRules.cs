namespace OrderApi.Clients;

public static class BaseAddressRules
{
    public static Uri Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"Base address '{value}' must be an absolute URI.", nameof(value));
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException($"Base address '{value}' must use http or https.", nameof(value));
        }

        var absolute = uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/";
        return new Uri(absolute, UriKind.Absolute);
    }
}
