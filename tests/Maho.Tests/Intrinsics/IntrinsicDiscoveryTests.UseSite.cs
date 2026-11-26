using Maho.Resolution;

namespace Maho.Tests;

public sealed partial class IntrinsicDiscoveryTests
{
    [Fact]
    public void UseSite_InferredCollection_WithoutArray_EmitsMH0021()
    {
        var compilation = Compilation.FromSource(UseSitePrelude + """
            public struct Host
            {
                public func Test() -> ()
                {
                    var list = [1, 2, 3];
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0021");
        Assert.Contains("Array", diag.Message);
    }

    [Fact]
    public void UseSite_TypedCollection_DoesNotRequireArray()
    {
        var compilation = Compilation.FromSource(UseSitePrelude + """
            public struct Host
            {
                public struct CustomList;
                public func Test() -> ()
                {
                    CustomList list = [1, 2, 3];
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void UseSite_UnsuffixedIntegerLiteral_WithoutInt32_EmitsMH0021()
    {
        var compilation = Compilation.FromSource(UseSitePrelude + """
            public struct Host
            {
                public func Test() -> ()
                {
                    var x = 42;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0021");
        Assert.Contains("Int32", diag.Message);
    }

    [Fact]
    public void UseSite_SuffixedIntegerLiteral_DoesNotRequireInt32()
    {
        var compilation = Compilation.FromSource(UseSitePrelude + """
            public struct Host
            {
                public func Test() -> ()
                {
                    var x = 42i64;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void UseSite_UnsuffixedFloatLiteral_WithoutFloat64_EmitsMH0021()
    {
        var compilation = Compilation.FromSource(UseSitePrelude + """
            public struct Host
            {
                public func Test() -> ()
                {
                    var x = 3.14;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0021");
        Assert.Contains("Float64", diag.Message);
    }

    [Fact]
    public void UseSite_UnsuffixedStringLiteral_WithoutStr_EmitsMH0021()
    {
        var lines = UseSitePrelude.Split(["\r\n", "\n"], StringSplitOptions.None);
        var preludeWithoutStr = string.Join("\n", lines.Where(l => !l.Contains("\"Str\"") && !l.Contains("struct Str") && !l.Contains("UInt8[] data") && !l.Contains("uint8[] data")));

        var compilation = Compilation.FromSource(preludeWithoutStr + """
            public struct Host
            {
                public func Test() -> ()
                {
                    var x = "hello";
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0021" && d.Message.Contains("Str"));
    }

    [Fact]
    public void UseSite_UnsuffixedCharLiteral_WithoutChar_EmitsMH0021()
    {
        var compilation = Compilation.FromSource(UseSitePrelude + """
            public struct Host
            {
                public func Test() -> ()
                {
                    var x = 'c';
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0021");
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
                public func Create() -> Int32 { return Zero; }
                public func Abs() -> Int32 { return Zero; }
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
                [IntrinsicType("Layout")] public interface Layout;
                [IntrinsicType("Sized")] public interface Sized : Layout;
                [IntrinsicType("Unsized")] public interface Unsized : Layout;

                intrinsic attribute IntrinsicType(&MyStr name);

                [IntrinsicType("ValueType")] public struct ValType;
                [IntrinsicType("Struct")] public struct Struct : ValType;

                [IntrinsicType("UInt8")] public struct UInt8;
                [IntrinsicType("Int8")] public struct Int8;
                [IntrinsicType("Int64")] public struct Int64;
                [IntrinsicType("NUInt")] public struct NUInt;

                using usize = NUInt;

                [IntrinsicType("Slice")]
                public struct Slice<T, D : int = 1>;

                [IntrinsicType("Metadata")]
                public struct Metadata<T> where T : Layout;

                [IntrinsicType("Reference")]
                public struct Reference<T> where T : Layout
                {
                    usize addr;
                    Metadata<T> metadata;
                }

                [IntrinsicType("Pointer")]
                public struct Pointer<T> where T : Layout
                {
                    usize addr;
                    Metadata<T> metadata;
                }

                [IntrinsicType("Str")]
                public struct MyStr
                {
                    UInt8[] data;
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
        var libCompilation = Compilation.FromSource(UseSitePrelude + """
            [IntrinsicType("Int32")] public struct Int32;
            [IntrinsicType("Array")] public struct Array<T, N : int, D : int = 1>;
            """, "Lib.mh");

        Assert.False(libCompilation.HasErrors);

        var consumerCompilation = Compilation.FromSource("""
            public struct Test
            {
                public Int32* p;
                public Int32& r;
                public Int32[5] fa;
                public Int32[] a;
            }

            public struct Host
            {
                public func Run() -> ()
                {
                    var x = 42;
                    var s = "hello";
                    var arr = [1, 2, 3];
                }
            }
            """, "Consumer.mh", referencedCompilations: [libCompilation]);

        Assert.False(consumerCompilation.HasErrors, string.Join("\n", consumerCompilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void BoxIntrinsic_ValidArity_RegistersSuccessfully()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Box")]
            public struct Box<T>;
            """);

        Assert.False(compilation.HasErrors);
        Assert.True(compilation.Context.DiscoveredIntrinsics.TryGet("Box", out var boxHandle));
    }

    [Fact]
    public void BoxIntrinsic_InvalidArity_ReportsAbiMismatch()
    {
        var twoArity = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Box")]
            public struct Box<T, A>;
            """);

        Assert.True(twoArity.HasErrors);
        Assert.Contains(twoArity.Diagnostics, d => d.Code == "MH0020");

        var threeArity = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Box")]
            public struct Box<T, A, Extra>;
            """);

        Assert.True(threeArity.HasErrors);
        Assert.Contains(threeArity.Diagnostics, d => d.Code == "MH0020");
    }

    [Fact]
    public void IntrinsicProperty_ValidAndDiagnostics()
    {
        var valid = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func ReadLen(&int32[] s) -> nuint
            {
                return s.Length;
            }
            """);

        Assert.False(valid.HasErrors, string.Join("\n", valid.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var lenProp = Assert.Single(valid.Context.PropertySymbols, p => p.Name.Text == "Length");
        Assert.True(lenProp.Flags.HasFlag(FunctionFlags.Intrinsic));
        Assert.Equal("Slice.Length", lenProp.IntrinsicSignature);
        Assert.False(lenProp.HasBacking);

        var outsideIntrinsic = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            intrinsic attribute IntrinsicProperty(&Str signature);

            public struct NonIntrinsic
            {
                [IntrinsicProperty("Foo.Bar")]
                public NUInt Bar { get; }
            }
            """);
        Assert.True(outsideIntrinsic.HasErrors);
        Assert.Contains(outsideIntrinsic.Diagnostics, d => d.Code == "MH0541" && d.Message.Contains("must be declared in an intrinsic type"));

        var wrongAttrParamType = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            intrinsic attribute IntrinsicProperty(Int64 wrong);
            """);
        Assert.True(wrongAttrParamType.HasErrors);
        Assert.Contains(wrongAttrParamType.Diagnostics, d => d.Code == "MH0541" && d.Message.Contains("must be '&Str'"));

        var nonStringArg = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            intrinsic attribute IntrinsicProperty(&Str signature);

            [IntrinsicType("Slice")]
            public struct MySlice<T, D : int = 1>
            {
                [IntrinsicProperty(123)]
                public NUInt Length { get; }
            }
            """);
        Assert.True(nonStringArg.HasErrors);
        Assert.Contains(nonStringArg.Diagnostics, d => d.Code == "MH0541" && d.Message.Contains("string literal"));
    }

    [Fact]
    public void OperatorFunction_ValidSignatures_RegisterSuccessfully()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(&Str fullName);

            public interface Add<TLhs, TRhs, TResult>
            {
                [OperatorFunction("Addition")]
                func Add(TLhs left, TRhs right) -> TResult;
            }

            public interface Ctor<TArgs...>
            {
                [OperatorFunction("Constructor")]
                func Construct(&this, TArgs... args) -> ();
            }

            public interface Dtor
            {
                [OperatorFunction("Destructor")]
                func Destroy(mut &this) -> ();
            }

            public interface Index<TIndex, TResult>
            {
                [OperatorFunction("IndexerGetter")]
                func Get(&this, TIndex index) -> TResult;
            }

            public interface Deref<TResult>
            {
                [OperatorFunction("IndirectMemberAccess")]
                func Arrow(&this) -> TResult&;
            }

            public interface Assign<TRhs>
            {
                [OperatorFunction("Assignment")]
                func Assign(&this, TRhs value) -> ();
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
    public void OperatorFunction_BinaryWithThreeParameters_ReportsMH0560()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(&Str fullName);

            public interface BadAdd<TLhs, TRhs, TResult>
            {
                [OperatorFunction("Addition")]
                func Add(TLhs left, TRhs right, TRhs extra) -> TResult;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("must accept exactly 2 parameters"));
    }

    [Fact]
    public void OperatorFunction_IndirectMemberAccessReturningVoid_ReportsMH0560()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(&Str fullName);

            public interface BadDeref
            {
                [OperatorFunction("IndirectMemberAccess")]
                func Arrow(&this) -> ();
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("return type cannot be void"));
    }

    [Fact]
    public void OperatorFunction_ConstructorStatic_ReportsMH0560()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(&Str fullName);

            public interface BadCtor
            {
                [OperatorFunction("Constructor")]
                func Construct() -> ();
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("constructors cannot be declared static"));
    }

    [Fact]
    public void OperatorFunction_IndexerGetterWithZeroParams_ReportsMH0560()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(&Str fullName);

            public interface BadIndex<TResult>
            {
                [OperatorFunction("IndexerGetter")]
                func Get(&this) -> TResult;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("must accept at least 1 index parameter"));
    }

    [Fact]
    public void OperatorFunction_DeclaredInStruct_ReportsMH0560()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(&Str fullName);

            public struct MyStruct
            {
                [OperatorFunction("Addition")]
                public func Add(MyStruct a, MyStruct b) -> MyStruct => a;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("must be declared in an interface"));
    }

    [Fact]
    public void OperatorFunction_UnknownOperatorName_ReportsMH0560()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(&Str fullName);

            public interface BadOp
            {
                [OperatorFunction("NotARealOperator")]
                func DoSomething() -> ();
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("unknown operator function"));
    }

    [Fact]
    public void OperatorFunction_DuplicateAcrossInterfaces_ReportsMH0563()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            intrinsic attribute OperatorFunction(&Str name);

            public interface FirstCallable
            {
                [OperatorFunction("Callable")]
                func Invoke(&this) -> Int8;
            }

            public interface SecondCallable
            {
                [OperatorFunction("Callable")]
                func Call(&this) -> Int8;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0563");
        Assert.Contains("duplicate operator function declaration for operator 'Callable'", diag.Message);
    }

    [Fact]
    public void IntrinsicFunction_InIntrinsicType_TagsFunctionFlagsIntrinsic()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            intrinsic attribute IntrinsicFunction(&Str target);

            [IntrinsicType("Int32")]
            public struct Int32
            {
                [IntrinsicFunction("Int32.Add(Int32, Int32)")]
                public func Add(Int32 a, Int32 b) -> Int32;
            }
            """);

        Assert.False(compilation.HasErrors);
        var int32Sym = compilation.Context.TypeSymbols.First(t => t.Name.Text == "Int32");
        var addMethodHandle = ((ProductTypeSymbol)int32Sym).Methods[0];
        var addMethod = compilation.Context.GetMethodSymbol(addMethodHandle);
        Assert.NotNull(addMethod);
        Assert.True(addMethod.Flags.HasFlag(FunctionFlags.Intrinsic));
        Assert.Equal("Int32.Add(Int32, Int32)", addMethod.IntrinsicSignature);
    }

    [Fact]
    public void IntrinsicFunction_InNonIntrinsicType_ReportsMH0541()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            intrinsic attribute IntrinsicFunction(&Str target);

            public struct UserType
            {
                [IntrinsicFunction("foo")]
                public func Foo() -> ();
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0541" && d.Message.Contains("must be declared in an intrinsic type"));
    }

    [Fact]
    public void IntrinsicFunction_WrongParameterCount_ReportsMH0541()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            intrinsic attribute IntrinsicFunction;
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0541" && d.Message.Contains("must accept exactly one parameter"));
    }

    [Fact]
    public void IntrinsicFunction_ParameterTypeMismatch_ReportsMH0541()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Int32")] public struct Int32;
            intrinsic attribute IntrinsicFunction(Int32 target);
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0541" && d.Message.Contains("Str"));
    }

    [Fact]
    public void IntrinsicFunction_ApplicationWithoutArgument_ReportsMH0541()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            intrinsic attribute IntrinsicFunction(&Str target);

            [IntrinsicType("Int32")]
            public struct Int32
            {
                [IntrinsicFunction]
                public func Add(Int32 a, Int32 b) -> Int32;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0541" && d.Message.Contains("requires exactly one argument"));
    }

    [Fact]
    public void IntrinsicFunction_ApplicationNonStringLiteral_ReportsMH0541()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            intrinsic attribute IntrinsicFunction(&Str target);

            [IntrinsicType("Int32")]
            public struct Int32
            {
                [IntrinsicFunction(42)]
                public func Add(Int32 a, Int32 b) -> Int32;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0541" && d.Message.Contains("must be a string literal"));
    }

    [Fact]
    public void OperatorFunction_New_ValidAndInvalidSignatures()
    {
        var valid = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(&Str name);

            public interface New<TTarget, TReturn>
            {
                [OperatorFunction("New")]
                func New() -> TReturn;
            }
            """);
        Assert.False(valid.HasErrors, string.Join("\n", valid.Diagnostics.Select(d => d.Message)));
        Assert.True(valid.Context.DiscoveredOperators.TryGetOperators(OperatorKind.New, out var newOps));
        Assert.Single(newOps);

        var nonStatic = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(&Str name);

            public interface BadNew<TTarget, TReturn>
            {
                [OperatorFunction("New")]
                func New(&this) -> TReturn;
            }
            """);
        Assert.True(nonStatic.HasErrors);
        Assert.Contains(nonStatic.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("must be declared static"));

        var voidReturn = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(&Str name);

            public interface BadNew<TTarget>
            {
                [OperatorFunction("New")]
                func New() -> ();
            }
            """);
        Assert.True(voidReturn.HasErrors);
        Assert.Contains(voidReturn.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("return type cannot be void"));
    }

    [Fact]
    public void OperatorFunction_Put_ValidAndInvalidSignatures()
    {
        var valid = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            public attribute OperatorFunction(&Str name);

            public interface Put<TTarget>
            {
                [OperatorFunction("Put")]
                func Put() -> TTarget;
            }
            """);
        Assert.False(valid.HasErrors, string.Join("\n", valid.Diagnostics.Select(d => d.Message)));
        Assert.True(valid.Context.DiscoveredOperators.TryGetOperators(OperatorKind.Put, out var putOps));
        Assert.Single(putOps);

        var voidReturn = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            public attribute OperatorFunction(&Str name);

            public interface BadPut<TTarget>
            {
                [OperatorFunction("Put")]
                func Put() -> ();
            }
            """);
        Assert.True(voidReturn.HasErrors);
        Assert.Contains(voidReturn.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("return type cannot be void"));
    }

    [Fact]
    public void OperatorFunction_Copy_And_Move_ValidAndInvalidSignatures()
    {
        var valid = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            public attribute OperatorFunction(&Str name);

            public interface Copy<T>
            {
                [OperatorFunction("Copy")]
                func Copy(T& dest, T& src) -> ();
            }

            public interface Move<T>
            {
                [OperatorFunction("Move")]
                func Move(T& dest, T& src) -> ();
            }
            """);
        Assert.False(valid.HasErrors, string.Join("\n", valid.Diagnostics.Select(d => d.Message)));
        Assert.True(valid.Context.DiscoveredOperators.TryGetOperators(OperatorKind.Copy, out var copyOps));
        Assert.Single(copyOps);
        Assert.True(valid.Context.DiscoveredOperators.TryGetOperators(OperatorKind.Move, out var moveOps));
        Assert.Single(moveOps);

        var badParamCount = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            public attribute OperatorFunction(&Str name);

            public interface BadCopy<T>
            {
                [OperatorFunction("Copy")]
                func Copy(T& dest) -> ();
            }
            """);
        Assert.True(badParamCount.HasErrors);
        Assert.Contains(badParamCount.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("must accept exactly 2 parameters"));
    }

    [Fact]
    public void OperatorFunction_Callable_ValidSignature()
    {
        var valid = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(&Str name);

            public interface Callable<TReturn, TArg>
            {
                [OperatorFunction("Callable")]
                func Call(&this, TArg arg) -> TReturn;
            }
            """);
        Assert.False(valid.HasErrors, string.Join("\n", valid.Diagnostics.Select(d => d.Message)));
        Assert.True(valid.Context.DiscoveredOperators.TryGetOperators(OperatorKind.Callable, out var callOps));
        Assert.Single(callOps);
    }

    [Fact]
    public void Lifecycle_DestructorNandCopy_ReportsMH0561()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            public attribute OperatorFunction(&Str name);

            public interface Destructor
            {
                [OperatorFunction("Destructor")]
                func Drop(mut &this) -> ();
            }

            public interface Copy<T>
            {
                [OperatorFunction("Copy")]
                func Copy(T& dest, T& src) -> ();
            }

            public struct Resource : Destructor, Copy<Resource>
            {
                public func Drop(mut &this) -> () {}
                public func Copy(Resource& dest, Resource& src) -> () {}
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0561" && d.Message.Contains("cannot implement both 'Destructor' and 'Copy'"));
    }

    [Fact]
    public void Lifecycle_CopyNandMove_ReportsMH0562()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """

            public attribute OperatorFunction(&Str name);

            public interface Copy<T>
            {
                [OperatorFunction("Copy")]
                func Copy(T& dest, T& src) -> ();
            }

            public interface Move<T>
            {
                [OperatorFunction("Move")]
                func Move(T& dest, T& src) -> ();
            }

            public struct Conflicted : Copy<Conflicted>, Move<Conflicted>
            {
                public func Copy(Conflicted& dest, Conflicted& src) -> () {}
                public func Move(Conflicted& dest, Conflicted& src) -> () {}
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0562" && d.Message.Contains("cannot implement both 'Copy' and 'Move'"));
    }

    [Fact]
    public void Delegate_ValidABI_WithReference_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            intrinsic attribute IntrinsicFunction(&Str signature);
            intrinsic attribute OperatorFunction(&Str name);


            public interface Callable<TReturn, TParams...>
            {
                [OperatorFunction("Callable")]
                func Invoke(&this, TParams args) -> TReturn;
            }

            [IntrinsicType("Delegate")]
            public struct Delegate<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
            {
                NUInt codeAddress;
                ValType& env;
                [IntrinsicFunction("Delegate.Invoke(TParams...)")]
                func Callable.Invoke(&this, TParams args) -> TReturn;
            }
            """);

        Assert.False(compilation.HasErrors);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Delegate));
    }
}
