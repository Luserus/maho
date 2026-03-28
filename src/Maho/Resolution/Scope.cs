namespace Maho.Resolution;

internal sealed class Scope
{
    private readonly Scope? parent;

    public Scope(Scope? parent = null)
    {
        this.parent = parent;
    }
}
