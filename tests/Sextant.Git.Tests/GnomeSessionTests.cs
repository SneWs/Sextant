using Sextant;

namespace Sextant.Git.Tests;

public class GnomeSessionTests
{
    [Fact]
    public void Gnome_keeps_the_desktop_frame_and_other_desktops_use_wayland()
    {
        Assert.False(GnomeSession.PrefersWayland("ubuntu:GNOME", "ubuntu"));
        Assert.False(GnomeSession.PrefersWayland("GNOME", "gnome"));
        Assert.False(GnomeSession.PrefersWayland("GNOME-Classic:GNOME", null));
        Assert.False(GnomeSession.PrefersWayland(null, "gnome"));
        Assert.False(GnomeSession.PrefersWayland("pop:GNOME", "pop"));
        Assert.True(GnomeSession.PrefersWayland("KDE", "plasma"));
        Assert.True(GnomeSession.PrefersWayland("X-Cinnamon", "cinnamon"));
        Assert.True(GnomeSession.PrefersWayland("ubuntu", "ubuntu"));
        Assert.True(GnomeSession.PrefersWayland("GNOMEISH", null));
        Assert.True(GnomeSession.PrefersWayland(null, null));
        Assert.True(GnomeSession.PrefersWayland("  ", " "));
    }
}
