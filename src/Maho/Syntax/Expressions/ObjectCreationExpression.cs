namespace Maho.Syntax;

/// <summary> Base type for expressions that create new objects. </summary>
internal abstract class ObjectCreationExpression : Expression
{
    /// <summary> Object-creation keyword token. </summary>
    public Token Keyword { get; }
    /// <summary> Which creation spelling was used. </summary>
    public ObjectCreationKind Kind { get; }

    /// <summary> Creates one object-creation expression node. </summary>
    public ObjectCreationExpression(Token keyword, ObjectCreationKind kind)
    {
        Keyword = keyword;
        Kind = kind;
    }
}
