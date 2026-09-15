namespace Maho.Syntax;

/// <summary> Top-level ambiguous reference declaration statement. </summary>
internal sealed class TopLevelAmbiguousReferenceDeclaration : TopLevel
{
    /// <summary> The ambiguous reference declaration. </summary>
    public AmbiguousReferenceDeclaration Declaration { get; }
    /// <summary> The statement terminator. </summary>
    public Token Semicolon { get; }

    /// <summary> Creates one top-level ambiguous reference declaration statement node. </summary>
    public TopLevelAmbiguousReferenceDeclaration(AmbiguousReferenceDeclaration declaration, Token semicolon)
    {
        Declaration = declaration;
        Semicolon = semicolon;
    }
}
