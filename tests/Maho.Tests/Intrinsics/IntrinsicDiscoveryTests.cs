using Maho.Resolution;

namespace Maho.Tests;

public sealed partial class IntrinsicDiscoveryTests
{
    private const string IntrinsicBootstrapPrelude = """
        intrinsic attribute IntrinsicType(Str name);

        [IntrinsicType("ValueType")]
        public struct ValType;

        [IntrinsicType("Struct")]
        public struct Struct : ValType;

        [IntrinsicType("Int8")]
        public struct Int8;

        [IntrinsicType("Int64")]
        public struct Int64;

        [IntrinsicType("NUInt")]
        public struct NUInt;

        [IntrinsicType("Reference")]
        public struct Reference<T>
        {
            NUInt address;
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T>
        {
            NUInt address;
        }

        [IntrinsicType("Str")]
        public struct Str
        {
            Int8& data;
            NUInt length;
        }
        """;

    private const string UseSitePrelude = IntrinsicBootstrapPrelude + """
        intrinsic attribute IntrinsicFunction(Str signature);

        intrinsic attribute OperatorFunction(Str name);


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

            [IntrinsicType("Int8")] public struct Int8;
            [IntrinsicType("Int64")] public struct Int64;
            [IntrinsicType("NUInt")] public struct NUInt;
            [IntrinsicType("Reference")] public struct Reference<T> { NUInt addr; }
            [IntrinsicType("Pointer")] public struct Pointer<T> { NUInt addr; }
            [IntrinsicType("Int32")] public struct Int32;
            [IntrinsicType("Str")] public struct Str { Int8& ptr; Int64 len; }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0541");
        Assert.Contains("must match the primitive 'Str' type", diag.Message);
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
            intrinsic attribute IntrinsicType(Str name);
            [IntrinsicType("Int8")] public struct Int8;
            [IntrinsicType("Int64")] public struct Int64;
            [IntrinsicType("NUInt")] public struct NUInt;
            [IntrinsicType("Reference")] public struct Reference<T> { NUInt address; }

            [IntrinsicType("Pointer")]
            public struct Pointer<T>
            {
                Int64 address;
            }

            [IntrinsicType("Str")]
            public struct Str
            {
                Int8& ptr;
                Int64 len;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0020" && d.Message.Contains("must be of type 'NUInt'"));
    }

    [Fact]
    public void AbiMismatch_PointerWithoutNUIntDeclared_EmitsMH0020()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute IntrinsicType(Str name);
            [IntrinsicType("Int8")] public struct Int8;
            [IntrinsicType("Int64")] public struct Int64;

            [IntrinsicType("Pointer")]
            public struct Pointer<T>
            {
                Int64 address;
            }

            [IntrinsicType("Str")]
            public struct Str
            {
                Int8& ptr;
                Int64 len;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0020" && d.Message.Contains("requires intrinsic 'NUInt'"));
    }

    [Fact]
    public void AbiMismatch_PointerWrongGenericCount_EmitsMH0020()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute IntrinsicType(Str name);
            [IntrinsicType("Int8")] public struct Int8;
            [IntrinsicType("Int64")] public struct Int64;
            [IntrinsicType("NUInt")] public struct NUInt;
            [IntrinsicType("Reference")] public struct Reference<T> { NUInt address; }

            [IntrinsicType("Pointer")]
            public struct Pointer
            {
                NUInt address;
            }

            [IntrinsicType("Str")]
            public struct Str
            {
                Int8& ptr;
                Int64 len;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0020" && d.Message.Contains("must have exactly 1 generic parameter"));
    }

    [Fact]
    public void AbiMismatch_PointerWrongFieldCount_EmitsMH0020()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute IntrinsicType(Str name);
            [IntrinsicType("Int8")] public struct Int8;
            [IntrinsicType("Int64")] public struct Int64;
            [IntrinsicType("NUInt")] public struct NUInt;
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
                Int8& ptr;
                Int64 len;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0020" && d.Message.Contains("must have exactly 1 instance field"));
    }

    [Fact]
    public void AbiMismatch_ReferenceRequiresNUIntField_EmitsMH0020()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute IntrinsicType(Str name);
            [IntrinsicType("Int8")] public struct Int8;
            [IntrinsicType("Int64")] public struct Int64;
            [IntrinsicType("NUInt")] public struct NUInt;

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
                Int8& ptr;
                Int64 len;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0020" && d.Message.Contains("Reference") && d.Message.Contains("must be of type 'NUInt'"));
    }

    [Fact]
    public void AbiMismatch_StrWrongFirstField_EmitsMH0020()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute IntrinsicType(Str name);
            [IntrinsicType("Int8")] public struct Int8;
            [IntrinsicType("Int64")] public struct Int64;
            [IntrinsicType("NUInt")] public struct NUInt;
            [IntrinsicType("Reference")] public struct Reference<T> { NUInt address; }

            [IntrinsicType("Str")]
            public struct Str
            {
                Int64 wrongPtr;
                Int64 length;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0020");
        Assert.Contains("first field must be a pointer or reference type", diag.Message);
    }

    [Fact]
    public void Str_ValidABI_WithRawPointer_Succeeds()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute IntrinsicType(Str name);
            [IntrinsicType("ValueType")] public struct ValType;
            [IntrinsicType("Struct")] public struct Struct : ValType;
            [IntrinsicType("Int8")] public struct Int8;
            [IntrinsicType("NUInt")] public struct NUInt;
            [IntrinsicType("Pointer")] public struct Pointer<T> { NUInt address; }

            [IntrinsicType("Str")]
            public struct Str
            {
                Int8* data;
                NUInt length;
            }
            """);

        Assert.False(compilation.HasErrors);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Str));
    }

    [Fact]
    public void AbiMismatch_StrWrongSecondField_EmitsMH0020()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute IntrinsicType(Str name);
            [IntrinsicType("Int8")] public struct Int8;
            [IntrinsicType("Int64")] public struct Int64;
            [IntrinsicType("NUInt")] public struct NUInt;
            [IntrinsicType("Reference")] public struct Reference<T> { NUInt address; }

            [IntrinsicType("Str")]
            public struct Str
            {
                Int8& data;
                Int8 wrongSecond;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0020");
        Assert.Contains("second field must be of type 'NUInt'", diag.Message);
    }

    [Fact]
    public void AbiMismatch_ArrayWrongFields_EmitsMH0020()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Array")]
            public struct Array<T>
            {
                Int64 data;
                Int64 length;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0020");
        Assert.Contains("first field must be a pointer or reference type", diag.Message);
    }

    [Fact]
    public void Array_ValidABI_WithReference_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Array")]
            public struct Array<T>
            {
                T& data;
                NUInt length;
            }
            """);

        Assert.Empty(compilation.Diagnostics);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Array));
    }

    [Fact]
    public void Array_ValidABI_WithPointerAndNUInt_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Array")]
            public struct Array<T>
            {
                T* data;
                NUInt length;
            }
            """);

        Assert.Empty(compilation.Diagnostics);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Array));
    }

    [Fact]
    public void Array_ValidABI_WithPointerGenericSyntax_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Array")]
            public struct Array<T>
            {
                Pointer<T> data;
                NUInt length;
            }
            """);

        Assert.Empty(compilation.Diagnostics);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Array));
    }

    [Fact]
    public void Reference_ValidABI_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude);

        Assert.False(compilation.HasErrors);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Reference));
    }

    [Fact]
    public void Span_ValidABI_WithReferenceAndNUInt_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Span")]
            public struct Span<T>
            {
                T& data;
                NUInt length;
            }
            """);

        Assert.Empty(compilation.Diagnostics);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Span));
    }

    [Fact]
    public void Span_ValidABI_WithReferenceGenericSyntax_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Span")]
            public struct Span<T>
            {
                Reference<T> data;
                NUInt length;
            }
            """);

        Assert.Empty(compilation.Diagnostics);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Span));
    }

    [Fact]
    public void Span_ValidABI_WithRawPointer_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Span")]
            public struct Span<T>
            {
                T* data;
                NUInt length;
            }
            """);

        Assert.False(compilation.HasErrors);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Span));
    }

    [Fact]
    public void AbiMismatch_SpanWrongFirstField_EmitsMH0020()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Span")]
            public struct Span<T>
            {
                Int64 wrongField;
                NUInt length;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0020");
        Assert.Contains("first field must be a reference or pointer type", diag.Message);
    }

    [Fact]
    public void FixedArray_ValidABI_ZeroFields_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("FixedArray")]
            public struct FixedArray<T, N>;
            """);

        Assert.False(compilation.HasErrors);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.FixedArray));
    }

    [Fact]
    public void FixedArray_ValidABI_MultidimensionalContract_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("FixedArray")]
            public struct FixedArray<T, N, D>;
            """);

        Assert.False(compilation.HasErrors);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.FixedArray));
    }

    [Fact]
    public void FixedArray_DuplicateIntrinsic_ReportsMH0543()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("FixedArray")]
            public struct FixedArray1D<T, N>;

            [IntrinsicType("FixedArray")]
            public struct FixedArrayND<T, N, D>;
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
    public void UseSite_ArrayWithoutArrayIntrinsic_EmitsMH0021()
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
        Assert.Contains("Array", diag.Message);
    }

    [Fact]
    public void UseSite_FixedArrayWithoutFixedArrayIntrinsic_EmitsMH0021()
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
        Assert.Contains("FixedArray", diag.Message);
    }

    [Fact]
    public void UseSite_SpanWithoutSpanIntrinsic_EmitsMH0021()
    {
        var compilation = Compilation.FromSource("""
            public struct Dummy;
            public struct Foo
            {
                Dummy[&] span;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0021");
        Assert.Contains("Span", diag.Message);
    }

    [Fact]
    public void UseSite_SpanWithSpanIntrinsic_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Int32")]
            public struct Int32;

            [IntrinsicType("Span")]
            public struct Span<T>
            {
                T& data;
                NUInt length;
            }

            public struct Container
            {
                Int32[&] span;
            }
            """);

        Assert.False(compilation.HasErrors);
    }

}
