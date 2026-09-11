using TaskBarHook.Media;

namespace TaskBarHook.Tests;

public sealed class GenerationGateTests
{
    [Fact]
    public void Discards_older_asynchronous_tokens()
    {
        var gate = new GenerationGate();
        var first = gate.Begin();
        var second = gate.Begin();

        Assert.False(gate.IsCurrent(first));
        Assert.True(gate.IsCurrent(second));
    }

    [Fact]
    public void New_generation_invalidates_in_flight_artwork_load()
    {
        var gate = new GenerationGate();
        var artworkLoad = gate.Begin();
        var newerTrack = gate.Begin();

        Assert.False(gate.IsCurrent(artworkLoad));
        Assert.True(gate.IsCurrent(newerTrack));
    }
}
