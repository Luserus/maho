using System;

namespace Maho.Resolution;

internal struct ScopeID
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

    public static bool operator ==(ScopeID id, ScopeID other) => id.Value == other.Value;

    public static bool operator !=(ScopeID id, ScopeID other) => id.Value != other.Value;

    public override bool Equals(object? obj) => obj is not null and ScopeID id ? id.Value == Value : false;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Value);
        return hash.ToHashCode();
    }
}


