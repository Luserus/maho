using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Parameter declarator containing modifiers, type, and identifier. </summary>
internal sealed class ParameterVariableDeclarator : SyntaxNode
{
    /// <summary> Parameter modifiers. </summary>
    public IReadOnlyList<Token> Modifiers { get; }
    /// <summary> Declared parameter type. </summary>
    public TypeSyntax Type { get; }
    /// <summary> Parameter identifier. </summary>
    public NamedSyntax Identifier { get; }

    /// <summary> Creates one parameter variable declarator node. </summary>
    public ParameterVariableDeclarator(IReadOnlyList<Token> modifiers, TypeSyntax type, NamedSyntax identifier)
    {
        Modifiers = modifiers;
        Type = type;
        Identifier = identifier;
    }
}
