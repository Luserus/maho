namespace Maho.Syntax;

/// <summary> Name syntax consisting of a single identifier token. </summary>
internal sealed class SimpleName : NamedSyntax
{
    /// <summary> Identifier token for the name. </summary>
    public Token Name { get; }

    /// <summary> Creates one simple name node. </summary>
    public SimpleName(Token name) => Name = name;
}
