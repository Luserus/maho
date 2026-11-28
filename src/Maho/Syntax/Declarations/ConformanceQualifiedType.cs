namespace Maho.Syntax;

/// <summary> Generic type argument pairing a concrete type with an explicit interface conformance (<c>Type as Interface</c>). </summary>
internal sealed class ConformanceQualifiedType : TypeSyntax
{
    /// <summary> Target concrete or generic type argument. </summary>
    public TypeSyntax TargetType { get; }

    /// <summary> The <c>as</c> contextual keyword separating type and interface conformance. </summary>
    public Token AsKeyword { get; }

    /// <summary> Disambiguated interface conformance type. </summary>
    public TypeSyntax InterfaceType { get; }

    public ConformanceQualifiedType(TypeSyntax targetType, Token asKeyword, TypeSyntax interfaceType)
    {
        TargetType = targetType;
        AsKeyword = asKeyword;
        InterfaceType = interfaceType;
    }
}

