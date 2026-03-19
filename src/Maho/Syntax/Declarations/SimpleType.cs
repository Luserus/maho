namespace Maho.Syntax;

internal sealed class SimpleType : TypeSyntax
{
    public Token Name { get; }

    public SimpleType(Token name)
    {
        Name = name;
    }
}
