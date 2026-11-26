namespace Maho.Syntax;

/// <summary> Parenthesized grouped type syntax (<c>(T)</c>) used to explicitly control modifier precedence. </summary>
internal sealed class GroupedType : TypeSyntax
{
    /// <summary> Opening parenthesis token <c>(</c>. </summary>
    public Token OpenParen { get; }

    /// <summary> Inner parenthesized type syntax. </summary>
    public TypeSyntax InnerType { get; }

    /// <summary> Closing parenthesis token <c>)</c>. </summary>
    public Token CloseParen { get; }

    /// <summary> Creates one grouped type node. </summary>
    public GroupedType(Token openParen, TypeSyntax innerType, Token closeParen)
    {
        OpenParen = openParen;
        InnerType = innerType;
        CloseParen = closeParen;
    }
}

