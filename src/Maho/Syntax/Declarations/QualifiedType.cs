namespace Maho.Syntax;

/// <summary> Type syntax composed from dot-separated type parts. </summary>
internal sealed class QualifiedType : TypeSyntax
{
    /// <summary> Left-hand type part. </summary>
    public TypeSyntax Left { get; }
    /// <summary> Dot token between the parts. </summary>
    public Token Dot { get; }
    /// <summary> Right-hand type part. </summary>
    public TypeSyntax Right { get; }

    /// <summary> Creates one qualified type node. </summary>
    public QualifiedType(TypeSyntax left, Token dot, TypeSyntax right)
    {
        Left = left;
        Dot = dot;
        Right = right;
    }
}
