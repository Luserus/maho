using System.Collections.Generic;

namespace Maho.Syntax;

internal sealed class TypeSyntax : SyntaxNode
{
    public IReadOnlyList<Token> Modifiers { get; }
    public TypeKind Kind { get; }
    public NamedSyntax Name { get; }
    public TypeBody Body { get; }

    public TypeSyntax(IReadOnlyList<Token> modifiers, TypeKind kind, NamedSyntax name, TypeBody body)
    {
        Modifiers = modifiers;
        Kind = kind;
        Name = name;
        Body = body;
    }
}