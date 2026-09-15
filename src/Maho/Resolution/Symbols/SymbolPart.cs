using System;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

internal readonly struct SymbolPart : IEquatable<SymbolPart>
{
    private readonly SourceText? source;
    private readonly TextSpan span;
    private readonly string? literal;

    public int Length => literal?.Length ?? span.Length;
    
    public int Arity { get; }

    private SymbolPart(SourceText? source, TextSpan span, string? literal, int arity)
    {
        this.source = source;
        this.span = span;
        this.literal = literal;
        Arity = arity;
    }

    public SymbolPart(Token token, int arity = 0) : this(token.Source, token.Span, null, arity) { }

    public SymbolPart(string literal, int arity = 0) : this(null, default, literal, arity) { }

    public ReadOnlySpan<char> AsSpan() => literal is not null ? literal.AsSpan() : source!.AsSpan(span);

    public bool Equals(SymbolPart other) => Arity == other.Arity && AsSpan().SequenceEqual(other.AsSpan());

    public override bool Equals(object? obj) => obj is SymbolPart other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        var value = AsSpan();

        hash.Add(Arity);

        foreach (char ch in value)
            hash.Add(ch);

        return hash.ToHashCode();
    }

    public override string ToString() => literal ?? source!.ToString(span);

    public static bool operator ==(SymbolPart syname, SymbolPart other) => syname.Equals(other);

    public static bool operator !=(SymbolPart syname, SymbolPart other) => !syname.Equals(other);
}