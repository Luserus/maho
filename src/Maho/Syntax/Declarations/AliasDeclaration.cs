using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary>Declares a type alias with an optional generic parameter list and constraints.</summary>
internal sealed class AliasDeclaration : SyntaxNode
{
    public Token Keyword { get; }
    public NamedSyntax Name { get; }
    public IReadOnlyList<TypeConstraintClause> Constraints { get; }
    public Token EqualsToken { get; }
    public TypeSyntax Target { get; }
    public Token Semicolon { get; }

    public AliasDeclaration(Token keyword, NamedSyntax name, IReadOnlyList<TypeConstraintClause> constraints, Token equals, TypeSyntax target, Token semicolon)
    {
        Keyword = keyword;
        Name = name;
        Constraints = constraints;
        EqualsToken = equals;
        Target = target;
        Semicolon = semicolon;
    }
}