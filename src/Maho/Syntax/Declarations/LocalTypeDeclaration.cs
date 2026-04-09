namespace Maho.Syntax;

/// <summary> Local declaration that introduces a nested type. </summary>
internal sealed class LocalTypeDeclaration : Local
{
    /// <summary> Nested type declaration. </summary>
    public TypeDeclaration Type { get; }

    /// <summary> Creates one local type declaration node. </summary>
    public LocalTypeDeclaration(TypeDeclaration type) => Type = type;
}
