using Maho.Resolution;

namespace Maho.Tests;

public sealed partial class IntrinsicDiscoveryTests
{
    private const string IntrinsicBootstrapPrelude = """
        intrinsic attribute IntrinsicType(&Str name);

        [IntrinsicType("Layout")]
        public interface Layout;

        [IntrinsicType("Sized")]
        public interface Sized : Layout;

        [IntrinsicType("Unsized")]
        public interface Unsized : Layout;

        [IntrinsicType("ValueType")]
        public struct ValType;

        [IntrinsicType("Struct")]
        public struct Struct : ValType;

        [IntrinsicType("UInt8")]
        public struct UInt8;

        [IntrinsicType("Int8")]
        public struct Int8;

        [IntrinsicType("Int64")]
        public struct Int64;

        [IntrinsicType("NUInt")]
        public struct NUInt;

        [IntrinsicType("Metadata")]
        public struct Metadata<T> where T : Layout;

        [IntrinsicType("Reference")]
        public struct Reference<T> where T : Layout
        {
            NUInt address;
            Metadata<T> metadata;
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T> where T : Layout
        {
            NUInt address;
            Metadata<T> metadata;
        }

        [IntrinsicType("Slice")]
        public struct Slice<T, D : int = 1>;

        [IntrinsicType("Str")]
        public struct Str
        {
            UInt8[] data;
        }
        """;

    private const string UseSitePrelude = IntrinsicBootstrapPrelude + """
        intrinsic attribute IntrinsicFunction(&Str signature);

        intrinsic attribute OperatorFunction(&Str name);


        public interface Callable<TReturn, TParams...>
        {
            [OperatorFunction("Callable")]
            public func Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            NUInt codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            public func Callable.Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Delegate")]
        public struct Delegate<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            NUInt codeAddress;
            Pointer<ValType> env;
            [IntrinsicFunction("Delegate.Invoke(TParams...)")]
            public func Callable.Invoke(&this, TParams args) -> TReturn;
        }
        """;

    [Fact]
    public void BlankCompilation_SucceedsWithoutIntrinsics()
    {
        var compilation = Compilation.FromSource("");
        Assert.False(compilation.HasErrors);
        Assert.Empty(compilation.Context.DiscoveredIntrinsics.All);
    }

    [Fact]
    public void Compilation_WithUserTypes_SucceedsWithoutIntrinsics()
    {
        var compilation = Compilation.FromSource("""
            public struct Point
            {
                public struct Int;
                public Int x;
                public Int y;
            }
            """);

        Assert.False(compilation.HasErrors);
        Assert.Empty(compilation.Context.DiscoveredIntrinsics.All);
    }

    [Fact]
    public void BootstrapCycle_IntrinsicTypeAttributeAndStr_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude);

        Assert.False(compilation.HasErrors);
        Assert.NotNull(compilation.Context.DiscoveredIntrinsics.IntrinsicTypeAttribute);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Str));
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Int8));
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Int64));
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.NUInt));
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Pointer));
    }

    [Fact]
    public void UnknownIntrinsicAttribute_EmitsMH0540()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute UnknownAttr;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0540");
        Assert.Contains("UnknownAttr", diag.Message);
    }

    [Fact]
    public void IntrinsicType_WrongParameterCount_ZeroParams_EmitsMH0541()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute IntrinsicType;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0541");
        Assert.Contains("must accept exactly one parameter", diag.Message);
    }

    [Fact]
    public void IntrinsicType_WrongParameterCount_TwoParams_EmitsMH0541()
    {
        var compilation = Compilation.FromSource("""
            public struct Str;
            intrinsic attribute IntrinsicType(Str a, Str b);
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0541");
        Assert.Contains("must accept exactly one parameter", diag.Message);
    }

    [Fact]
    public void IntrinsicType_ParameterTypeMismatch_EmitsMH0541()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute IntrinsicType(Int32 name);

            [IntrinsicType("ValueType")] public struct ValType;
            [IntrinsicType("Struct")] public struct Struct : ValType;
            [IntrinsicType("UInt8")] public struct UInt8;
            [IntrinsicType("Int32")] public struct Int32;
            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;
            [IntrinsicType("Str")] public struct Str { UInt8[] data; }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0541");
        Assert.Contains("must be '&Str'", diag.Message);
    }

    [Fact]
    public void UnknownIntrinsicLiteral_EmitsMH0542()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("BogusType")]
            public struct Bogus;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0542");
        Assert.Contains("BogusType", diag.Message);
    }

    [Fact]
    public void DuplicateIntrinsicType_InSameCompilation_EmitsMH0543()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Int32")]
            public struct Int32A;

            [IntrinsicType("Int32")]
            public struct Int32B;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0543");
        Assert.Contains("Int32", diag.Message);
    }

    [Fact]
    public void DuplicateIntrinsicType_AcrossReferencedProjects_EmitsMH0543()
    {
        var libCompilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Int32")]
            public struct Int32;
            """, "Lib.mh");

        Assert.False(libCompilation.HasErrors);

        var consumerCompilation = Compilation.FromSource("""
            [IntrinsicType("Int32")]
            public struct MyInt32;
            """, "App.mh", referencedCompilations: [libCompilation]);

        Assert.True(consumerCompilation.HasErrors);
        var diag = Assert.Single(consumerCompilation.Diagnostics, d => d.Code == "MH0543");
        Assert.Contains("Int32", diag.Message);
    }

    [Fact]
    public void AbiMismatch_PrimitiveWithFields_EmitsMH0020()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Int32")]
            public struct Int32
            {
                Int64 unexpectedField;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0020");
        Assert.Contains("must have zero instance fields", diag.Message);
    }

    [Fact]
    public void AbiMismatch_PrimitiveWithGenericParams_EmitsMH0020()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Int32")]
            public struct Int32<T>;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0020");
        Assert.Contains("cannot have generic parameters", diag.Message);
    }

    [Fact]
    public void AbiMismatch_PointerWithoutNUIntField_EmitsMH0020()
    {
        var compilation = Compilation.FromSource("""
            [IntrinsicType("Layout")] public interface Layout;
            [IntrinsicType("Sized")] public interface Sized : Layout;
            [IntrinsicType("Unsized")] public interface Unsized : Layout;
            intrinsic attribute IntrinsicType(&Str name);
            [IntrinsicType("UInt8")] public struct UInt8;
            [IntrinsicType("Int8")] public struct Int8;
            [IntrinsicType("Int64")] public struct Int64;
            [IntrinsicType("NUInt")] public struct NUInt;
            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;
            [IntrinsicType("Reference")] public struct Reference<T> { NUInt address; }

            [IntrinsicType("Pointer")]
            public struct Pointer<T>
            {
                Int64 address;
            }

            [IntrinsicType("Str")]
            public struct Str
            {
                UInt8[] data;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0020" && d.Message.Contains("must be of type 'NUInt'"));
    }

    [Fact]
    public void AbiMismatch_PointerWithoutNUIntDeclared_EmitsMH0020()
    {
        var compilation = Compilation.FromSource("""
            [IntrinsicType("Layout")] public interface Layout;
            [IntrinsicType("Sized")] public interface Sized : Layout;
            [IntrinsicType("Unsized")] public interface Unsized : Layout;
            intrinsic attribute IntrinsicType(&Str name);
            [IntrinsicType("UInt8")] public struct UInt8;
            [IntrinsicType("Int8")] public struct Int8;
            [IntrinsicType("Int64")] public struct Int64;
            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;

            [IntrinsicType("Pointer")]
            public struct Pointer<T>
            {
                Int64 address;
            }

            [IntrinsicType("Str")]
            public struct Str
            {
                UInt8[] data;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0020" && d.Message.Contains("requires intrinsic 'NUInt'"));
    }

    [Fact]
    public void AbiMismatch_PointerWrongGenericCount_EmitsMH0020()
    {
        var compilation = Compilation.FromSource("""
            [IntrinsicType("Layout")] public interface Layout;
            [IntrinsicType("Sized")] public interface Sized : Layout;
            [IntrinsicType("Unsized")] public interface Unsized : Layout;
            intrinsic attribute IntrinsicType(&Str name);
            [IntrinsicType("UInt8")] public struct UInt8;
            [IntrinsicType("Int8")] public struct Int8;
            [IntrinsicType("Int64")] public struct Int64;
            [IntrinsicType("NUInt")] public struct NUInt;
            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;
            [IntrinsicType("Reference")] public struct Reference<T> { NUInt address; }

            [IntrinsicType("Pointer")]
            public struct Pointer
            {
                NUInt address;
            }

            [IntrinsicType("Str")]
            public struct Str
            {
                UInt8[] data;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0020" && d.Message.Contains("must have exactly 1 generic parameter"));
    }

    [Fact]
    public void AbiMismatch_PointerWrongFieldCount_EmitsMH0020()
    {
        var compilation = Compilation.FromSource("""
            [IntrinsicType("Layout")] public interface Layout;
            [IntrinsicType("Sized")] public interface Sized : Layout;
            [IntrinsicType("Unsized")] public interface Unsized : Layout;
            intrinsic attribute IntrinsicType(&Str name);
            [IntrinsicType("UInt8")] public struct UInt8;
            [IntrinsicType("Int8")] public struct Int8;
            [IntrinsicType("Int64")] public struct Int64;
            [IntrinsicType("NUInt")] public struct NUInt;
            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;
            [IntrinsicType("Reference")] public struct Reference<T> { NUInt address; }

            [IntrinsicType("Pointer")]
            public struct Pointer<T>
            {
                NUInt address;
                NUInt offset;
            }

            [IntrinsicType("Str")]
            public struct Str
            {
                UInt8[] data;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0020" && d.Message.Contains("must have exactly 1 instance field"));
    }

    [Fact]
    public void AbiMismatch_ReferenceRequiresNUIntField_EmitsMH0020()
    {
        var compilation = Compilation.FromSource("""
            [IntrinsicType("Layout")] public interface Layout;
            [IntrinsicType("Sized")] public interface Sized : Layout;
            [IntrinsicType("Unsized")] public interface Unsized : Layout;
            intrinsic attribute IntrinsicType(&Str name);
            [IntrinsicType("UInt8")] public struct UInt8;
            [IntrinsicType("Int8")] public struct Int8;
            [IntrinsicType("Int64")] public struct Int64;
            [IntrinsicType("NUInt")] public struct NUInt;
            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;

            [IntrinsicType("Reference")]
            public struct Reference<T>
            {
                Int64 wrongField;
            }

            [IntrinsicType("Pointer")]
            public struct Pointer<T>
            {
                NUInt address;
            }

            [IntrinsicType("Str")]
            public struct Str
            {
                UInt8[] data;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0020" && d.Message.Contains("Reference") && d.Message.Contains("must be of type 'NUInt'"));
    }

    [Fact]
    public void AbiMismatch_StrWrongFirstField_EmitsMH0020()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute IntrinsicType(&Str name);
            [IntrinsicType("UInt8")] public struct UInt8;
            [IntrinsicType("Int64")] public struct Int64;
            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;

            [IntrinsicType("Str")]
            public struct Str
            {
                Int64 wrongField;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0020");
        Assert.Contains("UInt8[]", diag.Message);
    }

    [Fact]
    public void Str_ValidContract_WithByteSlice_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude);

        Assert.False(compilation.HasErrors);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Str));
    }

    [Fact]
    public void AbiMismatch_StrTwoFields_EmitsMH0020()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute IntrinsicType(&Str name);
            [IntrinsicType("UInt8")] public struct UInt8;
            [IntrinsicType("NUInt")] public struct NUInt;
            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;

            [IntrinsicType("Str")]
            public struct Str
            {
                UInt8[] data;
                NUInt extraLength;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0020");
        Assert.Contains("exactly 1 instance field", diag.Message);
    }

    [Fact]
    public void AbiMismatch_SliceWithFields_EmitsMH0020()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute IntrinsicType(&Str name);
            [IntrinsicType("Int64")] public struct Int64;
            [IntrinsicType("UInt8")] public struct UInt8;

            [IntrinsicType("Slice")]
            public struct Slice<T, D : int = 1>
            {
                Int64 data;
            }

            [IntrinsicType("Str")]
            public struct Str
            {
                UInt8[] data;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0020" && d.Message.Contains("type 'Slice'"));
        Assert.Contains("zero instance fields", diag.Message);
    }

    [Fact]
    public void Reference_ValidABI_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude);

        Assert.False(compilation.HasErrors);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Reference));
    }

    [Fact]
    public void Array_ValidContract_ZeroFields_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Array")]
            public struct Array<T, N : int>;
            """);

        Assert.False(compilation.HasErrors);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Array));
    }

    [Fact]
    public void Array_ValidContract_Multidimensional_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Array")]
            public struct Array<T, N : int, D : int = 1>;
            """);

        Assert.False(compilation.HasErrors);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Array));
    }

    [Fact]
    public void Array_DuplicateIntrinsic_ReportsMH0543()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Array")]
            public struct Array1D<T, N : int>;

            [IntrinsicType("Array")]
            public struct ArrayND<T, N : int, D : int>;
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0543");
    }

    [Fact]
    public void UseSite_PointerWithoutPointerIntrinsic_EmitsMH0021()
    {
        var compilation = Compilation.FromSource("""
            public struct Dummy;
            public struct Foo
            {
                Dummy* ptr;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0021");
        Assert.Contains("Pointer", diag.Message);
    }

    [Fact]
    public void UseSite_ReferenceWithoutReferenceIntrinsic_EmitsMH0021()
    {
        var compilation = Compilation.FromSource("""
            public struct Dummy;
            public struct Foo
            {
                Dummy& r;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0021");
        Assert.Contains("Reference", diag.Message);
    }

    [Fact]
    public void UseSite_SliceWithoutSliceIntrinsic_EmitsMH0021()
    {
        var compilation = Compilation.FromSource("""
            public struct Dummy;
            public struct Foo
            {
                Dummy[] arr;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0021");
        Assert.Contains("Slice", diag.Message);
    }

    [Fact]
    public void UseSite_ArrayWithoutArrayIntrinsic_EmitsMH0021()
    {
        var compilation = Compilation.FromSource("""
            public struct Dummy;
            public struct Foo
            {
                Dummy[5] arr;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0021");
        Assert.Contains("Array", diag.Message);
    }

    [Fact]
    public void UseSite_SliceSpanAliasWithIntrinsic_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Int32")]
            public struct Int32;

            public struct Container
            {
                Int32[]& sliceRef;
            }
            """);

        Assert.False(compilation.HasErrors);
    }

}
