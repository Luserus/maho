namespace Maho.Syntax;

/// <summary>
/// A parameter in a lambda expression, with an identifier and an optional type annotation.
/// Examples: x, or T x.
/// </summary>
internal sealed class LambdaParameterSyntax : SyntaxNode
{
    public NamedSyntax Identifier { get; }
    public TypeSyntax? Type { get; }

    public LambdaParameterSyntax(NamedSyntax identifier, TypeSyntax? type = null)
    {
        Identifier = identifier;
        Type = type;
    }
}
