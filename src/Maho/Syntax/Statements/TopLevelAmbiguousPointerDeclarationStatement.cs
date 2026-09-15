namespace Maho.Syntax;

/// <summary> Top-level ambiguous pointer declaration statement. </summary>
internal sealed class TopLevelAmbiguousPointerDeclaration : TopLevel
{
    /// <summary> The ambiguous pointer declaration. </summary>
    public AmbiguousPointerDeclaration Declaration { get; }
    /// <summary> The statement terminator. </summary>
    public Token Semicolon { get; }

    /// <summary> Creates one top-level ambiguous pointer declaration statement node. </summary>
    public TopLevelAmbiguousPointerDeclaration(AmbiguousPointerDeclaration declaration, Token semicolon)
    {
        Declaration = declaration;
        Semicolon = semicolon;
    }
}
