namespace Maho.Syntax;

/// <summary> Base initializer clause appearing on a function signature: <c>: Base1(a), Base2(b)</c>. </summary>
internal sealed class BaseInitializerClause : SyntaxNode
{
    public Token Colon { get; }
    public SeparatedSyntaxList<BaseInitializer> Initializers { get; }

    public BaseInitializerClause(Token colon, SeparatedSyntaxList<BaseInitializer> initializers)
    {
        Colon = colon;
        Initializers = initializers;
    }
}

