namespace Maho.Syntax;

/// <summary> Pattern that binds an expression to a variable name, e.g. <c>x</c>. </summary>
internal sealed class VariablePattern : PatternSyntax
{
    public NamedSyntax Name { get; }

    public VariablePattern(NamedSyntax name)
    {
        Name = name;
    }
}

