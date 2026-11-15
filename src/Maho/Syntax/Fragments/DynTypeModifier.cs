namespace Maho.Syntax;

/// <summary> Dyn prefix type modifier (<c>dyn T</c>). </summary>
internal sealed class DynTypeModifier : PrefixTypeModifier
{
    /// <summary> Dyn keyword token. </summary>
    public Token DynKeyword { get; }

    /// <summary> Creates one dyn modifier node. </summary>
    public DynTypeModifier(Token dynKeyword) : base(PrefixTypeModifierKind.Dyn)
    {
        DynKeyword = dynKeyword;
    }
}

