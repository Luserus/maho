using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Variadic postfix type modifier (...). </summary>
internal sealed class VariadicTypeModifier : PostfixTypeModifier
{
    /// <summary> Ellipsis tokens. </summary>
    public IReadOnlyList<Token> Ellipsis { get; }

    /// <summary> Creates one variadic modifier node. </summary>
    public VariadicTypeModifier(IReadOnlyList<Token> ellipsis) : base(PostfixTypeModifierKind.Variadic)
    {
        Ellipsis = ellipsis;
    }
}

