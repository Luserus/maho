namespace Maho.Tests;

public sealed class ControlFlowAnalysisTests
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
            public func Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            public func Callable.Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Delegate")]
        public struct Delegate<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            ValType* env;
            [IntrinsicFunction("Delegate.Invoke(TParams...)")]
            public func Callable.Invoke(&this, TParams args) -> TReturn;
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
    public void Delegate_OmittedReturnType_DefaultsToEmptyTuple()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func TestDelegate(delegate(int32) del) -> ()
            {
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
