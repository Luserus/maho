using System.Collections.Generic;
using Maho.Symbols;

namespace Maho.Resolution;

/// <summary> Stable project-wide semantic output produced by the resolution coordinator. </summary>
internal sealed class ResolutionProjectResult
{
    public string ProjectName { get; }
    public NamespaceSymbol GlobalNamespace { get; }
    public Scope GlobalScope { get; }
    public IReadOnlyList<ResolutionResult> Units { get; }
    public IReadOnlyList<ResolutionProjectReference> References { get; }
    public IReadOnlyDictionary<Symbol, Scope> SymbolScopes { get; }

    public ResolutionProjectResult(
        string projectName,
        NamespaceSymbol globalNamespace,
        Scope globalScope,
        IReadOnlyList<ResolutionResult> units,
        IReadOnlyList<ResolutionProjectReference> references,
        IReadOnlyDictionary<Symbol, Scope> symbolScopes)
    {
        ProjectName = projectName;
        GlobalNamespace = globalNamespace;
        GlobalScope = globalScope;
        Units = units;
        References = references;
        SymbolScopes = symbolScopes;
    }
}
