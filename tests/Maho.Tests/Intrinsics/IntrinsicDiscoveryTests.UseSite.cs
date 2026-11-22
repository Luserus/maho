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
                public func Test() -> void
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
                public func Test() -> void
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
                public func Test() -> void
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
                public func Test() -> void
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
                public func Test() -> void
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
        var preludeWithoutStr = string.Join("\n", lines.Where(l => !l.Contains("\"Str\"") && !l.Contains("struct Str") && !l.Contains("Int8& data") && !l.Contains("Int64 length")));

        var compilation = Compilation.FromSource(preludeWithoutStr + """
            public struct Host
            {
                public func Test() -> void
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
                public func Test() -> void
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
                intrinsic attribute IntrinsicType(MyStr name);

                [IntrinsicType("ValueType")] public struct ValType;
                [IntrinsicType("Struct")] public struct Struct : ValType;

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
                    usize len;
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
            [IntrinsicType("Array")] public struct Array<T> { T* data; NUInt len; }
            [IntrinsicType("Span")] public struct Span<T> { T& data; NUInt len; }
            [IntrinsicType("FixedArray")] public struct FixedArray<T, N>;
            """, "Lib.mh");

        Assert.False(libCompilation.HasErrors);

        var consumerCompilation = Compilation.FromSource("""
            public struct Test
            {
                public Int32* p;
                public Int32& r;
                public Int32[] a;
                public Int32[&] s;
                public Int32[5] fa;
            }

            public struct Host
            {
                public func Run() -> void
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
    public void UnitIntrinsic_ZeroField_RegistersSuccessfully()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Unit")]
            public struct Unit;
            """);

        Assert.False(compilation.HasErrors);
        Assert.True(compilation.Context.DiscoveredIntrinsics.TryGet("Unit", out var unitHandle));
    }

    [Fact]
    public void UnitIntrinsic_WithInstanceFields_ReportsAbiMismatch()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Unit")]
            public struct Unit
            {
                Int8 x;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0020");
    }

    [Fact]
    public void OperatorFunction_ValidSignatures_RegisterSuccessfully()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Unit")]
            public struct void;
            public attribute OperatorFunction(Str fullName);

            public interface Add<TLhs, TRhs, TResult>
            {
                [OperatorFunction("Addition")]
                public func Add(TLhs left, TRhs right) -> TResult;
            }

            public interface Ctor<TArgs...>
            {
                [OperatorFunction("Constructor")]
                public func Construct(&this, TArgs... args) -> void;
            }

            public interface Dtor
            {
                [OperatorFunction("Destructor")]
                public func Destroy(&this) -> void;
            }

            public interface Index<TIndex, TResult>
            {
                [OperatorFunction("IndexerGetter")]
                public func Get(&this, TIndex index) -> TResult;
            }

            public interface Deref<TResult>
            {
                [OperatorFunction("IndirectMemberAccess")]
                public func Arrow(&this) -> TResult&;
            }

            public interface Assign<TRhs>
            {
                [OperatorFunction("Assignment")]
                public func Assign(&this, TRhs value) -> void;
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
            public attribute OperatorFunction(Str fullName);

            public interface BadAdd<TLhs, TRhs, TResult>
            {
                [OperatorFunction("Addition")]
                public func Add(TLhs left, TRhs right, TRhs extra) -> TResult;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("must accept exactly 2 parameters"));
    }

    [Fact]
    public void OperatorFunction_IndirectMemberAccessReturningVoid_ReportsMH0560()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Unit")]
            public struct void;
            public attribute OperatorFunction(Str fullName);

            public interface BadDeref
            {
                [OperatorFunction("IndirectMemberAccess")]
                public func Arrow(&this) -> void;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("return type cannot be void"));
    }

    [Fact]
    public void OperatorFunction_ConstructorStatic_ReportsMH0560()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Unit")]
            public struct void;
            public attribute OperatorFunction(Str fullName);

            public interface BadCtor
            {
                [OperatorFunction("Constructor")]
                public func Construct() -> void;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("constructors cannot be declared static"));
    }

    [Fact]
    public void OperatorFunction_IndexerGetterWithZeroParams_ReportsMH0560()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(Str fullName);

            public interface BadIndex<TResult>
            {
                [OperatorFunction("IndexerGetter")]
                public func Get(&this) -> TResult;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("must accept at least 1 index parameter"));
    }

    [Fact]
    public void OperatorFunction_DeclaredInStruct_ReportsMH0560()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(Str fullName);

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
            public attribute OperatorFunction(Str fullName);

            public interface BadOp
            {
                [OperatorFunction("NotARealOperator")]
                public func DoSomething() -> void;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("unknown operator function"));
    }

    [Fact]
    public void IntrinsicFunction_InIntrinsicType_TagsFunctionFlagsIntrinsic()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            intrinsic attribute IntrinsicFunction(Str target);

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
            intrinsic attribute IntrinsicFunction(Str target);

            public struct UserType
            {
                [IntrinsicFunction("foo")]
                public func Foo() -> void;
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
            intrinsic attribute IntrinsicFunction(Str target);

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
            intrinsic attribute IntrinsicFunction(Str target);

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
            public attribute OperatorFunction(Str name);

            public interface New<TTarget, TReturn>
            {
                [OperatorFunction("New")]
                public func New() -> TReturn;
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
                public func New(&this) -> TReturn;
            }
            """);
        Assert.True(nonStatic.HasErrors);
        Assert.Contains(nonStatic.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("must be declared static"));

        var voidReturn = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Unit")]
            public struct void;
            public attribute OperatorFunction(Str name);

            public interface BadNew<TTarget>
            {
                [OperatorFunction("New")]
                public func New() -> void;
            }
            """);
        Assert.True(voidReturn.HasErrors);
        Assert.Contains(voidReturn.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("return type cannot be void"));
    }

    [Fact]
    public void OperatorFunction_Put_ValidAndInvalidSignatures()
    {
        var valid = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Unit")]
            public struct void;

            public attribute OperatorFunction(Str name);

            public interface Put<TTarget>
            {
                [OperatorFunction("Put")]
                public func Put(TTarget& destination) -> void;
            }
            """);
        Assert.False(valid.HasErrors, string.Join("\n", valid.Diagnostics.Select(d => d.Message)));
        Assert.True(valid.Context.DiscoveredOperators.TryGetOperators(OperatorKind.Put, out var putOps));
        Assert.Single(putOps);

        var nonVoid = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Unit")]
            public struct void;

            public attribute OperatorFunction(Str name);

            public interface BadPut<TTarget>
            {
                [OperatorFunction("Put")]
                public func Put(TTarget& destination) -> Int64;
            }
            """);
        Assert.True(nonVoid.HasErrors);
        Assert.Contains(nonVoid.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("return type must be void"));
    }

    [Fact]
    public void OperatorFunction_Copy_And_Move_ValidAndInvalidSignatures()
    {
        var valid = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Unit")]
            public struct void;

            public attribute OperatorFunction(Str name);

            public interface Copy<T>
            {
                [OperatorFunction("Copy")]
                public func Copy(T& dest, T& src) -> void;
            }

            public interface Move<T>
            {
                [OperatorFunction("Move")]
                public func Move(T& dest, T& src) -> void;
            }
            """);
        Assert.False(valid.HasErrors, string.Join("\n", valid.Diagnostics.Select(d => d.Message)));
        Assert.True(valid.Context.DiscoveredOperators.TryGetOperators(OperatorKind.Copy, out var copyOps));
        Assert.Single(copyOps);
        Assert.True(valid.Context.DiscoveredOperators.TryGetOperators(OperatorKind.Move, out var moveOps));
        Assert.Single(moveOps);

        var badParamCount = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Unit")]
            public struct void;

            public attribute OperatorFunction(Str name);

            public interface BadCopy<T>
            {
                [OperatorFunction("Copy")]
                public func Copy(T& dest) -> void;
            }
            """);
        Assert.True(badParamCount.HasErrors);
        Assert.Contains(badParamCount.Diagnostics, d => d.Code == "MH0560" && d.Message.Contains("must accept exactly 2 parameters"));
    }

    [Fact]
    public void OperatorFunction_Callable_ValidSignature()
    {
        var valid = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            public attribute OperatorFunction(Str name);

            public interface Callable<TReturn, TArg>
            {
                [OperatorFunction("Callable")]
                public func Call(&this, TArg arg) -> TReturn;
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
            [IntrinsicType("Unit")]
            public struct void;

            public attribute OperatorFunction(Str name);

            public interface Destructor
            {
                [OperatorFunction("Destructor")]
                public func Drop(&this) -> void;
            }

            public interface Copy<T>
            {
                [OperatorFunction("Copy")]
                public func Copy(T& dest, T& src) -> void;
            }

            public struct Resource : Destructor, Copy<Resource>
            {
                public func Drop(&this) -> void {}
                public func Copy(Resource& dest, Resource& src) -> void {}
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0561" && d.Message.Contains("cannot implement both 'Destructor' and 'Copy'"));
    }

    [Fact]
    public void Lifecycle_CopyNandMove_ReportsMH0562()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            [IntrinsicType("Unit")]
            public struct void;

            public attribute OperatorFunction(Str name);

            public interface Copy<T>
            {
                [OperatorFunction("Copy")]
                public func Copy(T& dest, T& src) -> void;
            }

            public interface Move<T>
            {
                [OperatorFunction("Move")]
                public func Move(T& dest, T& src) -> void;
            }

            public struct Conflicted : Copy<Conflicted>, Move<Conflicted>
            {
                public func Copy(Conflicted& dest, Conflicted& src) -> void {}
                public func Move(Conflicted& dest, Conflicted& src) -> void {}
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0562" && d.Message.Contains("cannot implement both 'Copy' and 'Move'"));
    }

    [Fact]
    public void Delegate_ValidABI_WithReference_Succeeds()
    {
        var compilation = Compilation.FromSource(IntrinsicBootstrapPrelude + """
            intrinsic attribute IntrinsicFunction(Str signature);
            intrinsic attribute OperatorFunction(Str name);

            [IntrinsicType("Unit")]
            public struct void;

            public interface Callable<TReturn, TParams...>
            {
                [OperatorFunction("Callable")]
                public func Invoke(&this, TParams args) -> TReturn;
            }

            [IntrinsicType("Delegate")]
            public struct Delegate<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
            {
                NUInt codeAddress;
                ValType& env;
                [IntrinsicFunction("Delegate.Invoke(TParams...)")]
                public func Callable.Invoke(&this, TParams args) -> TReturn;
            }
            """);

        Assert.False(compilation.HasErrors);
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Delegate));
    }
}
