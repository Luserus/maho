using System;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Symbols;

/// <summary>
/// Source-backed symbol name that can participate in equality and hashing without materializing a
/// managed string first.
/// </summary>
internal readonly struct SymbolName : IEquatable<SymbolName>
{
    /// <summary> Backing source text when this name points directly into parsed source. </summary>
    private readonly SourceText? source;
    /// <summary> Slice of the backing source text for source-backed names. </summary>
    private readonly TextSpan span;
    /// <summary> Literal fallback used when a name does not come from source text. </summary>
    private readonly string? literal;

    private SymbolName(SourceText? source, TextSpan span, string? literal)
    {
        this.source = source;
        this.span = span;
        this.literal = literal;
    }

    /// <summary> Character length of the name without forcing string materialization. </summary>
    public int Length => literal?.Length ?? span.Length;

    /// <summary> Indicates whether this name is empty. Used for synthetic root names. </summary>
    public bool IsEmpty => Length == 0;

    /// <summary> Shared empty name used by synthetic/global declarations. </summary>
    public static SymbolName Empty { get; } = FromLiteral(string.Empty);

    /// <summary> Creates a source-backed name directly from a token span. </summary>
    public static SymbolName FromToken(Token token) => new(token.Source, token.Span, literal: null);

    /// <summary> Creates a literal-backed name when no source span exists. </summary>
    public static SymbolName FromLiteral(string value) => new(source: null, default, value);

    /// <summary> Exposes the underlying character span without allocating a managed string. </summary>
    public ReadOnlySpan<char> AsSpan() => literal is not null ? literal.AsSpan() : source!.AsSpan(span);

    /// <summary> Value equality compares characters, not object identity. </summary>
    public bool Equals(SymbolName other) => AsSpan().SequenceEqual(other.AsSpan());

    public override bool Equals(object? obj) => obj is SymbolName other && Equals(other);

    /// <summary> Hashes the character contents so names can serve as scope dictionary keys. </summary>
    public override int GetHashCode()
    {
        HashCode hash = new();
        ReadOnlySpan<char> value = AsSpan();

        for (int i = 0; i < value.Length; i++)
            hash.Add(value[i]);

        return hash.ToHashCode();
    }

    /// <summary> Materializes the name as a string on demand. </summary>
    public override string ToString() => literal ?? source!.ToString(span);

    public static bool operator ==(SymbolName left, SymbolName right) => left.Equals(right);

    public static bool operator !=(SymbolName left, SymbolName right) => !left.Equals(right);
}
