using System.Linq;
using Maho;
using Maho.Resolution;
using Maho.Syntax;
using Xunit;

namespace Maho.Tests;

public sealed class CallableResolutionTests
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
    public void StandaloneLiteralCall_WhenNotCallable_ReportsMH0611()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Test() -> Unit
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
            public interface MyCallable
            {
                [OperatorFunction("Callable")]
                public func Invoke(&this) -> CustomTarget;
            }

            public struct CustomTarget : ValType, MyCallable
            {
                public literal int(int32 val)
                {
                    CustomTarget res;
                    return res;
                }

                public func MyCallable.Invoke(&this) -> CustomTarget
                {
                    return this;
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
    public void TargetTypedLiteralCall_AmbiguousLiteralOverloads_ReportsMH2013()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public interface MyCallable
            {
                [OperatorFunction("Callable")]
                public func Invoke(&this) -> CustomTarget;
            }

            public struct CustomTarget : ValType, MyCallable
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

                public func MyCallable.Invoke(&this) -> CustomTarget
                {
                    return this;
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
