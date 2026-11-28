using Maho.Syntax;

namespace Maho.Tests;

public sealed class NeverTypeTests
{
    private const string Prelude = """
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
        [IntrinsicType("Int32")]
        public struct int32;
        [IntrinsicType("Int64")]
        public struct int64;
        [IntrinsicType("NUInt")]
        public struct nuint;
        [IntrinsicType("Never")]
        public struct Never;
        [IntrinsicType("Bool")]
        public struct bool;

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

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            func Callable.Invoke(this&, TParams args) -> TReturn;
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
    public void NeverType_DivergingFunction_WithDeclaredReturnType_ReportsMH1011()
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
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1011");
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
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1011");
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
