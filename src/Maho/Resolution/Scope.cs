namespace Maho.Resolution;

internal sealed class Scope
{
    public ScopeID ID { get; }
    public Scope? Parent { get; }

    public static Scope GlobalScope { get; } = new Scope(0, null);

    public Scope(ScopeID id, Scope? parent)
    {
        ID = id;
        Parent = parent;
    }
}

