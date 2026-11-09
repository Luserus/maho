using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed class EnumStructVariant : EnumVariant
{
    public Token OpenBrace { get; }
    public IReadOnlyList<MemberFieldDeclaration> Fields { get; }
    public Token CloseBrace { get; }

    public EnumStructVariant(
        IReadOnlyList<AttributeListSyntax> attributes,
        NamedSyntax name,
        Token openBrace,
        IReadOnlyList<MemberFieldDeclaration> fields,
        Token closeBrace,
        Token? equalsToken,
        Expression? explicitValue)
        : base(attributes, name, EnumVariantKind.Struct, equalsToken, explicitValue)
    {
        OpenBrace = openBrace;
        Fields = fields;
        CloseBrace = closeBrace;
    }
}

