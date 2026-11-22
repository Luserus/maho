using System;
using System.Numerics;

namespace Maho.Resolution;

public readonly struct ConstantValue : IEquatable<ConstantValue>
{
    public ConstantValueKind Kind { get; }
    public object? Value { get; }

    public bool AsBool => Kind == ConstantValueKind.Bool ? (bool)Value! : throw new InvalidOperationException("Constant is not Bool");
    public BigInteger AsInt => Kind == ConstantValueKind.Int ? (BigInteger)Value! : throw new InvalidOperationException("Constant is not Int");
    public double AsFloat => Kind == ConstantValueKind.Float ? (double)Value! : throw new InvalidOperationException("Constant is not Float");
    public uint AsChar => Kind == ConstantValueKind.Char ? (uint)Value! : throw new InvalidOperationException("Constant is not Char");
    public string AsStr => Kind == ConstantValueKind.Str ? (string)Value! : throw new InvalidOperationException("Constant is not Str");

    public bool IsTrue => Kind == ConstantValueKind.Bool && (bool)Value!;
    public bool IsFalse => Kind == ConstantValueKind.Bool && !(bool)Value!;

    public ConstantValue(ConstantValueKind kind, object? value)
    {
        Kind = kind;
        Value = value;
    }

    public static ConstantValue FromBool(bool val) => new(ConstantValueKind.Bool, val);
    public static ConstantValue FromInt(BigInteger val) => new(ConstantValueKind.Int, val);
    public static ConstantValue FromFloat(double val) => new(ConstantValueKind.Float, val);
    public static ConstantValue FromChar(uint val) => new(ConstantValueKind.Char, val);
    public static ConstantValue FromStr(string val) => new(ConstantValueKind.Str, val);
    public static ConstantValue None => default;

    public bool Equals(ConstantValue other) =>
        Kind == other.Kind && Equals(Value, other.Value);

    public override bool Equals(object? obj) =>
        obj is ConstantValue other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Kind, Value);

    public override string ToString() => Kind switch
    {
        ConstantValueKind.Bool => ((bool)Value!).ToString().ToLowerInvariant(),
        ConstantValueKind.Int => ((BigInteger)Value!).ToString(),
        ConstantValueKind.Float => ((double)Value!).ToString("R"),
        ConstantValueKind.Char => $"'{char.ConvertFromUtf32((int)(uint)Value!)}'",
        ConstantValueKind.Str => $"\"{Value}\"",
        _ => "<none>"
    };

    public static bool operator ==(ConstantValue left, ConstantValue right) => left.Equals(right);
    public static bool operator !=(ConstantValue left, ConstantValue right) => !left.Equals(right);
}

