using System.Collections.Generic;

namespace Maho.Resolution;

internal static class KnownIntrinsics
{
    public const string IntrinsicTypeAttribute = "IntrinsicType";

    // Signed Integers
    public const string Int8 = "Int8";
    public const string Int16 = "Int16";
    public const string Int32 = "Int32";
    public const string Int64 = "Int64";
    public const string Int128 = "Int128";
    public const string NInt = "NInt";

    // Unsigned Integers
    public const string UInt8 = "UInt8";
    public const string UInt16 = "UInt16";
    public const string UInt32 = "UInt32";
    public const string UInt64 = "UInt64";
    public const string UInt128 = "UInt128";
    public const string NUInt = "NUInt";

    // Floating-Point
    public const string Float16 = "Float16";
    public const string Float32 = "Float32";
    public const string Float64 = "Float64";
    public const string Float128 = "Float128";

    // Primitives
    public const string Bool = "Bool";
    public const string Char = "Char";
    public const string Str = "Str";

    // Pointer and Reference
    public const string Pointer = "Pointer";
    public const string Reference = "Reference";

    // Arrays
    public const string FixedArray = "FixedArray";
    public const string Array = "Array";

    public static readonly HashSet<string> AllKnownTypes =
    [
        Int8, Int16, Int32, Int64, Int128, NInt,
        UInt8, UInt16, UInt32, UInt64, UInt128, NUInt,
        Float16, Float32, Float64, Float128,
        Bool, Char, Str,
        Pointer, Reference,
        FixedArray, Array
    ];

    public static readonly HashSet<string> ZeroInstanceFieldPrimitives =
    [
        Int8, Int16, Int32, Int64, Int128, NInt,
        UInt8, UInt16, UInt32, UInt64, UInt128, NUInt,
        Float16, Float32, Float64, Float128,
        Bool, Char,
        FixedArray
    ];
}

