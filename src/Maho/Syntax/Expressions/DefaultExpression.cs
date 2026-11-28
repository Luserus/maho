namespace Maho.Syntax;

/// <summary> Represents a <c>default</c> or <c>default(Type)</c> zero/default value expression. </summary>
internal sealed class DefaultExpression : Expression
{
    /// <summary> The <c>default</c> keyword token. </summary>
    public Token DefaultKeyword { get; }
    /// <summary> Optional opening parenthesis in <c>default(Type)</c>. </summary>
    public Token? OpenParen { get; }
    /// <summary> Optional explicit target type in <c>default(Type)</c>. </summary>
    public TypeSyntax? ExplicitType { get; }
    /// <summary> Optional closing parenthesis in <c>default(Type)</c>. </summary>
    public Token? CloseParen { get; }

    public DefaultExpression(Token defaultKeyword, Token? openParen = null, TypeSyntax? explicitType = null, Token? closeParen = null)
    {
        DefaultKeyword = defaultKeyword;
        OpenParen = openParen;
        ExplicitType = explicitType;
        CloseParen = closeParen;
    }
}

