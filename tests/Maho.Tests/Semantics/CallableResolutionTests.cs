namespace Maho.Tests;

public sealed class CallableResolutionTests
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
    public void StandaloneLiteralCall_WhenNotCallable_ReportsMH0611()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Test() -> ()
            {
                123();
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0611");
    }

    [Fact]
    public void TargetTypedLiteralCall_ResolvesOverloadViaTargetType()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct CustomTarget : Callable<CustomTarget>
            {
                public literal int(int32 val)
                {
                    CustomTarget res;
                    return res;
                }

                func Callable.Invoke(this&) -> CustomTarget
                {
                    return *this;
                }
            }

            public func Test() -> CustomTarget
            {
                CustomTarget c = 123();
                return c;
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void TargetTypedLiteralCall_WithByRefReceiver_Succeeds()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct CustomTarget : Callable<CustomTarget>
            {
                public literal int(int32 val)
                {
                    CustomTarget res;
                    return res;
                }

                func Callable.Invoke(this&) -> CustomTarget
                {
                    return *this;
                }
            }

            public func Test() -> CustomTarget
            {
                CustomTarget c = 123();
                return c;
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void TargetTypedLiteralCall_AmbiguousLiteralOverloads_ReportsMH2013()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct CustomTarget : Callable<CustomTarget>
            {
                public literal int(int32 val)
                {
                    CustomTarget res;
                    return res;
                }

                public literal int(int64 val)
                {
                    CustomTarget res;
                    return res;
                }

                func Callable.Invoke(this&) -> CustomTarget
                {
                    return *this;
                }
            }

            public func Test() -> CustomTarget
            {
                CustomTarget c = 123();
                return c;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2013");
    }
}
