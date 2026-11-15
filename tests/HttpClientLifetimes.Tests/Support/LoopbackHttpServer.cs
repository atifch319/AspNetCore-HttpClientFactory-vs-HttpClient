using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HttpClientLifetimes.Tests.Support;

internal sealed record LoopbackRequest(
    string Method,
    string Path,
    string Query,
    IReadOnlyDictionary<string, string> Headers,
    string Body,
    int ConnectionId);

internal sealed record LoopbackPlan(
    int StatusCode = 200,
    string Body = "{}",
    string ContentType = "application/json",
    IReadOnlyList<(string Name, string Value)>? Headers = null,
    bool Hold = false);

internal sealed class LoopbackHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<LoopbackRequest, CancellationToken, Task<LoopbackPlan>> _handler;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _connections = [];
    private readonly object _gate = new();
    private readonly ConcurrentQueue<TaskCompletionSource<bool>> _holds = new();
    private readonly TaskCompletionSource _firstHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _accepted;
    private int _requests;

    private LoopbackHttpServer(TcpListener listener, Func<LoopbackRequest, CancellationToken, Task<LoopbackPlan>> handler)
    {
        _listener = listener;
        _handler = handler;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public int Port { get; }

    public ConcurrentQueue<Exception> Errors { get; } = new();

    public ConcurrentDictionary<int, int> RequestsPerConnection { get; } = new();

    public int AcceptedConnections => Volatile.Read(ref _accepted);

    public int RequestCount => Volatile.Read(ref _requests);

    public static Task<LoopbackHttpServer> StartAsync(string instanceId = "loopback") =>
        StartAsync((request, _) => Task.FromResult(Json(instanceId, request.ConnectionId)));

    public static Task<LoopbackHttpServer> StartAsync(Func<LoopbackRequest, CancellationToken, Task<LoopbackPlan>> handler)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = new LoopbackHttpServer(listener, handler);
        _ = server.AcceptLoopAsync();
        return Task.FromResult(server);
    }

    public static LoopbackPlan Json(string instanceId, int connectionId, int statusCode = 200, bool hold = false) =>
        new(statusCode, $$"""{"instanceId":"{{instanceId}}","connectionId":{{connectionId}}}""", Hold: hold);

    public Task WaitForHoldAsync(CancellationToken cancellationToken) => _firstHold.Task.WaitAsync(cancellationToken);

    public int ReleaseHolds()
    {
        var released = 0;
        while (_holds.TryDequeue(out var hold))
        {
            hold.TrySetResult(true);
            released++;
        }

        return released;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        ReleaseHolds();
        _listener.Stop();

        Task[] snapshot;
        lock (_gate)
        {
            snapshot = _connections.ToArray();
        }

        try
        {
            await Task.WhenAll(snapshot).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Connection teardown races with client disposal.
        }

        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Errors.Enqueue(ex);
                break;
            }

            var connectionId = Interlocked.Increment(ref _accepted);
            var task = ServeAsync(client, connectionId, _stop.Token);
            lock (_gate)
            {
                _connections.Add(task);
            }
        }
    }

    private async Task ServeAsync(TcpClient client, int connectionId, CancellationToken cancellationToken)
    {
        using (client)
        {
            client.NoDelay = true;
            await using var stream = client.GetStream();
            var pending = new List<byte>(1024);
            var buffer = new byte[8192];
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var request = await ReadRequestAsync(stream, pending, buffer, connectionId, cancellationToken).ConfigureAwait(false);
                    if (request is null)
                    {
                        break;
                    }

                    Interlocked.Increment(ref _requests);
                    RequestsPerConnection.AddOrUpdate(connectionId, 1, static (_, count) => count + 1);

                    var plan = await _handler(request, cancellationToken).ConfigureAwait(false);
                    if (plan.Hold)
                    {
                        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        _holds.Enqueue(release);
                        _firstHold.TrySetResult();
                        await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }

                    await WriteResponseAsync(stream, plan, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or EndOfStreamException)
            {
                // The client closed the connection or the server is stopping.
            }
            catch (Exception ex)
            {
                Errors.Enqueue(ex);
            }
        }
    }

    private static async Task<LoopbackRequest?> ReadRequestAsync(
        NetworkStream stream,
        List<byte> pending,
        byte[] buffer,
        int connectionId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var headerIndex = IndexOfHeaderTerminator(pending);
            if (headerIndex >= 0)
            {
                var headerText = Encoding.ASCII.GetString(pending.GetRange(0, headerIndex).ToArray());
                var contentLength = ParseContentLength(headerText);
                var bodyEnd = headerIndex + 4 + contentLength;
                while (pending.Count < bodyEnd)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        throw new EndOfStreamException("The connection closed before the request body finished.");
                    }

                    AddBytes(pending, buffer, read);
                }

                var body = contentLength == 0
                    ? string.Empty
                    : Encoding.UTF8.GetString(pending.GetRange(headerIndex + 4, contentLength).ToArray());
                pending.RemoveRange(0, bodyEnd);
                return Parse(headerText, body, connectionId);
            }

            var count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                return pending.Count == 0 ? null : throw new EndOfStreamException("The connection closed in the middle of a request.");
            }

            AddBytes(pending, buffer, count);
            if (pending.Count > 1024 * 1024)
            {
                throw new InvalidOperationException("The request exceeded 1 MB.");
            }
        }
    }

    private static async Task WriteResponseAsync(NetworkStream stream, LoopbackPlan plan, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(plan.Body);
        var builder = new StringBuilder();
        builder.Append("HTTP/1.1 ").Append(plan.StatusCode).Append(' ').Append(Reason(plan.StatusCode)).Append("\r\n");
        builder.Append("Content-Type: ").Append(plan.ContentType).Append("\r\n");
        builder.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        builder.Append("Connection: keep-alive\r\n");
        if (plan.Headers is not null)
        {
            foreach (var (name, value) in plan.Headers)
            {
                builder.Append(name).Append(": ").Append(value).Append("\r\n");
            }
        }

        builder.Append("\r\n");
        var header = Encoding.ASCII.GetBytes(builder.ToString());
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static LoopbackRequest Parse(string headerText, string body, int connectionId)
    {
        var lines = headerText.Split("\r\n");
        var parts = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        var target = parts.Length > 1 ? parts[1] : "/";
        var path = target;
        var query = string.Empty;
        var queryIndex = target.IndexOf('?');
        if (queryIndex >= 0)
        {
            path = target[..queryIndex];
            query = target[(queryIndex + 1)..];
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var colon = lines[i].IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var name = lines[i][..colon].Trim();
            var value = lines[i][(colon + 1)..].Trim();
            headers[name] = headers.TryGetValue(name, out var existing) ? existing + ", " + value : value;
        }

        return new LoopbackRequest(parts[0], path, query, headers, body, connectionId);
    }

    private static int ParseContentLength(string headerText)
    {
        foreach (var line in headerText.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line.AsSpan("Content-Length:".Length).Trim(), out var length))
            {
                return length;
            }
        }

        return 0;
    }

    private static int IndexOfHeaderTerminator(List<byte> data)
    {
        for (var i = 0; i <= data.Count - 4; i++)
        {
            if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n')
            {
                return i;
            }
        }

        return -1;
    }

    private static void AddBytes(List<byte> pending, byte[] buffer, int count)
    {
        for (var i = 0; i < count; i++)
        {
            pending.Add(buffer[i]);
        }
    }

    private static string Reason(int statusCode) => statusCode switch
    {
        200 => "OK",
        400 => "Bad Request",
        404 => "Not Found",
        409 => "Conflict",
        500 => "Internal Server Error",
        _ => "OK"
    };
}
