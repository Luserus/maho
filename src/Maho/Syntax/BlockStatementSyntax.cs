namespace Maho.Syntax;

/// <summary> Represents a number of statements within a block enclosed by curly braces. </summary>
/// <param name="leftCurlyBrace"> The start of the block. </param>
/// <param name="statements"> The statements within the block. </param>
/// <param name="rightCurlyBrace"> The end of the block. </param>
internal sealed class BlockStatementSyntax(Token leftCurlyBrace, StatementSyntax[] statements, Token rightCurlyBrace) : StatementSyntax
{
    /// <summary> The start of the block. </summary>
    public Token LeftCurlyBrace { get; } = leftCurlyBrace;
    /// <summary> The statements within the block. </summary>
    public StatementSyntax[] Statements { get; } = statements;
    /// <summary> The end of the block. </summary>
    public Token RightCurlyBrace { get; } = rightCurlyBrace;
}