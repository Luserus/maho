using Maho.Resolution;
using Maho.Syntax;

namespace Maho.Tests;

public sealed class FunctionPointerAndDelegateTests
{
    private const string StdPrelude = """
        [intrinsic]
        public attribute IntrinsicType(Str name);

        [intrinsic]
        public attribute IntrinsicFunction;

        [intrinsic]
        public attribute OperatorFunction(Str name);

        [IntrinsicType("Int8")]
        public struct int8;

        [IntrinsicType("Int64")]
        public struct int64;

        [IntrinsicType("NUInt")]
        public struct nuint;

        [IntrinsicType("Void")]
        public struct void;

        [IntrinsicType("Int32")]
        public struct int32;

        [IntrinsicType("Reference")]
        public struct Reference<T>
        {
            nuint raw;
        }

        [IntrinsicType("Str")]
        public struct Str
        {
            int8& buffer;
            int64 length;
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T>
        {
            nuint addr;
        }

        [IntrinsicType("ValueType")]
        public struct ValType;

        [IntrinsicType("Struct")]
        public struct Struct : ValType;

        public interface Callable<TReturn, TParams...>
        {
            [OperatorFunction("Callable")]
            public func Invoke(TParams args) -> TReturn;
        }

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            [IntrinsicFunction]
            public func Callable.Invoke(TParams args) -> TReturn;
        }

        [IntrinsicType("Delegate")]
        public struct Delegate<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            ValType* env;
            [IntrinsicFunction]
            public func Callable.Invoke(TParams args) -> TReturn;
        }
        """;

    [Fact]
    public void Parse_FunctionAndDelegateTypeSyntax_ParsesCorrectly()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            func(int, int) -> int fnPtr;
            delegate(string) -> void del;
            """);

        Assert.Empty(diagnostics.Diagnostics);
        Assert.Equal(2, root.Members.Count);

        var topFn = Assert.IsType<TopLevelVariableDeclaration>(root.Members[0]);
        var fnType = Assert.IsType<FunctionTypeSyntax>(topFn.Declaration.Type);
        Assert.Equal(2, fnType.ParameterTypes.Count);
        Assert.NotNull(fnType.ReturnType);
        Assert.Equal("int", Assert.IsType<SimpleType>(fnType.ReturnType).Name.Value);

        var topDel = Assert.IsType<TopLevelVariableDeclaration>(root.Members[1]);
        var delType = Assert.IsType<DelegateTypeSyntax>(topDel.Declaration.Type);
        Assert.Single(delType.ParameterTypes);
        Assert.NotNull(delType.ReturnType);
        Assert.Equal("void", Assert.IsType<SimpleType>(delType.ReturnType).Name.Value);
    }

    [Fact]
    public void Resolve_FunctionAndDelegateVariables_ResolvesToFunctionAndDelegateTypes()
    {
        var compilation = Compilation.FromSource($$"""
            {{StdPrelude}}

            public class Host
            {
                public func(int32, int32) -> int32 fnVar;
                public delegate(int32) -> void delVar;
            }
            """);

        Assert.Empty(compilation.Diagnostics);
        var context = compilation.Context!;

        var host = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "Host");
        var fields = context.FieldSymbols.Where(f => f.Parent == ResolutionContext.GetHandle(host)).ToList();
        Assert.Equal(2, fields.Count);

        var fnField = fields.First(f => f.Name.ToString() == "fnVar");
        Assert.Equal(TypeRefKind.Function, fnField.Type.Kind);
        var fnSem = Assert.IsType<FunctionSemanticType>(fnField.Type.SpecialType);
        Assert.Equal(2, fnSem.ParameterTypes.Count);

        var delField = fields.First(f => f.Name.ToString() == "delVar");
        Assert.Equal(TypeRefKind.Delegate, delField.Type.Kind);
        var delSem = Assert.IsType<DelegateSemanticType>(delField.Type.SpecialType);
        Assert.Single(delSem.ParameterTypes);
    }

    [Fact]
    public void Resolve_FunctionType_WhenIntrinsicMissing_ReportsMH0240()
    {
        var compilation = Compilation.FromSource("""
            [intrinsic]
            public attribute IntrinsicType(Str name);
            [IntrinsicType("Int8")] public struct int8;
            [IntrinsicType("Int64")] public struct int64;
            [IntrinsicType("Reference")] public struct Reference<T> { nuint raw; }
            [IntrinsicType("Str")] public struct Str { int8& buffer; int64 length; }
            [IntrinsicType("NUInt")] public struct nuint;
            [IntrinsicType("Void")] public struct void;
            [IntrinsicType("Int32")] public struct int32;

            public func(int32) -> int32 fn;
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0240" && d.Message.Contains("Function"));
    }

    [Fact]
    public void Resolve_DelegateType_WhenIntrinsicMissing_ReportsMH0240()
    {
        var compilation = Compilation.FromSource("""
            [intrinsic]
            public attribute IntrinsicType(Str name);
            [IntrinsicType("Int8")] public struct int8;
            [IntrinsicType("Int64")] public struct int64;
            [IntrinsicType("Reference")] public struct Reference<T> { nuint raw; }
            [IntrinsicType("Str")] public struct Str { int8& buffer; int64 length; }
            [IntrinsicType("NUInt")] public struct nuint;
            [IntrinsicType("Void")] public struct void;
            [IntrinsicType("Int32")] public struct int32;

            public delegate(int32) -> int32 del;
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0240" && d.Message.Contains("Delegate"));
    }

    [Fact]
    public void Invocation_FunctionPointer_ResolvesInvocation()
    {
        var compilation = Compilation.FromSource($$"""
            {{StdPrelude}}

            public class Host
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
    public void Invocation_Delegate_ResolvesInvocation()
    {
        var compilation = Compilation.FromSource($$"""
            {{StdPrelude}}

            public class Host
            {
                public func Test(delegate(int32) -> void d, int32 a) -> void
                {
                    d(a);
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void MemberAccess_OnFunctionType_ResolvesAgainstFunctionIntrinsic()
    {
        var compilation = Compilation.FromSource($$"""
            {{StdPrelude}}

            public class Host
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
    public void Function_WithoutCallableImplementation_ReportsMH0544()
    {
        var compilation = Compilation.FromSource("""
            [intrinsic]
            public attribute IntrinsicType(Str name);

            [IntrinsicType("Int8")] public struct int8;
            [IntrinsicType("Int64")] public struct int64;
            [IntrinsicType("Reference")] public struct Reference<T> { nuint raw; }
            [IntrinsicType("Str")] public struct Str { int8& buffer; int64 length; }
            [IntrinsicType("NUInt")] public struct nuint;
            [IntrinsicType("ValueType")] public struct ValType;

            [IntrinsicType("Function")]
            public struct Function<TReturn, TParams...> : ValType
            {
                nuint codeAddress;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0544" && d.Message.Contains("Callable"));
    }

    [Fact]
    public void StructMethod_WithSemicolonWithoutIntrinsicFunction_ReportsMH0124()
    {
        var compilation = Compilation.FromSource("""
            [intrinsic]
            public attribute IntrinsicType(Str name);

            [IntrinsicType("Int8")] public struct int8;
            [IntrinsicType("Int64")] public struct int64;
            [IntrinsicType("Reference")] public struct Reference<T> { nuint raw; }
            [IntrinsicType("Str")] public struct Str { int8& buffer; int64 length; }
            [IntrinsicType("NUInt")] public struct nuint;
            [IntrinsicType("ValueType")] public struct ValType;
            [IntrinsicType("Struct")] public struct Struct : ValType;

            public struct Sample
            {
                public func Method() -> void;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0124");
    }

    [Fact]
    public void Call_WithoutCallableInPrelude_ReportsMH0240()
    {
        var compilation = Compilation.FromSource("""
            [intrinsic]
            public attribute IntrinsicType(Str name);

            [IntrinsicType("Int8")] public struct int8;
            [IntrinsicType("Int64")] public struct int64;
            [IntrinsicType("Reference")] public struct Reference<T> { nuint raw; }
            [IntrinsicType("Str")] public struct Str { int8& buffer; int64 length; }
            [IntrinsicType("NUInt")] public struct nuint;
            [IntrinsicType("Void")] public struct void;
            [IntrinsicType("Int32")] public struct int32;
            [IntrinsicType("ValueType")] public struct ValType;
            [IntrinsicType("Struct")] public struct Struct : ValType;

            public class Host
            {
                public func Test() -> void
                {
                    DoSomething();
                }

                public func DoSomething() -> void
                {
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0240" && d.Message.Contains("Callable"));
    }
}
