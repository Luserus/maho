using System;

namespace Maho.Resolution;

internal struct ScopeID : IEquatable<ScopeID>
{
    public int Value;

    public ScopeID(int value)
    {
        Value = value;
    }

    public static implicit operator ScopeID(int value) => new ScopeID(value);

    public static implicit operator int(ScopeID id) => id.Value;

    public static bool operator ==(ScopeID id, int value) => id.Value == value;

    public static bool operator !=(ScopeID id, int value) => id.Value != value;

    public static bool operator ==(int value, ScopeID id) => value == id.Value;

    public static bool operator !=(int value, ScopeID id) => value != id.Value;

    public static bool operator ==(ScopeID id, ScopeID other) => id.Equals(other);

    public static bool operator !=(ScopeID id, ScopeID other) => !id.Equals(other);

    public override bool Equals(object? obj) => obj is ScopeID id && Equals(id);

    public override int GetHashCode() => Value;

    public override string ToString() => Value.ToString();

    public bool Equals(ScopeID other) => other.Value == Value;
}


