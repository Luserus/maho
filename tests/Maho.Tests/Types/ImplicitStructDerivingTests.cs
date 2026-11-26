using Maho.Resolution;

namespace Maho.Tests;

public sealed class ImplicitStructDerivingTests
{
    [Fact]
    public void MinimalCompilation_WithoutStructIntrinsic_ReportsMH0021()
    {
        var preludeWithoutStruct = CompilerTestBed.FullPrelude.Replace(
            "\"Struct\"", "\"NotStruct\"");

        var compilation = Compilation.FromSource(preludeWithoutStruct + """
            public struct MyPoint;
            public struct MyVector;
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0021" && d.Message.Contains("Struct"));
    }

    [Fact]
    public void UserStruct_ImplicitlyDerivesFromStruct_WhenIntrinsicExists()
    {
        var compilation = Compilation.FromSource("""
            [IntrinsicType("Layout")] public interface Layout;
            [IntrinsicType("Sized")] public interface Sized : Layout;
            [IntrinsicType("Unsized")] public interface Unsized : Layout;

            intrinsic attribute IntrinsicType(&Str name);

            [IntrinsicType("UInt8")] public struct uint8;
            [IntrinsicType("Int8")] public struct int8;
            [IntrinsicType("Int64")] public struct int64;
            [IntrinsicType("NUInt")] public struct nuint;
            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;
            [IntrinsicType("Metadata")] public struct Metadata<T> where T : Layout;
            [IntrinsicType("Reference")] public struct Reference<T> where T : Layout { nuint raw; Metadata<T> metadata; }
            [IntrinsicType("Str")] public struct Str { uint8[] data; }

            [IntrinsicType("ValueType")]
            public struct ValType;

            [IntrinsicType("Struct")]
            public struct Struct : ValType;

            public struct UserPoint;
            """);

        Assert.False(compilation.HasErrors);
        var context = compilation.Context!;

        Assert.True(context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.ValueType, out var hValType));
        Assert.True(context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Struct, out var hStruct));

        var structSym = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "Struct");
        Assert.Contains(TypeRef.Resolved(hValType), structSym.BaseTypes);

        var userPointSym = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "UserPoint");
        Assert.Contains(TypeRef.Resolved(hStruct), userPointSym.BaseTypes);

    }

    [Fact]
    public void Hierarchy_Struct_DerivesFromValueType()
    {
        var compilation = Compilation.FromSource("""
            [IntrinsicType("Layout")] public interface Layout;
            [IntrinsicType("Sized")] public interface Sized : Layout;
            [IntrinsicType("Unsized")] public interface Unsized : Layout;

            intrinsic attribute IntrinsicType(&Str name);

            [IntrinsicType("UInt8")] public struct uint8;
            [IntrinsicType("Int8")] public struct int8;
            [IntrinsicType("Int64")] public struct int64;
            [IntrinsicType("NUInt")] public struct nuint;
            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;
            [IntrinsicType("Metadata")] public struct Metadata<T> where T : Layout;
            [IntrinsicType("Reference")] public struct Reference<T> where T : Layout { nuint raw; Metadata<T> metadata; }
            [IntrinsicType("Str")] public struct Str { uint8[] data; }

            [IntrinsicType("ValueType")]
            public struct ValType;

            [IntrinsicType("Struct")]
            public struct Struct : ValType;
            """);

        Assert.False(compilation.HasErrors);
        var context = compilation.Context!;

        Assert.True(context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.ValueType, out var hValType));
        var structSym = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "Struct");
        Assert.Contains(TypeRef.Resolved(hValType), structSym.BaseTypes);
    }

    [Fact]
    public void Hierarchy_Enum_DerivesFromValType_AndUserEnumDerivesFromEnum()
    {
        var compilation = Compilation.FromSource("""
            [IntrinsicType("Layout")] public interface Layout;
            [IntrinsicType("Sized")] public interface Sized : Layout;
            [IntrinsicType("Unsized")] public interface Unsized : Layout;

            intrinsic attribute IntrinsicType(&Str name);

            [IntrinsicType("UInt8")] public struct uint8;
            [IntrinsicType("Int8")] public struct int8;
            [IntrinsicType("Int64")] public struct int64;
            [IntrinsicType("NUInt")] public struct nuint;
            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;
            [IntrinsicType("Metadata")] public struct Metadata<T> where T : Layout;
            [IntrinsicType("Reference")] public struct Reference<T> where T : Layout { nuint raw; Metadata<T> metadata; }
            [IntrinsicType("Str")] public struct Str { uint8[] data; }

            [IntrinsicType("ValueType")]
            public struct ValType;

            [IntrinsicType("Struct")]
            public struct Struct : ValType;

            [IntrinsicType("Enum")]
            public struct Enum : ValType;

            public enum Color
            {
                Red,
                Green,
                Blue
            }
            """);

        Assert.False(compilation.HasErrors);
        var context = compilation.Context!;

        Assert.True(context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.ValueType, out var hValType));
        Assert.True(context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Enum, out var hEnum));

        var enumSym = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "Enum");
        Assert.Contains(TypeRef.Resolved(hValType), enumSym.BaseTypes);

        var colorSym = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "Color");
        Assert.Contains(TypeRef.Resolved(hEnum), colorSym.BaseTypes);
    }
}
