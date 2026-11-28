namespace Maho.Resolution;

/// <summary> Represents a compiled module imported automatically or bound via <c>extern using</c>. </summary>
internal sealed class ModuleSymbol : Symbol
{
    /// <summary> Canonical PascalCase module name of the referenced compilation. </summary>
    public string CanonicalModuleName { get; }

    /// <summary> Optional compiler-assigned alias for disambiguating duplicate module names. </summary>
    public string? CompilerAlias { get; }

    /// <summary> The semantic resolution context owned by the referenced module. </summary>
    public ResolutionContext SourceContext { get; }

    /// <summary> Project identifier of the referenced module. </summary>
    public int TargetProjectId => SourceContext.ProjectId;

    public ModuleSymbol(
        SymbolID id,
        Scope enclosingScope,
        string canonicalModuleName,
        string? compilerAlias,
        ResolutionContext sourceContext)
        : base(id, new SymbolPart(canonicalModuleName), enclosingScope)
    {
        Kind = SymbolKind.Module;
        CanonicalModuleName = canonicalModuleName;
        CompilerAlias = compilerAlias;
        SourceContext = sourceContext;
        ProjectId = sourceContext.ProjectId;
    }
}

