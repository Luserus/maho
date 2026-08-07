global using SymbolHandle = (Maho.Resolution.SymbolKind Kind, Maho.Resolution.SymbolID ID);

using System;

namespace Maho.Resolution;

internal struct SymbolID : IEquatable<SymbolID>
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

    public static bool operator ==(SymbolID id, SymbolID other) => id.Equals(other);

    public static bool operator !=(SymbolID id, SymbolID other) => !id.Equals(other);

    public override bool Equals(object? obj) => obj is SymbolID id && Equals(id);

    public override int GetHashCode() => Value;

    public override string ToString() => Value.ToString();

    public bool Equals(SymbolID other) => other.Value == Value;
}

