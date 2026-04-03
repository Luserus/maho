using Maho.Symbols;

namespace Maho.Resolution;

/// <summary> Describes one externally resolved project that later passes can consult. </summary>
internal sealed class ResolutionProjectReference
{
    public string Name { get; }
    public NamespaceSymbol GlobalNamespace { get; }
    public Scope GlobalScope { get; }

    public ResolutionProjectReference(string name, NamespaceSymbol globalNamespace, Scope globalScope)
    {
        Name = name;
        GlobalNamespace = globalNamespace;
        GlobalScope = globalScope;
    }

    public static ResolutionProjectReference FromResult(string name, ResolutionProjectResult result) =>
        new(name, result.GlobalNamespace, result.GlobalScope);
}
