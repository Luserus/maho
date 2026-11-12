using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Represents a pattern matching expression: <c>match (expr) { pattern =&gt; expr, ... }</c>. </summary>
internal sealed class MatchExpression : Expression
{
    public Token MatchKeyword { get; }
    public Token OpenParen { get; }
    public Expression Expression { get; }
    public Token CloseParen { get; }
    public Token OpenBrace { get; }
    public IReadOnlyList<MatchArm> Arms { get; }
    public Token CloseBrace { get; }

    public MatchExpression(
        Token matchKeyword,
        Token openParen,
        Expression expression,
        Token closeParen,
        Token openBrace,
        IReadOnlyList<MatchArm> arms,
        Token closeBrace)
    {
        MatchKeyword = matchKeyword;
        OpenParen = openParen;
        Expression = expression;
        CloseParen = closeParen;
        OpenBrace = openBrace;
        Arms = arms;
        CloseBrace = closeBrace;
    }
}

