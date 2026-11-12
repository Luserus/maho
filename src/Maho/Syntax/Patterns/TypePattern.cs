namespace Maho.Syntax;

/// <summary> Pattern matching a type in a union or type check, e.g. <c>A</c> or <c>A a</c>. </summary>
internal sealed class TypePattern : PatternSyntax
{
    public TypeSyntax Type { get; }
    public NamedSyntax? Variable { get; }

    public TypePattern(TypeSyntax type, NamedSyntax? variable = null)
    {
        Type = type;
        Variable = variable;
    }
}

