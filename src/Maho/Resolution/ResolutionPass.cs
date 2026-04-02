namespace Maho.Resolution;

internal abstract class ResolutionPass
{
    public virtual string Name => GetType().Name;

    public abstract void Execute(ResolutionContext context);
}
