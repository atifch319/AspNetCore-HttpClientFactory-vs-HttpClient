using System.Net;
using System.Net.Sockets;

namespace OrderApi.Clients;

public static class DirectoryConnector
{
    public static async ValueTask<Stream> ConnectAsync(
        EndpointDirectory directory,
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(context);

        var target = directory.Current;
        if (!IPAddress.TryParse(target.Host, out var address))
        {
            throw new InvalidOperationException(
                "The endpoint directory contains a hostname. The simulation dials an IP and does not call operating-system DNS.");
        }

        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, target.Port), cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            socket.Dispose();
            throw new HttpRequestException(
                $"Simulated connect to {target.Authority} for logical endpoint {context.DnsEndPoint} failed.",
                ex);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
