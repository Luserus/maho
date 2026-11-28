namespace Maho.Syntax;

/// <summary> Object-creation expression using direct field initialization without calling a constructor (e.g. <c>new T { x = 1, y = 2 }</c>). </summary>
internal sealed class ObjectInitializerExpression : ObjectCreationExpression
{
    /// <summary> Opening angle bracket for allocation generic arguments, if any. </summary>
    public Token? AllocationLessThan { get; }
    /// <summary> Allocation generic arguments, if any. </summary>
    public SeparatedSyntaxList<TypeSyntax>? AllocationTypeArguments { get; }
    /// <summary> Closing angle bracket for allocation generic arguments, if any. </summary>
    public Token? AllocationGreaterThan { get; }

    /// <summary> Opening parenthesis for allocation arguments, if any. </summary>
    public Token? AllocationOpenParen { get; }
    /// <summary> Allocation arguments, if any. </summary>
    public SeparatedSyntaxList<Expression>? AllocationArguments { get; }
    /// <summary> Closing parenthesis for allocation arguments, if any. </summary>
    public Token? AllocationCloseParen { get; }

    /// <summary> Target type syntax being initialized. </summary>
    public TypeSyntax Type { get; }
    /// <summary> Brace-enclosed direct field assignments. </summary>
    public TypeInitializer Initializer { get; }

    public ObjectInitializerExpression(
        Token keyword,
        ObjectCreationKind kind,
        Token? allocationLessThan,
        SeparatedSyntaxList<TypeSyntax>? allocationTypeArguments,
        Token? allocationGreaterThan,
        Token? allocationOpenParen,
        SeparatedSyntaxList<Expression>? allocationArguments,
        Token? allocationCloseParen,
        TypeSyntax type,
        TypeInitializer initializer)
        : base(keyword, kind)
    {
        AllocationLessThan = allocationLessThan;
        AllocationTypeArguments = allocationTypeArguments;
        AllocationGreaterThan = allocationGreaterThan;
        AllocationOpenParen = allocationOpenParen;
        AllocationArguments = allocationArguments;
        AllocationCloseParen = allocationCloseParen;
        Type = type;
        Initializer = initializer;
    }
}

