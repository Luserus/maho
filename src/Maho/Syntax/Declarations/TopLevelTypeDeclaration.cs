namespace Maho.Syntax;

/// <summary> Top-level wrapper around a type declaration. </summary>
internal sealed class TopLevelTypeDeclaration : TopLevel
{
    /// <summary> Wrapped type declaration. </summary>
    public TypeDeclaration Type { get; }

    /// <summary> Creates one top-level type declaration wrapper. </summary>
    public TopLevelTypeDeclaration(TypeDeclaration type) => Type = type;
}
