using Maho.Symbols;

namespace Maho.Resolution;

/// <summary> Describes one externally resolved project that later passes can consult. </summary>
internal sealed class ResolutionProjectReference
{
    /// <summary> Friendly identity for the referenced project. </summary>
    public string Name { get; }
    /// <summary> Root namespace symbol exposed by the referenced project. </summary>
    public NamespaceSymbol GlobalNamespace { get; }
    /// <summary> Root lexical scope exposed by the referenced project. </summary>
    public Scope GlobalScope { get; }

    /// <summary> Creates one cross-project semantic reference surface. </summary>
    public ResolutionProjectReference(string name, NamespaceSymbol globalNamespace, Scope globalScope)
    {
        Name = name;
        GlobalNamespace = globalNamespace;
        GlobalScope = globalScope;
    }

    /// <summary> Convenience helper that exposes a completed project result as a reference. </summary>
    public static ResolutionProjectReference FromResult(string name, ResolutionProjectResult result) =>
        new(name, result.GlobalNamespace, result.GlobalScope);
}
