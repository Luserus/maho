namespace Maho.Resolution;

/// <summary> Base contract for one semantic resolution stage. </summary>
internal abstract class ResolutionPass
{
    public virtual string Name => GetType().Name;

    public abstract void Execute(ResolutionContext context);
}
