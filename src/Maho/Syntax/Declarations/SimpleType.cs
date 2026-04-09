namespace Maho.Syntax;

/// <summary> Type syntax consisting of a single identifier token. </summary>
internal sealed class SimpleType : TypeSyntax
{
    /// <summary> Identifier token naming the type. </summary>
    public Token Name { get; }

    /// <summary> Creates one simple type node. </summary>
    public SimpleType(Token name)
    {
        Name = name;
    }
}
