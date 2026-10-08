using Avalonia.Controls;
using Avalonia.Threading;
using Sextant.Git;
using Sextant.ViewModels;

namespace Sextant.Services;

/// <summary>
/// Shows the SSH passphrase dialog for Windows git and for a WSL distribution's git.
/// A checked box keeps that passphrase in memory until the process exits. It is not written to disk.
/// macOS and Linux are left to their own agents.
/// </summary>
public static class WindowsAskPass
{
    private static readonly AskPassSession Session = new();
    public static AskPassServer? Attach(GitProcessRunner runner, Window window)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var executable = Path.Combine(AppContext.BaseDirectory, "Sextant.AskPass.exe");
        if (!File.Exists(executable))
            return null;

        var server = new AskPassServer();
        server.Prompt = (request, cancellationToken) => Prompt(window, request, cancellationToken);
        server.Start();
        runner.AskPass = new AskPassLaunch(executable, server.PipeName);
        return server;
    }

    private static Task<string?> Prompt(Window window, AskPassRequest request, CancellationToken cancellationToken)
    {
        var source = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    source.TrySetResult(await Show(window, request, cancellationToken).ConfigureAwait(true));
                }
                catch (Exception exception)
                {
                    source.TrySetException(exception);
                }
            });
        }
        catch (Exception exception)
        {
            source.TrySetException(exception);
        }

        return source.Task;
    }

    private static async Task<string?> Show(Window window, AskPassRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return null;
        if (window.DataContext is not MainViewModel viewModel || viewModel.Dialogs is null)
            return null;

        var dialogs = viewModel.Dialogs;
        if (request.Kind == AskPassKind.Confirm)
        {
            var yes = await dialogs.ConfirmAsync("Confirm", request.Prompt, "Yes").ConfigureAwait(true);
            return yes ? "" : null;
        }

        if (request.Kind == AskPassKind.Message)
        {
            var ok = await dialogs.ConfirmAsync("SSH", request.Prompt, "OK").ConfigureAwait(true);
            return ok ? "" : null;
        }

        if (request.Prompt.Contains("yes/no", StringComparison.OrdinalIgnoreCase))
        {
            var yes = await dialogs.ConfirmAsync("Confirm", request.Prompt, "Yes").ConfigureAwait(true);
            return yes ? "yes" : "no";
        }

        if (Session.TryReuse(request.CommandId, request.Prompt, out var cached, out var rejectedRemembered))
            return cached;

        var entered = await dialogs.PromptSessionSecretAsync("SSH passphrase", request.Prompt, rejectedRemembered).ConfigureAwait(true);
        if (entered.Value is null)
            return null;
        Session.Store(request.CommandId, request.Prompt, entered.Value, entered.RememberForSession);
        return entered.Value;
    }
}
