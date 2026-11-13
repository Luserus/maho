using Maho.Resolution;
using Maho.Syntax;
using Xunit;

namespace Maho.Tests;

public sealed class ImplicitStructDerivingTests
{
    [Fact]
    public void MinimalCompilation_WithoutStructIntrinsic_ReportsMH0240()
    {
        var preludeWithoutStruct = CompilerTestBed.FullPrelude.Replace(
            "\"Struct\"", "\"NotStruct\"");

        var compilation = Compilation.FromSource(preludeWithoutStruct + """
            public struct MyPoint;
            public struct MyVector;
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0240" && d.Message.Contains("Struct"));
    }

    [Fact]
    public void UserStruct_ImplicitlyDerivesFromStruct_WhenIntrinsicExists()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute IntrinsicType(Str name);

            [IntrinsicType("Int8")] public struct int8;
            [IntrinsicType("Int64")] public struct int64;
            [IntrinsicType("Reference")] public struct Reference<T> { nuint raw; }
            [IntrinsicType("Str")] public struct Str { int8& buffer; nuint length; }
            [IntrinsicType("NUInt")] public struct nuint;
            [IntrinsicType("Unit")] public struct void;

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

        var voidSym = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "void");
        Assert.Contains(TypeRef.Resolved(hValType), voidSym.BaseTypes);
    }

    [Fact]
    public void Hierarchy_Void_DerivesFromValueType()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute IntrinsicType(Str name);

            [IntrinsicType("Int8")] public struct int8;
            [IntrinsicType("Int64")] public struct int64;
            [IntrinsicType("Reference")] public struct Reference<T> { nuint raw; }
            [IntrinsicType("Str")] public struct Str { int8& buffer; nuint length; }
            [IntrinsicType("NUInt")] public struct nuint;

            [IntrinsicType("ValueType")]
            public struct ValType;

            [IntrinsicType("Struct")]
            public struct Struct : ValType;

            [IntrinsicType("Unit")]
            public struct void;
            """);

        Assert.False(compilation.HasErrors);
        var context = compilation.Context!;

        Assert.True(context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.ValueType, out var hValType));
        var voidSym = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "void");
        Assert.Contains(TypeRef.Resolved(hValType), voidSym.BaseTypes);
    }

    [Fact]
    public void Hierarchy_Enum_DerivesFromValType_AndUserEnumDerivesFromEnum()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute IntrinsicType(Str name);

            [IntrinsicType("Int8")] public struct int8;
            [IntrinsicType("Int64")] public struct int64;
            [IntrinsicType("Reference")] public struct Reference<T> { nuint raw; }
            [IntrinsicType("Str")] public struct Str { int8& buffer; nuint length; }
            [IntrinsicType("NUInt")] public struct nuint;

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
