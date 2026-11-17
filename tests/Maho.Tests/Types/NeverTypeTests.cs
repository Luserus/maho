using System.Linq;
using Maho;
using Maho.Resolution;
using Maho.Syntax;
using Xunit;

namespace Maho.Tests;

public sealed class NeverTypeTests
{
    private const string Prelude = """
        intrinsic attribute IntrinsicType(Str name);
        intrinsic attribute IntrinsicFunction(Str signature);
        intrinsic attribute OperatorFunction(Str name);

        [IntrinsicType("Int8")]
        public struct int8;
        [IntrinsicType("Int32")]
        public struct int32;
        [IntrinsicType("Int64")]
        public struct int64;
        [IntrinsicType("NUInt")]
        public struct nuint;
        [IntrinsicType("Unit")]
        public struct Unit;
        [IntrinsicType("Never")]
        public struct Never;
        [IntrinsicType("Bool")]
        public struct bool;

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
            ValType* env;
            [IntrinsicFunction("Delegate.Invoke(TParams...)")]
            public func Callable.Invoke(TParams args) -> TReturn;
        }
        """;

    [Fact]
    public void NeverType_Syntax_ParsesCorrectly()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            func Panic() -> Never;
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var fn = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members));
        var simple = Assert.IsType<SimpleType>(fn.Function.Signature.ReturnType);
        Assert.Equal("Never", simple.Name.Value);
    }

    [Fact]
    public void NeverType_DivergingFunction_CompilesCleanly()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Panic() -> Never
            {
                while (true)
                {
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void NeverType_AssignableToAnyTargetType()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Panic() -> Never
            {
                while (true) {}
            }

            public func Test() -> int32
            {
                int32 x = Panic();
                return x;
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void NeverType_DivergingFunction_WithDeclaredReturnType_ReportsMH2018()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Bad() -> int32
            {
                while (true)
                {
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2018");
    }

    [Fact]
    public void NeverType_FunctionReturningNever_CannotReturn_ReportsMH2006()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func BadPanic() -> Never
            {
                return;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2006");
    }

    [Fact]
    public void NeverType_CallMarksSubsequentCodeUnreachable()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Panic() -> Never
            {
                while (true) {}
            }

            public func Test() -> int32
            {
                Panic();
                // Subsequent statement is unreachable, so function does not fall through
            }
            """);

        // Since Panic() returns Never, the block terminates as Unreachable and doesn't fall through to exit.
        // And since all paths diverge (calling Panic()), the function Test() must return Never
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2018");
    }

    [Fact]
    public void NeverType_TestDeclaredNever_CallingPanic_CompilesCleanly()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Panic() -> Never
            {
                while (true) {}
            }

            public func Test() -> Never
            {
                Panic();
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void NeverType_CannotAssignToNever_ReportsError()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Test(Never n)
            {
                n = 42;
            }
            """);

        Assert.True(compilation.HasErrors);
    }
}
