using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed class ParameterVariableDeclarator : SyntaxNode
{
    public IReadOnlyList<Token> Modifiers { get; }
    public NamedSyntax Type { get; }
    public IdentifierName Identifier { get; }

    public ParameterVariableDeclarator(IReadOnlyList<Token> modifiers, NamedSyntax type, IdentifierName identifier)
    {
        Modifiers = modifiers;
        Type = type;
        Identifier = identifier;
    }
}