using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed class VariableDeclaration : SyntaxNode
{
    public IReadOnlyList<Token> Modifiers { get; }
    /// <summary> The type of the variable. </summary>
    public NamedSyntax Type { get; }
    public SeparatedSyntaxList<VariableDeclarator> Declarators { get; }

    public VariableDeclaration(IReadOnlyList<Token> modifiers, NamedSyntax type, SeparatedSyntaxList<VariableDeclarator> declarators)
    {
        Modifiers = modifiers;
        Type = type;
        Declarators = declarators;
    }
}