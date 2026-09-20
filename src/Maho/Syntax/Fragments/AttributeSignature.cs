using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed class AttributeSignature : SyntaxNode
{
    public IReadOnlyList<AttributeListSyntax> Attributes { get; }
    public IReadOnlyList<Token> Modifiers { get; }
    public Token Keyword { get; }
    public NamedSyntax Name { get; }
    public AttributeParameters? Parameters { get; }

    public AttributeSignature(IReadOnlyList<AttributeListSyntax> attributes, IReadOnlyList<Token> modifiers, Token keyword, NamedSyntax name, AttributeParameters? parameters)
    {
        Attributes = attributes;
        Modifiers = modifiers;
        Keyword = keyword;
        Name = name;
        Parameters = parameters;
    }
}