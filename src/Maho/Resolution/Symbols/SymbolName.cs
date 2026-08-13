using System;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

internal readonly struct SymbolName : IEquatable<SymbolName>
{
    private readonly SourceText? source;
    private readonly TextSpan span;
    private readonly string? literal;

    public int Length => literal?.Length ?? span.Length;

    private SymbolName(SourceText? source, TextSpan span, string? literal)
    {
        this.source = source;
        this.span = span;
        this.literal = literal;
    }

    public SymbolName(Token token) : this(token.Source, token.Span, null) { }

    public SymbolName(string literal) : this(null, default, literal) { }

    public ReadOnlySpan<char> AsSpan() => literal is not null ? literal.AsSpan() : source!.AsSpan(span);

    public bool Equals(SymbolName other) => AsSpan().SequenceEqual(other.AsSpan());

    public override bool Equals(object? obj) => obj is SymbolName other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        var value = AsSpan();

        foreach (char ch in value)
            hash.Add(ch);

        return hash.ToHashCode();
    }

    public override string ToString() => literal ?? source!.ToString(span);

    public static bool operator ==(SymbolName syname, SymbolName other) => syname.Equals(other);

    public static bool operator !=(SymbolName syname, SymbolName other) => !syname.Equals(other);
}

