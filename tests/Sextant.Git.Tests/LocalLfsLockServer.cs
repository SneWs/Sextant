using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Sextant.Git.Tests;

internal sealed class LocalLfsLockServer : IAsyncDisposable
{
    private const string Prefix = "/repo.git/info/lfs/";
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentQueue<Request> _requests = new();
    private readonly List<LfsLock> _locks;
    private readonly int _pageSize;
    private readonly Task _server;
    private int _nextId;

    public LocalLfsLockServer(IEnumerable<LfsLock> locks, int pageSize = 100)
    {
        _locks = locks.ToList();
        _pageSize = pageSize;
        _listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{Prefix.TrimEnd('/')}";
        _server = ServeAsync();
    }

    public string Url { get; }

    public int ListStatus { get; set; } = 200;

    public Func<CancellationToken, Task>? BeforeList { get; set; }

    public IReadOnlyList<Request> Requests => _requests.ToArray();

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _listener.Stop();
        try
        {
            // Unexpected fixture exceptions must fail the test, not disappear in a background task.
            await _server;
        }
        finally
        {
            _lifetime.Dispose();
        }
    }

    private async Task ServeAsync()
    {
        try
        {
            while (true)
            {
                using var client = await _listener.AcceptTcpClientAsync(_lifetime.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await HandleAsync(client.GetStream(), timeout.Token);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (SocketException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task HandleAsync(NetworkStream stream, CancellationToken token)
    {
        var header = new List<byte>();
        var one = new byte[1];
        while (header.Count < 16 * 1024)
        {
            await stream.ReadExactlyAsync(one, token);
            header.Add(one[0]);
            if (header.Count >= 4 && header[^4] == '\r' && header[^3] == '\n' && header[^2] == '\r' && header[^1] == '\n')
                break;
        }
        if (header.Count == 16 * 1024)
            throw new InvalidOperationException("LFS fixture received an oversized HTTP header.");

        var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var first = lines[0].Split(' ');
        var method = first[0];
        var uri = new Uri(new Uri(Url + "/"), first[1]);
        var length = 0;
        foreach (var line in lines.Skip(1))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                length = int.Parse(line["Content-Length:".Length..].Trim(), CultureInfo.InvariantCulture);
            if (line.Equals("Expect: 100-continue", StringComparison.OrdinalIgnoreCase))
                await stream.WriteAsync("HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray(), token);
        }
        if (length < 0 || length > 16 * 1024)
            throw new InvalidOperationException("LFS fixture received an oversized HTTP body.");
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, token);
        var query = Query(uri);
        var route = uri.AbsolutePath.StartsWith(Prefix, StringComparison.Ordinal)
            ? uri.AbsolutePath[Prefix.Length..]
            : uri.AbsolutePath;
        string? path = null;
        var force = false;
        if (body.Length > 0)
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("path", out var pathValue))
                path = pathValue.GetString();
            if (document.RootElement.TryGetProperty("force", out var forceValue))
                force = forceValue.GetBoolean();
        }
        query.TryGetValue("cursor", out var cursor);
        _requests.Enqueue(new Request(method, route, path, cursor, force));

        if (method == "GET" && route == "locks")
        {
            if (BeforeList is { } beforeList)
                await beforeList(token);
            if (ListStatus != 200)
            {
                await RespondAsync(stream, ListStatus, new { message = "Lock server unavailable" }, token);
                return;
            }
            IEnumerable<LfsLock> matching = _locks;
            if (query.TryGetValue("path", out var requestedPath))
                matching = matching.Where(item => item.Path == requestedPath);
            if (query.TryGetValue("id", out var requestedId))
                matching = matching.Where(item => item.Id == requestedId);
            var all = matching.ToArray();
            var offset = string.IsNullOrEmpty(cursor) ? 0 : int.Parse(cursor, CultureInfo.InvariantCulture);
            var page = all.Skip(offset).Take(_pageSize).Select(WireLock).ToArray();
            var next = offset + page.Length < all.Length
                ? (offset + page.Length).ToString(CultureInfo.InvariantCulture)
                : "";
            await RespondAsync(stream, 200, new { locks = page, next_cursor = next }, token);
            return;
        }

        if (method == "POST" && route == "locks")
        {
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("LFS fixture received a lock request without a path.");
            var existing = _locks.FirstOrDefault(item => item.Path == path);
            if (existing is not null)
            {
                await RespondAsync(stream, 409, new { message = "Lock already exists", @lock = WireLock(existing) }, token);
                return;
            }
            var created = new LfsLock("created-" + ++_nextId, path, "Test");
            _locks.Add(created);
            await RespondAsync(stream, 201, new { @lock = WireLock(created) }, token);
            return;
        }

        if (method == "POST" && route.StartsWith("locks/", StringComparison.Ordinal) && route.EndsWith("/unlock", StringComparison.Ordinal))
        {
            var id = route["locks/".Length..^"/unlock".Length];
            var existing = _locks.FirstOrDefault(item => item.Id == id);
            if (existing is null)
            {
                await RespondAsync(stream, 404, new { message = "Lock not found" }, token);
                return;
            }
            if (existing.Owner != "Test" && !force)
            {
                await RespondAsync(stream, 403, new { message = "Lock belongs to another user" }, token);
                return;
            }
            _locks.Remove(existing);
            await RespondAsync(stream, 200, new { @lock = WireLock(existing) }, token);
            return;
        }

        await RespondAsync(stream, 404, new { message = $"Unexpected LFS request: {method} {uri.PathAndQuery}" }, token);
        throw new InvalidOperationException($"Unexpected LFS request: {method} {uri.PathAndQuery}");
    }

    private static object WireLock(LfsLock item) =>
        new { id = item.Id, path = item.Path, owner = new { name = item.Owner }, locked_at = "2026-10-07T12:00:00Z" };

    private static Dictionary<string, string> Query(Uri uri) =>
        uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(parts => Uri.UnescapeDataString(parts[0]), parts =>
                parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace("+", " ", StringComparison.Ordinal)) : "", StringComparer.Ordinal);

    private static async Task RespondAsync(NetworkStream stream, int status, object response, CancellationToken token)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(response);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} Response\r\nContent-Type: application/vnd.git-lfs+json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(body, token);
    }

    internal sealed record Request(string Method, string Route, string? Path, string? Cursor, bool Force);
}
