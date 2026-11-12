using Maho.Resolution;

namespace Maho.Tests;

public sealed class IntrinsicDiscoveryTests
{
    private const string IntrinsicBootstrapPrelude = """
        intrinsic attribute IntrinsicType(Str name);

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
            Int64 length;
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
    public void AbiMismatch_PrimitiveWithFields_EmitsMH0544()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Int32")]
            public struct Int32
            {
                Int64 unexpectedField;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0544");
        Assert.Contains("must have zero instance fields", diag.Message);
    }

    [Fact]
    public void AbiMismatch_PrimitiveWithGenericParams_EmitsMH0544()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Int32")]
            public struct Int32<T>;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0544");
        Assert.Contains("cannot have generic parameters", diag.Message);
    }

    [Fact]
    public void AbiMismatch_PointerWithoutNUIntField_EmitsMH0544()
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
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0544" && d.Message.Contains("must be of type 'NUInt'"));
    }

    [Fact]
    public void AbiMismatch_PointerWithoutNUIntDeclared_EmitsMH0544()
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
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0544" && d.Message.Contains("requires intrinsic 'NUInt'"));
    }

    [Fact]
    public void AbiMismatch_PointerWrongGenericCount_EmitsMH0544()
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
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0544" && d.Message.Contains("must have exactly 1 generic parameter"));
    }

    [Fact]
    public void AbiMismatch_PointerWrongFieldCount_EmitsMH0544()
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
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0544" && d.Message.Contains("must have exactly 1 instance field"));
    }

    [Fact]
    public void AbiMismatch_ReferenceRequiresNUIntField_EmitsMH0544()
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
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0544" && d.Message.Contains("Reference") && d.Message.Contains("must be of type 'NUInt'"));
    }

    [Fact]
    public void AbiMismatch_StrWrongFirstField_EmitsMH0544()
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
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0544");
        Assert.Contains("first field must be a reference type", diag.Message);
    }

    [Fact]
    public void AbiMismatch_StrWithRawPointer_EmitsMH0544()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute IntrinsicType(Str name);
            [IntrinsicType("Int8")] public struct Int8;
            [IntrinsicType("Int64")] public struct Int64;
            [IntrinsicType("NUInt")] public struct NUInt;
            [IntrinsicType("Reference")] public struct Reference<T> { NUInt address; }
            [IntrinsicType("Pointer")] public struct Pointer<T> { NUInt address; }

            [IntrinsicType("Str")]
            public struct Str
            {
                Int8* data;
                Int64 length;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0544");
        Assert.Contains("first field must be a reference type", diag.Message);
    }

    [Fact]
    public void AbiMismatch_StrWrongSecondField_EmitsMH0544()
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
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0544");
        Assert.Contains("second field must be of type 'Int64'", diag.Message);
    }

    [Fact]
    public void AbiMismatch_ArrayWrongFields_EmitsMH0544()
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
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0544");
        Assert.Contains("first field must be a pointer type", diag.Message);
    }

    [Fact]
    public void Array_ValidABI_WithPointerAndInt64_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Array")]
            public struct Array<T>
            {
                T* data;
                Int64 length;
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
                Int64 length;
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
    public void Span_ValidABI_WithReferenceAndInt64_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Span")]
            public struct Span<T>
            {
                T& data;
                Int64 length;
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
                Int64 length;
            }
            """);

        Assert.Empty(compilation.Diagnostics);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Span));
    }

    [Fact]
    public void AbiMismatch_SpanWrongFirstField_EmitsMH0544()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Span")]
            public struct Span<T>
            {
                T* wrongPtr;
                Int64 length;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0544");
        Assert.Contains("first field must be a reference type", diag.Message);
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
    public void UseSite_PointerWithoutPointerIntrinsic_EmitsMH0240()
    {
        var compilation = Compilation.FromSource("""
            public struct Foo
            {
                public struct void;
                void* ptr;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0240");
        Assert.Contains("Pointer", diag.Message);
    }

    [Fact]
    public void UseSite_ReferenceWithoutReferenceIntrinsic_EmitsMH0240()
    {
        var compilation = Compilation.FromSource("""
            public struct Foo
            {
                public struct void;
                void& r;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0240");
        Assert.Contains("Reference", diag.Message);
    }

    [Fact]
    public void UseSite_ArrayWithoutArrayIntrinsic_EmitsMH0240()
    {
        var compilation = Compilation.FromSource("""
            public struct Foo
            {
                public struct void;
                void[] arr;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0240");
        Assert.Contains("Array", diag.Message);
    }

    [Fact]
    public void UseSite_FixedArrayWithoutFixedArrayIntrinsic_EmitsMH0240()
    {
        var compilation = Compilation.FromSource("""
            public struct Foo
            {
                public struct void;
                void[5] arr;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0240");
        Assert.Contains("FixedArray", diag.Message);
    }

    [Fact]
    public void UseSite_SpanWithoutSpanIntrinsic_EmitsMH0240()
    {
        var compilation = Compilation.FromSource("""
            public struct Foo
            {
                public struct void;
                &void span;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0240");
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
                Int64 length;
            }

            public struct Container
            {
                &Int32 span;
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void UseSite_InferredCollection_WithoutArray_EmitsMH0240()
    {
        var compilation = Compilation.FromSource("""
            public class Host
            {
                public struct void;
                public void Test()
                {
                    var list = [1, 2, 3];
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0240");
        Assert.Contains("Array", diag.Message);
    }

    [Fact]
    public void UseSite_TypedCollection_DoesNotRequireArray()
    {
        var compilation = Compilation.FromSource("""
            public class Host
            {
                public struct void;
                public struct CustomList;
                public void Test()
                {
                    CustomList list = [1, 2, 3];
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void UseSite_UnsuffixedIntegerLiteral_WithoutInt32_EmitsMH0240()
    {
        var compilation = Compilation.FromSource("""
            public class Host
            {
                public struct void;
                public void Test()
                {
                    var x = 42;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0240");
        Assert.Contains("Int32", diag.Message);
    }

    [Fact]
    public void UseSite_SuffixedIntegerLiteral_DoesNotRequireInt32()
    {
        var compilation = Compilation.FromSource("""
            public class Host
            {
                public struct void;
                public void Test()
                {
                    var x = 42i64;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void UseSite_UnsuffixedFloatLiteral_WithoutFloat64_EmitsMH0240()
    {
        var compilation = Compilation.FromSource("""
            public class Host
            {
                public struct void;
                public void Test()
                {
                    var x = 3.14;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0240");
        Assert.Contains("Float64", diag.Message);
    }

    [Fact]
    public void UseSite_UnsuffixedStringLiteral_WithoutStr_EmitsMH0240()
    {
        var compilation = Compilation.FromSource("""
            public class Host
            {
                public struct void;
                public void Test()
                {
                    var x = "hello";
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0240");
        Assert.Contains("Str", diag.Message);
    }

    [Fact]
    public void UseSite_UnsuffixedCharLiteral_WithoutChar_EmitsMH0240()
    {
        var compilation = Compilation.FromSource("""
            public class Host
            {
                public struct void;
                public void Test()
                {
                    var x = 'c';
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0240");
        Assert.Contains("Char", diag.Message);
    }

    [Fact]
    public void InstanceMethodsAndStaticMembers_DoNotAffectAbiCheck()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            [IntrinsicType("Int32")]
            public struct Int32
            {
                public static Int32 Zero;
                public static Int32 Create() { return Zero; }
                public Int32 Abs() { return Zero; }
            }
            """);

        Assert.False(compilation.HasErrors);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Int32));
    }

    [Fact]
    public void SymbolicResolution_AliasesAndNamespaces_Succeeds()
    {
        var compilation = Compilation.FromSource("""
            namespace Std
            {
                intrinsic attribute IntrinsicType(MyStr name);

                [IntrinsicType("Int8")] public struct Int8;
                [IntrinsicType("Int64")] public struct Int64;
                [IntrinsicType("NUInt")] public struct NUInt;

                using usize = NUInt;

                [IntrinsicType("Reference")]
                public struct Reference<T>
                {
                    usize addr;
                }

                [IntrinsicType("Pointer")]
                public struct Pointer<T>
                {
                    usize addr;
                }

                [IntrinsicType("Str")]
                public struct MyStr
                {
                    Int8& data;
                    Int64 len;
                }
            }
            """);

        Assert.Empty(compilation.Diagnostics);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Pointer));
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Reference));
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Str));
    }

    [Fact]
    public void ReferencedProject_DeclaresIntrinsics_ConsumerUsesThem_Succeeds()
    {
        var libCompilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Int32")] public struct Int32;
            [IntrinsicType("Array")] public struct Array<T> { T* data; Int64 len; }
            [IntrinsicType("Span")] public struct Span<T> { T& data; Int64 len; }
            [IntrinsicType("FixedArray")] public struct FixedArray<T, N>;
            """, "Lib.mh");

        Assert.False(libCompilation.HasErrors);

        var consumerCompilation = Compilation.FromSource("""
            public struct Test
            {
                public Int32* p;
                public Int32& r;
                public Int32[] a;
                public &Int32 s;
                public Int32[5] fa;
            }

            public class Host
            {
                public struct void;
                public void Run()
                {
                    var x = 42;
                    var s = "hello";
                    var arr = [1, 2, 3];
                }
            }
            """, "Consumer.mh", referencedCompilations: [libCompilation]);

        Assert.False(consumerCompilation.HasErrors);
    }

    [Fact]
    public void VoidIntrinsic_ZeroField_RegistersSuccessfully()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Void")]
            public struct Void;
            """);

        Assert.False(compilation.HasErrors);
        Assert.True(compilation.Context.DiscoveredIntrinsics.TryGet("Void", out var voidHandle));
    }

    [Fact]
    public void VoidIntrinsic_WithInstanceFields_ReportsAbiMismatch()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Void")]
            public struct Void
            {
                Int8 x;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0544");
    }

    [Fact]
    public void OperatorFunction_ValidSignatures_RegisterSuccessfully()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public struct void;
            public attribute OperatorFunction(Str fullName);

            public interface Add<TLhs, TRhs, TResult>
            {
                [OperatorFunction("Addition")]
                public static TResult Add(TLhs left, TRhs right);
            }

            public interface Ctor<TArgs...>
            {
                [OperatorFunction("Constructor")]
                public void Construct(TArgs... args);
            }

            public interface Dtor
            {
                [OperatorFunction("Destructor")]
                public void Destroy();
            }

            public interface Index<TIndex, TResult>
            {
                [OperatorFunction("IndexerGetter")]
                public TResult Get(TIndex index);
            }

            public interface Deref<TResult>
            {
                [OperatorFunction("IndirectMemberAccess")]
                public TResult& Arrow();
            }

            public interface Assign<TRhs>
            {
                [OperatorFunction("Assignment")]
                public void Assign(TRhs value);
            }
            """);

        Assert.False(compilation.HasErrors);
        Assert.True(compilation.Context.DiscoveredOperators.TryGetOperators("Addition", out var addOps));
        Assert.Single(addOps);
        Assert.True(compilation.Context.DiscoveredOperators.TryGetOperators("Constructor", out var ctorOps));
        Assert.Single(ctorOps);
        Assert.True(compilation.Context.DiscoveredOperators.TryGetOperators("Destructor", out var dtorOps));
        Assert.Single(dtorOps);
        Assert.True(compilation.Context.DiscoveredOperators.TryGetOperators("IndexerGetter", out var indexOps));
        Assert.Single(indexOps);
        Assert.True(compilation.Context.DiscoveredOperators.TryGetOperators("IndirectMemberAccess", out var arrowOps));
        Assert.Single(arrowOps);
        Assert.True(compilation.Context.DiscoveredOperators.TryGetOperators("Assignment", out var assignOps));
        Assert.Single(assignOps);
    }

    [Fact]
    public void OperatorFunction_BinaryWithThreeParameters_ReportsMH0242()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(Str fullName);

            public interface BadAdd<TLhs, TRhs, TResult>
            {
                [OperatorFunction("Addition")]
                public static TResult Add(TLhs left, TRhs right, TRhs extra);
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0242" && d.Message.Contains("must accept exactly 2 parameters"));
    }

    [Fact]
    public void OperatorFunction_IndirectMemberAccessReturningNonReference_ReportsMH0242()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(Str fullName);

            public interface BadDeref<TResult>
            {
                [OperatorFunction("IndirectMemberAccess")]
                public TResult Arrow();
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0242" && d.Message.Contains("return type must be a reference (&)"));
    }

    [Fact]
    public void OperatorFunction_ConstructorStatic_ReportsMH0242()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(Str fullName);

            public interface BadCtor
            {
                [OperatorFunction("Constructor")]
                public static void Construct();
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0242" && d.Message.Contains("constructors cannot be declared static"));
    }

    [Fact]
    public void OperatorFunction_IndexerGetterWithZeroParams_ReportsMH0242()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(Str fullName);

            public interface BadIndex<TResult>
            {
                [OperatorFunction("IndexerGetter")]
                public TResult Get();
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0242" && d.Message.Contains("must accept at least 1 index parameter"));
    }

    [Fact]
    public void OperatorFunction_DeclaredInStruct_ReportsMH0242()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(Str fullName);

            public struct MyStruct
            {
                [OperatorFunction("Addition")]
                public static MyStruct Add(MyStruct a, MyStruct b) => a;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0242" && d.Message.Contains("must be declared in an interface"));
    }

    [Fact]
    public void OperatorFunction_UnknownOperatorName_ReportsMH0242()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(Str fullName);

            public interface BadOp
            {
                [OperatorFunction("NotARealOperator")]
                public void DoSomething();
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0242" && d.Message.Contains("unknown operator function"));
    }

    [Fact]
    public void IntrinsicFunction_InIntrinsicType_TagsFunctionFlagsIntrinsic()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute IntrinsicFunction(Str target = "");

            [IntrinsicType("Int32")]
            public struct Int32
            {
                [IntrinsicFunction("i32.add")]
                public static Int32 Add(Int32 a, Int32 b);
            }
            """);

        Assert.False(compilation.HasErrors);
        var int32Sym = compilation.Context.TypeSymbols.First(t => t.Name.Text == "Int32");
        var addMethodHandle = ((ProductTypeSymbol)int32Sym).Methods[0];
        var addMethod = compilation.Context.GetMethodSymbol(addMethodHandle);
        Assert.NotNull(addMethod);
        Assert.True(addMethod.Flags.HasFlag(FunctionFlags.Intrinsic));
    }

    [Fact]
    public void IntrinsicFunction_InNonIntrinsicType_ReportsMH0541()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute IntrinsicFunction(Str target = "");

            public struct UserType
            {
                [IntrinsicFunction("foo")]
                public static void Foo();
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0541" && d.Message.Contains("must be declared in an intrinsic type"));
    }

    [Fact]
    public void OperatorFunction_New_ValidAndInvalidSignatures()
    {
        var valid = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(Str name);

            public interface New<TTarget, TReturn>
            {
                [OperatorFunction("New")]
                public static TReturn New();
            }
            """);
        Assert.False(valid.HasErrors, string.Join("\n", valid.Diagnostics.Select(d => d.Message)));
        Assert.True(valid.Context.DiscoveredOperators.TryGetOperators(OperatorKind.New, out var newOps));
        Assert.Single(newOps);

        var nonStatic = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(Str name);

            public interface BadNew<TTarget, TReturn>
            {
                [OperatorFunction("New")]
                public TReturn New();
            }
            """);
        Assert.True(nonStatic.HasErrors);
        Assert.Contains(nonStatic.Diagnostics, d => d.Code == "MH0242" && d.Message.Contains("must be declared static"));

        var voidReturn = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(Str name);

            public interface BadNew<TTarget>
            {
                [OperatorFunction("New")]
                public static void New();
            }
            """);
        Assert.True(voidReturn.HasErrors);
        Assert.Contains(voidReturn.Diagnostics, d => d.Code == "MH0242" && d.Message.Contains("return type cannot be void"));
    }

    [Fact]
    public void OperatorFunction_Put_ValidAndInvalidSignatures()
    {
        var valid = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Void")]
            public struct void;

            public attribute OperatorFunction(Str name);

            public interface Put<TTarget>
            {
                [OperatorFunction("Put")]
                public static void Put(TTarget& destination);
            }
            """);
        Assert.False(valid.HasErrors, string.Join("\n", valid.Diagnostics.Select(d => d.Message)));
        Assert.True(valid.Context.DiscoveredOperators.TryGetOperators(OperatorKind.Put, out var putOps));
        Assert.Single(putOps);

        var nonVoid = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Void")]
            public struct void;

            public attribute OperatorFunction(Str name);

            public interface BadPut<TTarget>
            {
                [OperatorFunction("Put")]
                public static Int64 Put(TTarget& destination);
            }
            """);
        Assert.True(nonVoid.HasErrors);
        Assert.Contains(nonVoid.Diagnostics, d => d.Code == "MH0242" && d.Message.Contains("return type must be void"));
    }

    [Fact]
    public void OperatorFunction_Copy_And_Move_ValidAndInvalidSignatures()
    {
        var valid = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Void")]
            public struct void;

            public attribute OperatorFunction(Str name);

            public interface Copy<T>
            {
                [OperatorFunction("Copy")]
                public static void Copy(T& dest, T& src);
            }

            public interface Move<T>
            {
                [OperatorFunction("Move")]
                public static void Move(T& dest, T& src);
            }
            """);
        Assert.False(valid.HasErrors, string.Join("\n", valid.Diagnostics.Select(d => d.Message)));
        Assert.True(valid.Context.DiscoveredOperators.TryGetOperators(OperatorKind.Copy, out var copyOps));
        Assert.Single(copyOps);
        Assert.True(valid.Context.DiscoveredOperators.TryGetOperators(OperatorKind.Move, out var moveOps));
        Assert.Single(moveOps);

        var badParamCount = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Void")]
            public struct void;

            public attribute OperatorFunction(Str name);

            public interface BadCopy<T>
            {
                [OperatorFunction("Copy")]
                public static void Copy(T& dest);
            }
            """);
        Assert.True(badParamCount.HasErrors);
        Assert.Contains(badParamCount.Diagnostics, d => d.Code == "MH0242" && d.Message.Contains("must accept exactly 2 parameters"));
    }

    [Fact]
    public void OperatorFunction_Callable_ValidSignature()
    {
        var valid = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(Str name);

            public interface Callable<TReturn, TArg>
            {
                [OperatorFunction("Callable")]
                public TReturn Call(TArg arg);
            }
            """);
        Assert.False(valid.HasErrors, string.Join("\n", valid.Diagnostics.Select(d => d.Message)));
        Assert.True(valid.Context.DiscoveredOperators.TryGetOperators(OperatorKind.Callable, out var callOps));
        Assert.Single(callOps);
    }

    [Fact]
    public void Lifecycle_DestructorNandCopy_ReportsMH0243()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Void")]
            public struct void;

            public attribute OperatorFunction(Str name);

            public interface Destructor
            {
                [OperatorFunction("Destructor")]
                public void Drop();
            }

            public interface Copy<T>
            {
                [OperatorFunction("Copy")]
                public static void Copy(T& dest, T& src);
            }

            public struct Resource : Destructor, Copy<Resource>
            {
                public void Drop() {}
                public static void Copy(Resource& dest, Resource& src) {}
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0243" && d.Message.Contains("cannot implement both 'Destructor' and 'Copy'"));
    }

    [Fact]
    public void Lifecycle_CopyNandMove_ReportsMH0244()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Void")]
            public struct void;

            public attribute OperatorFunction(Str name);

            public interface Copy<T>
            {
                [OperatorFunction("Copy")]
                public static void Copy(T& dest, T& src);
            }

            public interface Move<T>
            {
                [OperatorFunction("Move")]
                public static void Move(T& dest, T& src);
            }

            public struct Conflicted : Copy<Conflicted>, Move<Conflicted>
            {
                public static void Copy(Conflicted& dest, Conflicted& src) {}
                public static void Move(Conflicted& dest, Conflicted& src) {}
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0244" && d.Message.Contains("cannot implement both 'Copy' and 'Move'"));
    }
}
