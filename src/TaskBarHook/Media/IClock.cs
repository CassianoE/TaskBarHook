namespace TaskBarHook.Media;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
