using Maho.Resolution;

namespace Maho.Tests;

public sealed class OptionalAndPostfixOperatorTests
{
    private const string BasePrelude = """
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

        [IntrinsicType("Enum")]
        public struct Enum : ValType;

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


        public interface Unwrap<TReturn>
        {
            [OperatorFunction("Unwrap")]
            func Unwrap(this&) -> TReturn;
        }

        public interface Try<TReturn, TFail>
        {
            [OperatorFunction("Try")]
            func Try(this&) -> (TReturn | TFail);
        }

        public interface Implicit<TFrom, TTo>
        {
            [OperatorFunction("ImplicitCast")]
            func Implicit(TFrom from) -> TTo;
        }

        [IntrinsicType("Optional")]
        public enum Optional<T> : Enum, Unwrap<T>, Try<T, Optional<T>>, Implicit<T, Optional<T>>
        {
            Some(T value),
            None,

            [IntrinsicFunction("Optional.Unwrap")]
            func Unwrap.Unwrap(this&) -> T;

            [IntrinsicFunction("Optional.Try")]
            func Try.Try(this&) -> (T | Optional<T>);

            [IntrinsicFunction("Optional.Implicit")]
            func Implicit.Implicit(T from) -> Optional<T>;
        }
        """;

    [Fact]
    public void Optional_IntrinsicDiscovery_RegistersSuccessfully()
    {
        var compilation = Compilation.FromSource(BasePrelude);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
        Assert.True(compilation.Context.DiscoveredIntrinsics.Contains(KnownIntrinsics.Optional));
        Assert.True(compilation.Context.DiscoveredOperators.TryGetOperators(OperatorKind.Unwrap, out _));
        Assert.True(compilation.Context.DiscoveredOperators.TryGetOperators(OperatorKind.Try, out _));
    }

    [Fact]
    public void Postfix_Unwrap_Operator_ResolvesPayloadType()
    {
        var compilation = Compilation.FromSource(BasePrelude + """
            public func TestUnwrap(Optional<int32> opt) -> int32
            {
                int32 val = opt!;
                return val;
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Postfix_Try_Operator_UnwrapsPayload_InFunctionReturningOptional()
    {
        var compilation = Compilation.FromSource(BasePrelude + """
            public func TestTry(Optional<int32> opt) -> Optional<int32>
            {
                int32 val = opt?;
                return opt;
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Postfix_Try_Operator_IncompatibleFunctionReturn_ReportsDiagnostic()
    {
        var compilation = Compilation.FromSource(BasePrelude + """
            public func TestTryBad(Optional<int32> opt) -> int32
            {
                int32 val = opt?;
                return val;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code is "MH2005" or "MH2006");
    }

    [Fact]
    public void ImplicitCast_T_To_OptionalT_ViaOperator()
    {
        var compilation = Compilation.FromSource(BasePrelude + """
            public func TestImplicit(int32 x) -> Optional<int32>
            {
                Optional<int32> opt = x;
                return opt;
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Postfix_Operator_OnNonSupportingType_ReportsMH2002()
    {
        var compilation = Compilation.FromSource(BasePrelude + """
            public func TestBad(int32 x) -> int32
            {
                var y = x!;
                return x;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2002");
    }
}
