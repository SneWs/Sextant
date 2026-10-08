using System.Text;
using Sextant.Git;
using Sextant.Git.AskPass;

// Console helper for SSH_ASKPASS. ssh reads the passphrase from this process's stdout.
// A GUI subsystem executable does not give ssh a reliable stdout handle.
var parsed = Parse(args);
if (parsed is null)
    return 1;

string? secret;
try
{
    secret = await AskPassProtocol.ExchangeAsync(parsed.Value.Pipe, parsed.Value.Kind, parsed.Value.Prompt, CancellationToken.None, parsed.Value.Command, parsed.Value.Repository)
        .ConfigureAwait(false);
}
catch (Exception)
{
    return 1;
}

if (secret is null)
    return 1;

using (var stdout = Console.OpenStandardOutput())
{
    var bytes = Encoding.UTF8.GetBytes(secret);
    stdout.Write(bytes);
    stdout.WriteByte((byte)'\n');
    stdout.Flush();
}

return 0;

static (string Pipe, AskPassKind Kind, string Prompt, string? Command, string? Repository)? Parse(string[] args)
{
    string? pipe = null;
    string? kind = null;
    string? command = null;
    string? repository = null;
    var prompt = new List<string>();
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--pipe" && i + 1 < args.Length)
        {
            pipe = args[++i];
            continue;
        }

        if (args[i] == "--kind" && i + 1 < args.Length)
        {
            kind = args[++i];
            continue;
        }

        if (args[i] == "--command" && i + 1 < args.Length)
        {
            command = args[++i];
            continue;
        }

        if (args[i] == "--repo" && i + 1 < args.Length)
        {
            repository = args[++i];
            continue;
        }

        prompt.Add(args[i]);
    }

    pipe ??= Environment.GetEnvironmentVariable("SEXTANT_ASKPASS");
    kind ??= Environment.GetEnvironmentVariable("SSH_ASKPASS_PROMPT");
    command ??= Environment.GetEnvironmentVariable(AskPassEnvironment.CommandVariable);
    repository ??= Environment.GetEnvironmentVariable(AskPassEnvironment.RepositoryVariable);
    if (string.IsNullOrWhiteSpace(pipe))
        return null;
    var text = string.Join(' ', prompt).Trim();
    if (text.Length == 0)
        text = "Enter the passphrase for the SSH key used by this repository.";
    return (
        pipe,
        AskPassEnvironment.KindOf(kind),
        text,
        string.IsNullOrEmpty(command) ? null : command,
        string.IsNullOrEmpty(repository) ? null : repository);
}
