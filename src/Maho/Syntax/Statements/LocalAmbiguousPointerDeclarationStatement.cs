namespace Maho.Syntax;

/// <summary> Local ambiguous pointer declaration statement. </summary>
internal sealed class LocalAmbiguousPointerDeclarationStatement : LocalStatement
{
    /// <summary> The ambiguous pointer declaration. </summary>
    public AmbiguousPointerDeclaration Declaration { get; }
    /// <summary> The statement terminator. </summary>
    public Token Semicolon { get; }

    /// <summary> Creates one local ambiguous pointer declaration statement node. </summary>
    public LocalAmbiguousPointerDeclarationStatement(AmbiguousPointerDeclaration declaration, Token semicolon)
    {
        Declaration = declaration;
        Semicolon = semicolon;
    }
}
