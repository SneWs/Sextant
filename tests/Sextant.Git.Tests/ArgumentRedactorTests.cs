namespace Sextant.Git.Tests;

public class ArgumentRedactorTests
{
    [Fact]
    public void Redacts_url_userinfo()
    {
        var redacted = ArgumentRedactor.Redact("https://user:token@github.com/a/b.git");
        Assert.DoesNotContain("token", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("user", redacted, StringComparison.Ordinal);
        Assert.Contains("github.com", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redacts_userinfo_inside_command_output()
    {
        var redacted = ArgumentRedactor.RedactText("fatal: unable to access 'https://user:token@github.com/a/b.git/': 403");
        Assert.DoesNotContain("token", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("user:", redacted, StringComparison.Ordinal);
        Assert.Contains("github.com/a/b.git", redacted, StringComparison.Ordinal);
        Assert.Contains("403", redacted, StringComparison.Ordinal);
    }
}
