using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed class EnumUnitVariant : EnumVariant
{
    public EnumUnitVariant(IReadOnlyList<AttributeListSyntax> attributes, NamedSyntax name, Token? equalsToken, Expression? explicitValue)
        : base(attributes, name, EnumVariantKind.Unit, equalsToken, explicitValue)
    {
    }
}

