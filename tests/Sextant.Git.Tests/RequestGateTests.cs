namespace Sextant.Git.Tests;

public class RequestGateTests
{
    [Fact]
    public void Stale_generation_is_dropped()
    {
        var gate = new RequestGate();
        var first = gate.Next();
        var second = gate.Next();
        Assert.False(gate.IsCurrent(first));
        Assert.True(gate.IsCurrent(second));
    }
}
