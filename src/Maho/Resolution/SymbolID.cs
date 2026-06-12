using System;

namespace Maho.Resolution;

internal struct SymbolID
{
    public int Value;

    public SymbolID(int value)
    {
        Value = value;
    }

    public static implicit operator SymbolID(int value) => new SymbolID(value);

    public static implicit operator int(SymbolID id) => id.Value;

    public static bool operator ==(SymbolID id, int value) => id.Value == value;

    public static bool operator !=(SymbolID id, int value) => id.Value != value;

    public static bool operator ==(int value, SymbolID id) => value == id.Value;

    public static bool operator !=(int value, SymbolID id) => value != id.Value;

    public static bool operator ==(SymbolID id, SymbolID other) => id.Value == other.Value;

    public static bool operator !=(SymbolID id, SymbolID other) => id.Value != other.Value;

    public override bool Equals(object? obj) => obj is not null and SymbolID id ? id.Value == Value : false;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Value);
        return hash.ToHashCode();
    }
}

