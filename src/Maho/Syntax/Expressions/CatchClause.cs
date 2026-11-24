namespace Maho.Syntax;

/// <summary> Represents a catch clause in a try expression: <c>catch [(Type [var])] { ... }</c>. </summary>
internal sealed class CatchClause : SyntaxNode
{
    public Token CatchKeyword { get; }
    public Token? OpenParen { get; }
    public TypeSyntax? Type { get; }
    public Token? VariableIdentifier { get; }
    public Token? CloseParen { get; }
    public BlockExpression Block { get; }

    public CatchClause(
        Token catchKeyword,
        Token? openParen,
        TypeSyntax? type,
        Token? variableIdentifier,
        Token? closeParen,
        BlockExpression block)
    {
        CatchKeyword = catchKeyword;
        OpenParen = openParen;
        Type = type;
        VariableIdentifier = variableIdentifier;
        CloseParen = closeParen;
        Block = block;
    }
}

