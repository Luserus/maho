namespace Maho.Syntax;

internal sealed class QualifiedType : TypeSyntax
{
    public TypeSyntax Left { get; }
    public Token Dot { get; }
    public TypeSyntax Right { get; }

    public QualifiedType(TypeSyntax left, Token dot, TypeSyntax right)
    {
        Left = left;
        Dot = dot;
        Right = right;
    }
}