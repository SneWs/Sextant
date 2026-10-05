using Sextant;

namespace Sextant.Git.Tests;

public class MacTabShortcutTests
{
    [Fact]
    public void Digit_zero_selects_the_tenth_tab_and_nine_selects_the_ninth()
    {
        Assert.Equal(0, TabShortcut.IndexFromDigit(1));
        Assert.Equal(8, TabShortcut.IndexFromDigit(9));
        Assert.Equal(9, TabShortcut.IndexFromDigit(0));
        Assert.Null(TabShortcut.IndexFromDigit(11));
        Assert.Null(TabShortcut.IndexFromDigit(-1));
        Assert.Equal("⌘1", TabShortcut.Hint(0));
        Assert.Equal("⌘0", TabShortcut.Hint(9));
        Assert.Null(TabShortcut.Hint(10));
    }
}
