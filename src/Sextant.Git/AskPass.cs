using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;

namespace Sextant.Git;

public enum AskPassKind : byte
{
    Password = 1,
    Confirm = 2,
    Message = 3,
}

public sealed record AskPassRequest(AskPassKind Kind, string Prompt, string? CommandId = null);

/// <summary>
/// Answers <c>SSH_ASKPASS</c> for a git process that has no terminal.
/// The helper process talks to this pipe; the app shows the dialog.
/// </summary>
public sealed record AskPassLaunch(string Executable, string PipeName);

public sealed class AskPassServer : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _dialog = new(1, 1);
    private int _started;

    public AskPassServer()
    {
        PipeName = "sextant-askpass-" + Guid.NewGuid().ToString("N");
    }

    public string PipeName { get; }

    public Func<AskPassRequest, CancellationToken, Task<string?>>? Prompt { get; set; }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;
        _ = AcceptLoop(_lifetime.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _lifetime.Dispose();
        _dialog.Dispose();
    }

    private async Task AcceptLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var pipe = CreatePipe();
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                return;
            }
            catch (IOException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                return;
            }

            _ = AnswerAsync(pipe);
        }
    }

    private async Task AnswerAsync(NamedPipeServerStream pipe)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        try
        {
            var request = await AskPassProtocol.ReadRequestAsync(pipe, linked.Token).ConfigureAwait(false);
            if (request is null)
            {
                await AskPassProtocol.WriteResponseAsync(pipe, null, linked.Token).ConfigureAwait(false);
                return;
            }

            string? secret = null;
            await _dialog.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (Prompt is not null)
                    secret = await Prompt(request, linked.Token).ConfigureAwait(false);
            }
            finally
            {
                _dialog.Release();
            }

            await AskPassProtocol.WriteResponseAsync(pipe, secret, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    private NamedPipeServerStream CreatePipe() =>
        new(PipeName, PipeDirection.InOut, 8, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
}

public static class AskPassProtocol
{
    public static async Task WriteRequestAsync(Stream stream, AskPassKind kind, string prompt, CancellationToken cancellationToken, string? commandId = null)
    {
        var text = prompt.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetBytes(text);
        var command = Encoding.UTF8.GetBytes(commandId ?? "");
        var header = new byte[5];
        header[0] = (byte)kind;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), bytes.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > 0)
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        var commandHeader = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(commandHeader, command.Length);
        await stream.WriteAsync(commandHeader, cancellationToken).ConfigureAwait(false);
        if (command.Length > 0)
            await stream.WriteAsync(command, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<AskPassRequest?> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[5];
        if (!await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false))
            return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1));
        if (length < 0 || length > 16 * 1024)
            return null;
        var body = new byte[length];
        if (length > 0 && !await ReadExactAsync(stream, body, cancellationToken).ConfigureAwait(false))
            return null;
        var kind = header[0] switch
        {
            (byte)AskPassKind.Confirm => AskPassKind.Confirm,
            (byte)AskPassKind.Message => AskPassKind.Message,
            _ => AskPassKind.Password,
        };
        var commandHeader = new byte[4];
        if (!await ReadExactAsync(stream, commandHeader, cancellationToken).ConfigureAwait(false))
            return null;
        var commandLength = BinaryPrimitives.ReadInt32LittleEndian(commandHeader);
        if (commandLength < 0 || commandLength > 128)
            return null;
        var commandBytes = new byte[commandLength];
        if (commandLength > 0 && !await ReadExactAsync(stream, commandBytes, cancellationToken).ConfigureAwait(false))
            return null;
        var commandId = commandLength == 0 ? null : Encoding.UTF8.GetString(commandBytes);
        return new AskPassRequest(kind, Encoding.UTF8.GetString(body), commandId);
    }

    public static async Task WriteResponseAsync(Stream stream, string? secret, CancellationToken cancellationToken)
    {
        var bytes = secret is null ? [] : Encoding.UTF8.GetBytes(secret);
        var header = new byte[5];
        header[0] = secret is null ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), bytes.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > 0)
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string?> ReadResponseAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[5];
        if (!await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false))
            return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1));
        if (header[0] != 0 || length < 0 || length > 16 * 1024)
            return null;
        var body = new byte[length];
        if (length > 0 && !await ReadExactAsync(stream, body, cancellationToken).ConfigureAwait(false))
            return null;
        return Encoding.UTF8.GetString(body);
    }

    public static async Task<string?> ExchangeAsync(string pipeName, AskPassKind kind, string prompt, CancellationToken cancellationToken, string? commandId = null)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5_000, cancellationToken).ConfigureAwait(false);
        await WriteRequestAsync(pipe, kind, prompt, cancellationToken, commandId).ConfigureAwait(false);
        return await ReadResponseAsync(pipe, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (count == 0)
                return false;
            read += count;
        }

        return true;
    }
}

public static class AskPassEnvironment
{
    public const string Require = "force";

    public const string CommandVariable = "SEXTANT_ASKPASS_COMMAND";

    public static Dictionary<string, string> ForWindowsGit(string executable, string pipeName) => new(StringComparer.Ordinal)
    {
        ["SSH_ASKPASS"] = executable,
        ["SSH_ASKPASS_REQUIRE"] = Require,
        ["SEXTANT_ASKPASS"] = pipeName,
    };

    public static Dictionary<string, string> ForWslGit(string scriptPath) => new(StringComparer.Ordinal)
    {
        ["SSH_ASKPASS"] = scriptPath,
        ["SSH_ASKPASS_REQUIRE"] = Require,
    };

    public static AskPassKind KindOf(string? promptKind) => promptKind switch
    {
        "confirm" => AskPassKind.Confirm,
        "none" => AskPassKind.Message,
        _ => AskPassKind.Password,
    };
}
