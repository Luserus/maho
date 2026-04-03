namespace Maho.Resolution;

/// <summary>
/// Base contract for one semantic resolution stage. A pass can choose to do project-wide setup,
/// per-unit work, project-wide finalization, or any combination of the three.
/// </summary>
internal abstract class ResolutionPass
{
    public virtual string Name => GetType().Name;

    public virtual void BeforeProject(ResolutionCoordinatorContext context) { }

    public virtual void ExecuteUnit(ResolutionContext context) { }

    public virtual void AfterProject(ResolutionCoordinatorContext context) { }
}
