namespace Maho.Syntax;

/// <summary> Object-creation expression for constructor invocation. </summary>
internal sealed class ConstructorCallExpression : ObjectCreationExpression
{
    /// <summary> Opening angle bracket for allocation generic arguments, if any. </summary>
    public Token? AllocationLessThan { get; }
    /// <summary> Allocation generic arguments, if any (e.g. new&lt;Arena&gt;). </summary>
    public SeparatedSyntaxList<TypeSyntax>? AllocationTypeArguments { get; }
    /// <summary> Closing angle bracket for allocation generic arguments, if any. </summary>
    public Token? AllocationGreaterThan { get; }

    /// <summary> Opening parenthesis for allocation arguments, if any. </summary>
    public Token? AllocationOpenParen { get; }
    /// <summary> Allocation arguments, if any (e.g. new(pool) or new&lt;Arena&gt;(pool)). </summary>
    public SeparatedSyntaxList<Expression>? AllocationArguments { get; }
    /// <summary> Closing parenthesis for allocation arguments, if any. </summary>
    public Token? AllocationCloseParen { get; }

    /// <summary> Constructed type syntax. </summary>
    public TypeSyntax Type { get; }
    /// <summary> Opening parenthesis token. </summary>
    public Token OpenParen { get; }
    /// <summary> Constructor arguments. </summary>
    public SeparatedSyntaxList<Expression> Arguments { get; }
    /// <summary> Closing parenthesis token. </summary>
    public Token CloseParen { get; }
    /// <summary> Optional object initializer clause. </summary>
    public ObjectWithClause? WithClause { get; }

    /// <summary> Full constructor for constructor-call expression node with allocation arguments. </summary>
    public ConstructorCallExpression(
        Token keyword,
        ObjectCreationKind kind,
        Token? allocationLessThan,
        SeparatedSyntaxList<TypeSyntax>? allocationTypeArguments,
        Token? allocationGreaterThan,
        Token? allocationOpenParen,
        SeparatedSyntaxList<Expression>? allocationArguments,
        Token? allocationCloseParen,
        TypeSyntax type,
        Token openParen,
        SeparatedSyntaxList<Expression> arguments,
        Token closeParen,
        ObjectWithClause? withClause)
        : base(keyword, kind)
    {
        AllocationLessThan = allocationLessThan;
        AllocationTypeArguments = allocationTypeArguments;
        AllocationGreaterThan = allocationGreaterThan;
        AllocationOpenParen = allocationOpenParen;
        AllocationArguments = allocationArguments;
        AllocationCloseParen = allocationCloseParen;
        Type = type;
        OpenParen = openParen;
        Arguments = arguments;
        CloseParen = closeParen;
        WithClause = withClause;
    }

    /// <summary> Creates one constructor-call expression node without allocation arguments. </summary>
    public ConstructorCallExpression(
        Token keyword,
        ObjectCreationKind kind,
        TypeSyntax type,
        Token openParen,
        SeparatedSyntaxList<Expression> arguments,
        Token closeParen,
        ObjectWithClause? withClause)
        : this(keyword, kind, null, null, null, null, null, null, type, openParen, arguments, closeParen, withClause)
    {
    }
}
