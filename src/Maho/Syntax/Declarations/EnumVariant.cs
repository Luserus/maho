using System.Collections.Generic;

namespace Maho.Syntax;

internal abstract class EnumVariant : SyntaxNode
{
    public IReadOnlyList<AttributeListSyntax> Attributes { get; }
    public NamedSyntax Name { get; }
    public EnumVariantKind Kind { get; }
    public Token? EqualsToken { get; }
    public Expression? ExplicitValue { get; }

    protected EnumVariant(IReadOnlyList<AttributeListSyntax> attributes, NamedSyntax name, EnumVariantKind kind, Token? equalsToken, Expression? explicitValue)
    {
        Attributes = attributes;
        Name = name;
        Kind = kind;
        EqualsToken = equalsToken;
        ExplicitValue = explicitValue;
    }
}

