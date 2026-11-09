using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed class EnumTupleVariant : EnumVariant
{
    public Token OpenParen { get; }
    public SeparatedSyntaxList<Parameter> Parameters { get; }
    public Token CloseParen { get; }

    public EnumTupleVariant(
        IReadOnlyList<AttributeListSyntax> attributes,
        NamedSyntax name,
        Token openParen,
        SeparatedSyntaxList<Parameter> parameters,
        Token closeParen,
        Token? equalsToken,
        Expression? explicitValue)
        : base(attributes, name, EnumVariantKind.Tuple, equalsToken, explicitValue)
    {
        OpenParen = openParen;
        Parameters = parameters;
        CloseParen = closeParen;
    }
}

