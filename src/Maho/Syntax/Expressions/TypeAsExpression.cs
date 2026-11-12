namespace Maho.Syntax;

/// <summary>
/// Static dispatch type qualification expression: &lt;TargetType as InterfaceType&gt;.
/// Allows static interface dispatch such as `&lt;Int32 as Add&gt;.Add(a, b)`.
/// </summary>
internal sealed class TypeAsExpression : Expression
{
    public Token LessThan { get; }
    public TypeSyntax TargetType { get; }
    public Token AsKeyword { get; }
    public TypeSyntax InterfaceType { get; }
    public Token GreaterThan { get; }

    public TypeAsExpression(Token lessThan, TypeSyntax targetType, Token asKeyword, TypeSyntax interfaceType, Token greaterThan)
    {
        LessThan = lessThan;
        TargetType = targetType;
        AsKeyword = asKeyword;
        InterfaceType = interfaceType;
        GreaterThan = greaterThan;
    }
}

