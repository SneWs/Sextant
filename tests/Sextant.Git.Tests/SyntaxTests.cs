using Sextant;

namespace Sextant.Git.Tests;

public class SyntaxTests
{
    [Fact]
    public void Syntax_covers_the_visible_line_only()
    {
        const string line = "  public class Foo";
        var spans = DiffSyntax.Tokenize(line, "c");
        Assert.Contains(spans, span => span.Kind == SyntaxKind.Keyword && line.Substring(span.Start, span.Length) == "public");
        Assert.Contains(spans, span => span.Kind == SyntaxKind.Keyword && line.Substring(span.Start, span.Length) == "class");
        const string comment = "+ // note";
        Assert.Contains(DiffSyntax.Tokenize(comment, "c"), span => span.Kind == SyntaxKind.Comment && comment.Substring(span.Start, span.Length).Contains("//", StringComparison.Ordinal));
        Assert.Empty(DiffSyntax.Tokenize("public", null));
        Assert.Equal("c", DiffSyntax.Language("a.cs"));
        Assert.Null(DiffSyntax.Language("a.png"));
        Assert.Equal("hash", DiffSyntax.Language("Assets/Wood.mat"));
        Assert.Equal("hash", DiffSyntax.Language("Assets/Hero.prefab"));
        Assert.Equal("hash", DiffSyntax.Language("Assets/Hero.prefab.meta"));
        Assert.Equal("hash", DiffSyntax.Language("Scenes/Main.unity"));
        Assert.Equal("hash", DiffSyntax.Language("Anim/Run.overrideController"));
        Assert.Equal(".yaml", DiffSyntax.GrammarExtension("Assets/Wood.MAT"));
        Assert.Equal(".json", DiffSyntax.GrammarExtension("Asm/Game.asmdef"));
        Assert.Equal(".json", DiffSyntax.GrammarExtension("Graphs/Lit.shadergraph"));
        Assert.Equal(".xml", DiffSyntax.GrammarExtension("UI/Menu.uxml"));
        Assert.Equal(".css", DiffSyntax.GrammarExtension("UI/Menu.uss"));
        Assert.Equal(".hlsl", DiffSyntax.GrammarExtension("Shaders/Trace.raytrace"));
        Assert.Equal(".cs", DiffSyntax.GrammarExtension("Scripts/Hero.cs"));
        Assert.Equal(".shader", DiffSyntax.GrammarExtension("Shaders/Lit.shader"));
        const string yaml = "  # a Unity material";
        Assert.Contains(DiffSyntax.Tokenize(yaml, DiffSyntax.Language("Wood.mat")), span => span.Kind == SyntaxKind.Comment);
    }
}
