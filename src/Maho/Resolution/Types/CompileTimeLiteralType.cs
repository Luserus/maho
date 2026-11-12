namespace Maho.Resolution;

/// <summary>
/// Represents an uncollapsed, compile-time literal whose concrete type is inferred from context
/// or defaults to a standard intrinsic primitive. Unsuffixed literals start as instances of this type
/// and collapse retroactively into the first concrete lvalue type encountered.
/// </summary>
internal sealed class CompileTimeLiteralType : SemanticType
{
    /// <inheritdoc/>
    public override SemanticTypeKind Kind => SemanticTypeKind.Literal;

    /// <summary>The scalar literal kind (Int, Float, Str, Char).</summary>
    public CompileTimeLiteralKind LiteralKind { get; }

    /// <summary>The exact raw source text of the literal expression.</summary>
    public string RawText { get; }

    /// <inheritdoc/>
    public override bool IsResolved => true;

    /// <inheritdoc/>
    public override bool IsError => false;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompileTimeLiteralType"/> class.
    /// </summary>
    /// <param name="literalKind">The scalar literal kind.</param>
    /// <param name="rawText">The literal's raw text representation.</param>
    public CompileTimeLiteralType(CompileTimeLiteralKind literalKind, string rawText)
    {
        LiteralKind = literalKind;
        RawText = rawText;
    }

    /// <inheritdoc/>
    public override bool Equals(SemanticType? other) =>
        other is CompileTimeLiteralType lit &&
        lit.LiteralKind == LiteralKind &&
        lit.RawText == RawText;

    /// <inheritdoc/>
    public override int GetHashCode() =>
        System.HashCode.Combine((byte)LiteralKind, RawText);

    /// <inheritdoc/>
    public override string ToString() => $"<literal {LiteralKind}: {RawText}>";
}

