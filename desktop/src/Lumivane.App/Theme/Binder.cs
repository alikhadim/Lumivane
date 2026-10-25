namespace Lumivane.App;

/// <summary>Collects the update callbacks of one page. The window runs them whenever the model changed.</summary>
public sealed class Binder : IDisposable
{
    [ThreadStatic] static Binder? current;
    public static Binder Current { get => current ?? throw new InvalidOperationException("No active binder"); set => current = value; }
    public static Binder? Peek => current;

    readonly List<Action> updaters = new();
    readonly List<Action> cleanups = new();

    public void Add(Action update) { updaters.Add(update); update(); }
    public void OnDispose(Action cleanup) => cleanups.Add(cleanup);
    public void Run() { foreach (var u in updaters.ToArray()) u(); }

    public void Dispose()
    {
        foreach (var c in cleanups) { try { c(); } catch { } }
        cleanups.Clear(); updaters.Clear();
    }

    /// <summary>Runs <paramref name="build"/> with this binder as the active one.</summary>
    public T Build<T>(Func<T> build)
    {
        var prev = current; current = this;
        try { return build(); } finally { current = prev; }
    }
}
