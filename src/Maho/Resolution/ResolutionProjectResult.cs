using System.Collections.Generic;
using Maho.Symbols;

namespace Maho.Resolution;

/// <summary> Stable project-wide semantic output produced by the resolution coordinator. </summary>
internal sealed class ResolutionProjectResult
{
    /// <summary> Friendly identity for the resolved project. </summary>
    public string ProjectName { get; }
    /// <summary> Root namespace symbol containing all top-level declarations. </summary>
    public NamespaceSymbol GlobalNamespace { get; }
    /// <summary> Global lexical scope at the project root. </summary>
    public Scope GlobalScope { get; }
    /// <summary> Frozen per-unit semantic results in syntax-tree order. </summary>
    public ResolutionResult[] Units { get; }
    /// <summary> Referenced projects that were visible during this resolution run. </summary>
    public ResolutionProjectReference[] References { get; }
    /// <summary> Frozen project-wide symbol -> owned scope map. </summary>
    public IReadOnlyDictionary<Symbol, Scope> SymbolScopes { get; }
    /// <summary> Frozen intrinsic attribute declarations collected across the project by simple compiler-known name. </summary>
    public IReadOnlyDictionary<string, TypeSymbol[]> IntrinsicAttributeDefinitions { get; }

    /// <summary> Creates the final project-wide semantic result object. </summary>
    public ResolutionProjectResult(
        string projectName,
        NamespaceSymbol globalNamespace,
        Scope globalScope,
        ResolutionResult[] units,
        ResolutionProjectReference[] references,
        IReadOnlyDictionary<Symbol, Scope> symbolScopes,
        IReadOnlyDictionary<string, TypeSymbol[]> intrinsicAttributeDefinitions)
    {
        ProjectName = projectName;
        GlobalNamespace = globalNamespace;
        GlobalScope = globalScope;
        Units = units;
        References = references;
        SymbolScopes = symbolScopes;
        IntrinsicAttributeDefinitions = intrinsicAttributeDefinitions;
    }
}
