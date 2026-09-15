namespace Maho.Syntax;

/// <summary> Object-style <c>with</c> initializer clause for creation expressions. </summary>
internal sealed class ObjectWithClause : SyntaxNode
{
    /// <summary> With keyword token. </summary>
    public Token WithKeyword { get; }
    /// <summary> Initializer body after the with keyword. </summary>
    public CollectionInitializer Initializer { get; }

    /// <summary> Creates one object-style with clause. </summary>
    public ObjectWithClause(Token withKeyword, CollectionInitializer initializer)
    {
        WithKeyword = withKeyword;
        Initializer = initializer;
    }
}
