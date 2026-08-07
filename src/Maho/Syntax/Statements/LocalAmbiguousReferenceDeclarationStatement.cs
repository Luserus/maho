namespace Maho.Syntax;

/// <summary> Local ambiguous reference declaration statement. </summary>
internal sealed class LocalAmbiguousReferenceDeclarationStatement : LocalStatement
{
    /// <summary> The ambiguous reference declaration. </summary>
    public AmbiguousReferenceDeclaration Declaration { get; }
    /// <summary> The statement terminator. </summary>
    public Token Semicolon { get; }

    /// <summary> Creates one local ambiguous reference declaration statement node. </summary>
    public LocalAmbiguousReferenceDeclarationStatement(AmbiguousReferenceDeclaration declaration, Token semicolon)
    {
        Declaration = declaration;
        Semicolon = semicolon;
    }
}
