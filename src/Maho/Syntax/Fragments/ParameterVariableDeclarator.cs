using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed class ParameterVariableDeclarator : SyntaxNode
{
    public IReadOnlyList<Token> Modifiers { get; }
    public TypeSyntax Type { get; }
    public NamedSyntax Identifier { get; }

    public ParameterVariableDeclarator(IReadOnlyList<Token> modifiers, TypeSyntax type, NamedSyntax identifier)
    {
        Modifiers = modifiers;
        Type = type;
        Identifier = identifier;
    }
}