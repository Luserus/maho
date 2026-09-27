using System;
using System.Linq;
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

    public HygieneId Hygiene { get; }

    private SymbolPart(SourceText? source, TextSpan span, string? literal, int arity, HygieneId hygiene)
    {
        this.source = source;
        this.span = span;
        this.literal = literal;
        Arity = arity;
        Hygiene = hygiene;
    }

    public SymbolPart(Token token, int arity = 0, HygieneId hygiene = default) : this(token.Source, token.Span, null, arity, hygiene) { }

    public SymbolPart(string literal, int arity = 0, HygieneId hygiene = default) : this(null, default, literal, arity, hygiene) { }

    public ReadOnlySpan<char> AsSpan() => literal is not null ? literal.AsSpan() : source!.AsSpan(span);

    public bool Equals(SymbolPart other) => Arity == other.Arity && Hygiene == other.Hygiene && AsSpan().SequenceEqual(other.AsSpan());

    public override bool Equals(object? obj) => obj is SymbolPart other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        var value = AsSpan();

        hash.Add(Arity);
        hash.Add(Hygiene);

        foreach (char ch in value)
            hash.Add(ch);

        return hash.ToHashCode();
    }

    public string Text => literal ?? source!.ToString(span);

    public string ToDisplayString()
    {
        string text = Text;
        if (Arity == 0)
            return text;

        if (Arity == 1)
            return $"{text}<T>";

        var args = Enumerable.Range(1, Arity).Select(i => $"T{i}");
        return $"{text}<{string.Join(", ", args)}>";
    }

    public override string ToString() => Text;

    public static bool operator ==(SymbolPart syname, SymbolPart other) => syname.Equals(other);

    public static bool operator !=(SymbolPart syname, SymbolPart other) => !syname.Equals(other);
}