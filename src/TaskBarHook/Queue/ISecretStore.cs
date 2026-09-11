namespace TaskBarHook.Queue;

public interface ISecretStore
{
    string? Get(string key);

    void Set(string key, string value);

    void Delete(string key);
}

public sealed class MemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, string value) => _values[key] = value;

    public void Delete(string key) => _values.Remove(key);
}
