using Maho.Resolution;
using Maho.Syntax;

namespace Maho.Tests;

public sealed class FunctionPointerAndDelegateTests
{
    private const string StdPrelude = """
        [IntrinsicType("Layout")]
        public interface Layout;
        [IntrinsicType("Sized")]
        public interface Sized : Layout;
        [IntrinsicType("Unsized")]
        public interface Unsized : Layout;

        intrinsic attribute IntrinsicType(Str& name);

        intrinsic attribute IntrinsicFunction(Str& signature);

        intrinsic attribute OperatorFunction(Str& name);

        [IntrinsicType("UInt8")]
        public struct uint8;

        [IntrinsicType("Int8")]
        public struct int8;

        [IntrinsicType("Int64")]
        public struct int64;

        [IntrinsicType("NUInt")]
        public struct nuint;


        [IntrinsicType("Int32")]
        public struct int32;

        [IntrinsicType("Slice")]
        public struct Slice<T, D : int = 1>;

        [IntrinsicType("Metadata")]
        public struct Metadata<T> where T : Layout;

        [IntrinsicType("Reference")]
        public struct Reference<T> where T : Layout
        {
            nuint raw;
            Metadata<T> metadata;
        }

        [IntrinsicType("Str")]
        public struct Str
        {
            uint8[] data;
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T> where T : Layout
        {
            nuint addr;
            Metadata<T> metadata;
        }

        [IntrinsicType("ValueType")]
        public struct ValType;

        [IntrinsicType("Struct")]
        public struct Struct : ValType;

        public interface Callable<TReturn, TParams...>
        {
            [OperatorFunction("Callable")]
            func Invoke(this&, TParams args) -> TReturn;
        }

        public interface CallableMut<TReturn, TParams...>
        {
            [OperatorFunction("CallableMut")]
            func Invoke(this mut&, TParams args) -> TReturn;
        }

        public interface CallableOnce<TReturn, TParams...>
        {
            [OperatorFunction("CallableOnce")]
            func Invoke(this, TParams args) -> TReturn;
        }

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>, CallableMut<TReturn, TParams...>, CallableOnce<TReturn, TParams...>
        {
            public nuint codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            func Callable.Invoke(this&, TParams args) -> TReturn;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            func CallableMut.Invoke(this mut&, TParams args) -> TReturn;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            func CallableOnce.Invoke(this, TParams args) -> TReturn;
        }
        """;

    [Fact]
    public void Parse_FunctionTypeSyntax_ParsesCorrectly()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            func(int, int) -> int fnPtr;
            func(string) -> () fnUnit;
            """);

        Assert.Empty(diagnostics.Diagnostics);
        Assert.Equal(2, root.Members.Count);

        var topFn = Assert.IsType<TopLevelVariableDeclaration>(root.Members[0]);
        var fnType = Assert.IsType<FunctionTypeSyntax>(topFn.Declaration.Type);
        Assert.Equal(2, fnType.ParameterTypes.Count);
        Assert.NotNull(fnType.ReturnType);
        Assert.Equal("int", Assert.IsType<SimpleType>(fnType.ReturnType).Name.Value);

        var topUnit = Assert.IsType<TopLevelVariableDeclaration>(root.Members[1]);
        var fnUnit = Assert.IsType<FunctionTypeSyntax>(topUnit.Declaration.Type);
        Assert.Single(fnUnit.ParameterTypes);
        Assert.NotNull(fnUnit.ReturnType);
        var tupleRet = Assert.IsType<TupleType>(fnUnit.ReturnType);
        Assert.Empty(tupleRet.Elements);
    }

    [Fact]
    public void Resolve_FunctionVariablesAndMethods_IncludeExplicitReceiverInMethodType()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct Counter
            {
                public int32 val;
                public func Read(this&) -> int32 { return this->val; }
                public func Inc(this mut&) -> int32 { this->val = 1; return this->val; }
                public func Create() -> Counter { Counter c; c.val = 0; return c; }
            }

            public struct Host
            {
                public func(int32, int32) -> int32 fnVar;
                public func(Counter&) -> int32 unboundRead = Counter.Read;
                public func(Counter mut&) -> int32 unboundInc = Counter.Inc;
            }
            """);

        Assert.Empty(compilation.Diagnostics);
        var context = compilation.Context!;

        var host = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "Host");
        var fields = context.FieldSymbols.Where(f => f.Parent == ResolutionContext.GetHandle(host)).ToList();
        Assert.Equal(3, fields.Count);

        var fnField = fields.First(f => f.Name.ToString() == "fnVar");
        Assert.Equal(TypeRefKind.Function, fnField.Type.Kind);
        var fnSem = Assert.IsType<FunctionSemanticType>(fnField.Type.SpecialType);
        Assert.Equal(2, fnSem.ParameterTypes.Count);

        var readField = fields.First(f => f.Name.ToString() == "unboundRead");
        var readSem = Assert.IsType<FunctionSemanticType>(readField.Type.SpecialType);
        Assert.Single(readSem.ParameterTypes);
        Assert.Equal(TypeRefKind.Reference, readSem.ParameterTypes[0].Kind);
    }

    [Fact]
    public void Resolve_FunctionType_WhenIntrinsicMissing_ReportsMH0021()
    {
        var compilation = Compilation.FromSource("""
            [IntrinsicType("Layout")] public interface Layout;
            [IntrinsicType("Sized")] public interface Sized : Layout;
            [IntrinsicType("Unsized")] public interface Unsized : Layout;
            intrinsic attribute IntrinsicType(Str& name);
            [IntrinsicType("UInt8")] public struct uint8;
            [IntrinsicType("Int8")] public struct int8;
            [IntrinsicType("Int64")] public struct int64;
            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;
            [IntrinsicType("Metadata")] public struct Metadata<T> where T : Layout;
            [IntrinsicType("Reference")] public struct Reference<T> where T : Layout { nuint raw; Metadata<T> metadata; }
            [IntrinsicType("Str")] public struct Str { uint8[] data; }
            [IntrinsicType("NUInt")] public struct nuint;
            [IntrinsicType("Int32")] public struct int32;

            public func(int32) -> int32 fn;
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0021" && d.Message.Contains("Function"));
    }

    [Fact]
    public void UnboundInstanceMethod_WithExplicitReceiverArg_Succeeds_AndOmitted_ReportsMH2019()
    {
        var valid = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct Counter
            {
                public int32 val;
                public func Inc(this mut&, int32 delta) -> int32 { this->val = delta; return this->val; }
            }

            public func Test() -> int32
            {
                Counter c;
                c.val = 0;
                return Counter.Inc(&c, 5);
            }
            """);
        Assert.False(valid.HasErrors, string.Join("\n", valid.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var missingRecv = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct Counter
            {
                public int32 val;
                public func Read(this&) -> int32 { return this->val; }
            }

            public func Test() -> int32
            {
                return Counter.Read();
            }
            """);
        Assert.True(missingRecv.HasErrors);
        Assert.Contains(missingRecv.Diagnostics, d => d.Code == "MH2019");
    }

    [Fact]
    public void Invocation_FunctionPointer_ResolvesInvocation()
    {
        var compilation = Compilation.FromSource($$"""
            {{StdPrelude}}

            public struct Host
            {
                public func Test(func(int32, int32) -> int32 f, int32 a, int32 b) -> int32
                {
                    return f(a, b);
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void Invocation_CallableInterfaces_ResolveWithMatchingMutability()
    {
        var valid = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct Host
            {
                public func CallShared(Callable<int32, int32>& f, int32 a) -> int32
                {
                    return f(a);
                }

                public func CallMut(CallableMut<int32, int32> mut& f, int32 a) -> int32
                {
                    return f(a);
                }
            }
            """);
        Assert.False(valid.HasErrors, string.Join("\n", valid.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var immutCallingMut = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct Host
            {
                public func Bad(CallableMut<int32, int32>& f, int32 a) -> int32
                {
                    return f(a);
                }
            }
            """);
        Assert.True(immutCallingMut.HasErrors);
        Assert.Contains(immutCallingMut.Diagnostics, d => d.Code == "MH0611");
    }

    [Fact]
    public void MemberAccess_OnFunctionType_ResolvesAgainstFunctionIntrinsic()
    {
        var compilation = Compilation.FromSource($$"""
            {{StdPrelude}}

            public struct Host
            {
                public func Test(func(int32, int32) -> int32 f) -> nuint
                {
                    return f.codeAddress;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void Function_WithoutCallableImplementation_ReportsMH0020()
    {
        var compilation = Compilation.FromSource("""
            [IntrinsicType("Layout")] public interface Layout;
            [IntrinsicType("Sized")] public interface Sized : Layout;
            [IntrinsicType("Unsized")] public interface Unsized : Layout;
            intrinsic attribute IntrinsicType(Str& name);

            [IntrinsicType("UInt8")] public struct uint8;
            [IntrinsicType("Int8")] public struct int8;
            [IntrinsicType("Int64")] public struct int64;
            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;
            [IntrinsicType("Metadata")] public struct Metadata<T> where T : Layout;
            [IntrinsicType("Reference")] public struct Reference<T> where T : Layout { nuint raw; Metadata<T> metadata; }
            [IntrinsicType("Str")] public struct Str { uint8[] data; }
            [IntrinsicType("NUInt")] public struct nuint;
            [IntrinsicType("ValueType")] public struct ValType;

            [IntrinsicType("Function")]
            public struct Function<TReturn, TParams...> : ValType
            {
                nuint codeAddress;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0020" && d.Message.Contains("Callable"));
    }

    [Fact]
    public void StructMethod_WithSemicolonWithoutIntrinsicFunction_ReportsMH0124()
    {
        var compilation = Compilation.FromSource("""
            [IntrinsicType("Layout")] public interface Layout;
            [IntrinsicType("Sized")] public interface Sized : Layout;
            [IntrinsicType("Unsized")] public interface Unsized : Layout;
            intrinsic attribute IntrinsicType(Str& name);

            [IntrinsicType("UInt8")] public struct uint8;
            [IntrinsicType("Int8")] public struct int8;
            [IntrinsicType("Int64")] public struct int64;
            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;
            [IntrinsicType("Metadata")] public struct Metadata<T> where T : Layout;
            [IntrinsicType("Reference")] public struct Reference<T> where T : Layout { nuint raw; Metadata<T> metadata; }
            [IntrinsicType("Str")] public struct Str { uint8[] data; }
            [IntrinsicType("NUInt")] public struct nuint;
            [IntrinsicType("ValueType")] public struct ValType;
            [IntrinsicType("Struct")] public struct Struct : ValType;

            public struct Sample
            {
                public func Method() -> ();
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0124");
    }

    [Fact]
    public void Call_WithoutCallableInPrelude_ReportsMH0021()
    {
        var compilation = Compilation.FromSource("""
            [IntrinsicType("Layout")] public interface Layout;
            [IntrinsicType("Sized")] public interface Sized : Layout;
            [IntrinsicType("Unsized")] public interface Unsized : Layout;
            intrinsic attribute IntrinsicType(Str& name);

            [IntrinsicType("UInt8")] public struct uint8;
            [IntrinsicType("Int8")] public struct int8;
            [IntrinsicType("Int64")] public struct int64;
            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;
            [IntrinsicType("Metadata")] public struct Metadata<T> where T : Layout;
            [IntrinsicType("Reference")] public struct Reference<T> where T : Layout { nuint raw; Metadata<T> metadata; }
            [IntrinsicType("Str")] public struct Str { uint8[] data; }
            [IntrinsicType("NUInt")] public struct nuint;
            [IntrinsicType("Int32")] public struct int32;
            [IntrinsicType("ValueType")] public struct ValType;
            [IntrinsicType("Struct")] public struct Struct : ValType;

            public struct Host
            {
                public func Test() -> ()
                {
                    DoSomething();
                }

                public func DoSomething() -> ()
                {
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0021" && d.Message.Contains("Callable"));
    }

    [Fact]
    public void FunctionType_RightAssociativeParsing_NestedReturnTypes()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            func() -> func() -> func() -> int32 fn;
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var topFn = Assert.IsType<TopLevelVariableDeclaration>(root.Members[0]);
        var level1 = Assert.IsType<FunctionTypeSyntax>(topFn.Declaration.Type);
        Assert.Empty(level1.ParameterTypes);

        var level2 = Assert.IsType<FunctionTypeSyntax>(level1.ReturnType);
        Assert.Empty(level2.ParameterTypes);

        var level3 = Assert.IsType<FunctionTypeSyntax>(level2.ReturnType);
        Assert.Empty(level3.ParameterTypes);

        var baseType = Assert.IsType<SimpleType>(level3.ReturnType);
        Assert.Equal("int32", baseType.Name.Value);
    }

    [Fact]
    public void Resolve_RightAssociativeFunctionType_ChainedInvocations()
    {
        var compilation = Compilation.FromSource($$"""
            {{StdPrelude}}

            public struct Host
            {
                public func Some() -> func() -> func() -> int32
                {
                    return () => () => 42;
                }

                public func Run() -> int32
                {
                    var x = Some;
                    var f1 = x();
                    var f2 = f1();
                    return f2();
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void Resolve_RightAssociativeFunctionType_DirectChainedCall()
    {
        var compilation = Compilation.FromSource($$"""
            {{StdPrelude}}

            public struct Host
            {
                public func Some() -> func() -> func() -> int32
                {
                    return () => () => 42;
                }

                public func Run() -> int32
                {
                    return Some()()();
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void Resolve_RightAssociativeFunctionType_OvercallingReportsMH0611()
    {
        var compilation = Compilation.FromSource($$"""
            {{StdPrelude}}

            public struct Host
            {
                public func Some() -> func() -> func() -> int32
                {
                    return () => () => 42;
                }

                public func Run() -> ()
                {
                    Some()()()();
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0611");
    }
}
