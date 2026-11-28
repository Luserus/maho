namespace Maho.Tests;

public sealed class ControlFlowAnalysisTests
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
    public void MissingReturn_InNonUnitFunction_ReportsMH1010()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Compute(int32 x) -> int32
            {
                int32 y = x;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1010");
    }

    [Fact]
    public void UnitFunction_OmittedReturn_CompilesCleanly()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func DoNothing() -> ()
            {
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Function_OmittedReturnType_DefaultsToEmptyTuple_CompilesCleanly()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func DoSomething(int32 x)
            {
                int32 y = x;
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
        var fn = Assert.Single(compilation.Context!.FunctionSymbols, f => f.Name.Text == "DoSomething");
        Assert.True(fn.ReturnType.IsEmptyTuple);
    }

    [Fact]
    public void FunctionPointer_OmittedReturnType_DefaultsToEmptyTuple()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func TestFnPtr(func(int32) fn) -> ()
            {
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Lambda_OmittedReturnType_DefaultsToEmptyTuple()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func TestLambda() -> ()
            {
                var fn = (int32 x) => { };
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void InfiniteLoop_WithoutBreak_InNonNeverFunction_ReportsMH1011()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func RunForever() -> int32
            {
                while (true)
                {
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1011");
    }
}
