namespace Maho.Resolution;

/// <summary>
/// Represents an uncollapsed, compile-time literal whose concrete type is inferred from context
/// or defaults to a standard intrinsic primitive.
/// </summary>
internal sealed class CompileTimeLiteralType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Literal;
    public CompileTimeLiteralKind LiteralKind { get; }
    public string RawText { get; }

    public override bool IsResolved => true;
    public override bool IsError => false;

    public CompileTimeLiteralType(CompileTimeLiteralKind literalKind, string rawText)
    {
        LiteralKind = literalKind;
        RawText = rawText;
    }

    public override bool Equals(SemanticType? other) =>
        other is CompileTimeLiteralType lit &&
        lit.LiteralKind == LiteralKind &&
        lit.RawText == RawText;

    public override int GetHashCode() =>
        System.HashCode.Combine((byte)LiteralKind, RawText);

    public override string ToString() => $"<literal {LiteralKind}: {RawText}>";
}
