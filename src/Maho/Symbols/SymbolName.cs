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
    private readonly SourceText? source;
    private readonly TextSpan span;
    private readonly string? literal;

    private SymbolName(SourceText? source, TextSpan span, string? literal)
    {
        this.source = source;
        this.span = span;
        this.literal = literal;
    }

    public int Length => literal?.Length ?? span.Length;

    public bool IsEmpty => Length == 0;

    public static SymbolName Empty { get; } = FromLiteral(string.Empty);

    public static SymbolName FromToken(Token token) => new(token.Source, token.Span, literal: null);

    public static SymbolName FromLiteral(string value) => new(source: null, default, value);

    public ReadOnlySpan<char> AsSpan() => literal is not null ? literal.AsSpan() : source!.AsSpan(span);

    public bool Equals(SymbolName other) => AsSpan().SequenceEqual(other.AsSpan());

    public override bool Equals(object? obj) => obj is SymbolName other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        ReadOnlySpan<char> value = AsSpan();

        for (int i = 0; i < value.Length; i++)
            hash.Add(value[i]);

        return hash.ToHashCode();
    }

    public override string ToString() => literal ?? source!.ToString(span);

    public static bool operator ==(SymbolName left, SymbolName right) => left.Equals(right);

    public static bool operator !=(SymbolName left, SymbolName right) => !left.Equals(right);
}
