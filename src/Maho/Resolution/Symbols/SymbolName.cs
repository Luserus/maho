using System;
using System.Collections.Generic;
using System.Linq;

namespace Maho.Resolution;

internal readonly struct SymbolName : IEquatable<SymbolName>
{
    private readonly SymbolPart[] parts;

    public int Count => parts?.Length ?? 0;

    public bool IsQualified => Count > 1;

    public SymbolPart this[int index] => parts[index];

    public SymbolPart First => parts[0];

    public SymbolPart Last => parts[^1];

    public IReadOnlyList<SymbolPart> Parts => parts ?? [];

    public SymbolName(SymbolPart[] parts) => this.parts = parts;

    public SymbolName(SymbolPart part) : this([part]) { }

    public bool Equals(SymbolName other)
    {
        if (parts is null && other.parts is null) return true;
        if (parts is null || other.parts is null) return false;
        if (parts.Length != other.parts.Length) return false;

        for (int i = 0; i < parts.Length; i++)
        {
            if (!parts[i].Equals(other.parts[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is SymbolName other && Equals(other);

    public override int GetHashCode()
    {
        if (parts is null) return 0;
        var hash = new HashCode();
        for (int i = 0; i < parts.Length; i++)
            hash.Add(parts[i]);
        return hash.ToHashCode();
    }

    public static bool operator ==(SymbolName left, SymbolName right) => left.Equals(right);
    public static bool operator !=(SymbolName left, SymbolName right) => !left.Equals(right);

    public string ToDisplayString() => parts is null ? string.Empty : string.Join('.', parts.Select(p => p.ToDisplayString()));

    public override string ToString() => ToDisplayString();
}