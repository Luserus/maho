using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary>
/// Parsed constant declaration (<c>const [Type]? Identifier = Expression;</c>).
/// </summary>
internal sealed class ConstantDeclaration : SyntaxNode
{
    public IReadOnlyList<AttributeListSyntax> Attributes { get; }
    public IReadOnlyList<Token> Modifiers { get; }
    public Token ConstKeyword { get; }
    public TypeSyntax? Type { get; }
    public NamedSyntax Identifier { get; }
    public Token EqualsToken { get; }
    public Expression Value { get; }

    public ConstantDeclaration(
        IReadOnlyList<AttributeListSyntax> attributes,
        IReadOnlyList<Token> modifiers,
        Token constKeyword,
        TypeSyntax? type,
        NamedSyntax identifier,
        Token equalsToken,
        Expression value)
    {
        Attributes = attributes;
        Modifiers = modifiers;
        ConstKeyword = constKeyword;
        Type = type;
        Identifier = identifier;
        EqualsToken = equalsToken;
        Value = value;
    }
}
