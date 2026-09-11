namespace TaskBarHook.Models;

public sealed record CommandAvailability(bool PlayPause, bool Next, bool Previous, bool Seek = false)
{
    public static CommandAvailability None { get; } = new(false, false, false);
}
