namespace TaskBarHook.Media;

public sealed class GenerationGate
{
    private int _generation;

    public int Begin() => Interlocked.Increment(ref _generation);

    public bool IsCurrent(int token) => Volatile.Read(ref _generation) == token;
}
