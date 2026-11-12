using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary>
/// Represents a literal macro declaration (e.g. `literal s(int V) => Duration.FromSeconds(V);`).
/// </summary>
internal sealed class LiteralMacroDeclaration : SyntaxNode
{
    public IReadOnlyList<AttributeListSyntax> Attributes { get; }
    public IReadOnlyList<Token> Modifiers { get; }
    public Token LiteralKeyword { get; }
    public Token Suffix { get; }
    public Token OpenParen { get; }
    public Token KindToken { get; }
    public LiteralMacroKind LiteralKind { get; }
    public Token ParameterIdentifier { get; }
    public Token CloseParen { get; }
    public Expression Body { get; }
    public Token? Semicolon { get; }

    public LiteralMacroDeclaration(
        IReadOnlyList<AttributeListSyntax> attributes,
        IReadOnlyList<Token> modifiers,
        Token literalKeyword,
        Token suffix,
        Token openParen,
        Token kindToken,
        LiteralMacroKind literalKind,
        Token parameterIdentifier,
        Token closeParen,
        Expression body,
        Token? semicolon = null)
    {
        Attributes = attributes;
        Modifiers = modifiers;
        LiteralKeyword = literalKeyword;
        Suffix = suffix;
        OpenParen = openParen;
        KindToken = kindToken;
        LiteralKind = literalKind;
        ParameterIdentifier = parameterIdentifier;
        CloseParen = closeParen;
        Body = body;
        Semicolon = semicolon;
    }
}

