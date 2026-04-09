namespace Maho.Syntax;

/// <summary> Object-creation expression for constructor invocation. </summary>
internal sealed class ConstructorCallExpression : ObjectCreationExpression
{
    /// <summary> Constructed type syntax. </summary>
    public TypeSyntax Type { get; }
    /// <summary> Opening parenthesis token. </summary>
    public Token OpenParen { get; }
    /// <summary> Constructor arguments. </summary>
    public SeparatedSyntaxList<Expression> Arguments { get; }
    /// <summary> Closing parenthesis token. </summary>
    public Token CloseParen { get; }

    /// <summary> Creates one constructor-call expression node. </summary>
    public ConstructorCallExpression(Token keyword, ObjectCreationKind kind, TypeSyntax type, Token openParen, SeparatedSyntaxList<Expression> arguments, Token closeParen) : base(keyword, kind)
    {
        Type = type;
        OpenParen = openParen;
        Arguments = arguments;
        CloseParen = closeParen;
    }
}
