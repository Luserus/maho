using Maho.Diagnostics;
using Maho.Resolution;
using Maho.Syntax;
using Xunit;

namespace Maho.Tests;

public sealed class LambdaExpressionTests
{
    private const string StdPrelude = """
        intrinsic attribute IntrinsicType(Str name);

        intrinsic attribute IntrinsicFunction(Str signature);

        intrinsic attribute OperatorFunction(Str name);

        [IntrinsicType("Int8")] public struct int8;
        [IntrinsicType("Int64")] public struct int64;
        [IntrinsicType("NUInt")] public struct nuint;
        [IntrinsicType("Unit")] public struct void;
        [IntrinsicType("Int32")] public struct int32;

        [IntrinsicType("Reference")]
        public struct Reference<T>
        {
            nuint raw;
        }

        [IntrinsicType("Str")]
        public struct Str
        {
            int8& buffer;
            nuint length;
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
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            public func Callable.Invoke(TParams args) -> TReturn;
        }

        [IntrinsicType("Delegate")]
        public struct Delegate<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            Pointer<ValType> env;
            [IntrinsicFunction("Delegate.Invoke(TParams...)")]
            public func Callable.Invoke(TParams args) -> TReturn;
        }
        """;

    [Fact]
    public void Parse_LambdaExpressions_VariousForms_ParseCorrectly()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public func Test() -> void
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

            public class Host
            {
                public func Test() -> void
                {
                    func(int32) -> int32 f = x => x;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void Lambda_Capturing_InfersDelegateType()
    {
        var compilation = Compilation.FromSource($$"""
            {{StdPrelude}}

            public class Host
            {
                public func Test(int32 captured) -> void
                {
                    delegate(int32) -> int32 d = x => captured;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
    }

    [Fact]
    public void Lambda_BlockBody_WithExplicitReturn_Succeeds()
    {
        var compilation = Compilation.FromSource($$"""
            {{StdPrelude}}

            public class Host
            {
                public func Test() -> void
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

            public class Host
            {
                public func Test() -> void
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
