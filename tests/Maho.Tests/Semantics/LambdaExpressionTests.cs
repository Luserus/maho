using Maho.Syntax;

namespace Maho.Tests;

public sealed class LambdaExpressionTests
{
    private const string StdPrelude = """
        [IntrinsicType("Layout")] public interface Layout;
        [IntrinsicType("Sized")] public interface Sized : Layout;
        [IntrinsicType("Unsized")] public interface Unsized : Layout;

        intrinsic attribute IntrinsicType(Str& name);

        intrinsic attribute IntrinsicFunction(Str& signature);

        intrinsic attribute OperatorFunction(Str& name);

        [IntrinsicType("UInt8")] public struct uint8;
        [IntrinsicType("Int8")] public struct int8;
        [IntrinsicType("Int64")] public struct int64;
        [IntrinsicType("NUInt")] public struct nuint;
        [IntrinsicType("Int32")] public struct int32;

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
            nuint codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            func Callable.Invoke(this&, TParams args) -> TReturn;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            func CallableMut.Invoke(this mut&, TParams args) -> TReturn;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            func CallableOnce.Invoke(this, TParams args) -> TReturn;
        }
        """;

    [Fact]
    public void Parse_LambdaExpressions_VariousForms_ParseCorrectly()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public func Test() -> ()
            {
                var l1 = x => x;
                var l2 = (x, y) => x;
                var l3 = (int a, int b) -> int => a;
                var l4 = (int a) => { return a; };
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var funcDecl = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members));
        var body = Assert.IsType<FunctionBlockBody>(funcDecl.Function.Body);
        Assert.Equal(4, body.Locals.Count);

        var s1 = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[0]);
        var lambda1 = Assert.IsType<LambdaExpression>(s1.Declaration.Declarator.Initializer!.Initializer);
        Assert.Single(lambda1.Parameters);

        var s2 = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[1]);
        var lambda2 = Assert.IsType<LambdaExpression>(s2.Declaration.Declarator.Initializer!.Initializer);
        Assert.Equal(2, lambda2.Parameters.Count);

        var s3 = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[2]);
        var lambda3 = Assert.IsType<LambdaExpression>(s3.Declaration.Declarator.Initializer!.Initializer);
        Assert.NotNull(lambda3.ReturnType);

        var s4 = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[3]);
        var lambda4 = Assert.IsType<LambdaExpression>(s4.Declaration.Declarator.Initializer!.Initializer);
        Assert.IsType<BlockExpression>(lambda4.Body);
    }

    [Fact]
    public void Lambda_NonCapturing_InfersFunctionType()
    {
        var compilation = Compilation.FromSource($$"""
            {{StdPrelude}}

            public struct Host
            {
                public func Test() -> ()
                {
                    func(int32) -> int32 f = x => x;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void Lambda_Capturing_SynthesizesClosureStruct()
    {
        var compilation = Compilation.FromSource($$"""
            {{StdPrelude}}

            public struct Host
            {
                public func Test(int32 captured) -> int32
                {
                    var d = (int32 x) => captured;
                    return d(5);
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Contains(compilation.Context!.TypeSymbols, t => t.Name.Text.StartsWith("__Closure_"));
    }

    [Fact]
    public void Lambda_BlockBody_WithExplicitReturn_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{StdPrelude}}

            public struct Host
            {
                public func Test() -> ()
                {
                    func(int32) -> int32 f = (int32 x) -> int32 => {
                        int32 y = x;
                        return y;
                    };
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void Lambda_BlockBody_TrailingExpressionWithoutReturn_ReportsError()
    {
        var compilation = Compilation.FromSource($$"""
            {{StdPrelude}}

            public struct Host
            {
                public func Test() -> ()
                {
                    func(int32) -> int32 f = (int32 x) -> int32 => {
                        int32 y = x;
                        y
                    };
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code is "MH2016" or "MH0120" or "MH0126");
    }
}
