using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed class VariableDeclaration : SyntaxNode
{
    public IReadOnlyList<Token> Modifiers { get; }
    /// <summary> The type of the variable. </summary>
    public TypeSyntax Type { get; }
    public SeparatedSyntaxList<VariableDeclarator> Declarators { get; }

    public VariableDeclaration(IReadOnlyList<Token> modifiers, TypeSyntax type, SeparatedSyntaxList<VariableDeclarator> declarators)
    {
        Modifiers = modifiers;
        Type = type;
        Declarators = declarators;
    }
}