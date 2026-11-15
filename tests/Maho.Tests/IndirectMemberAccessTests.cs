using System.Linq;
using Maho.Resolution;
using Xunit;

namespace Maho.Tests;

public sealed class IndirectMemberAccessTests
{
    private const string Prelude = """
        intrinsic attribute IntrinsicType(Str name);
        intrinsic attribute IntrinsicFunction(Str signature);
        intrinsic attribute OperatorFunction(Str name);

        [IntrinsicType("Int8")]
        public struct int8;
        [IntrinsicType("Int32")]
        public struct Int32;
        [IntrinsicType("NUInt")]
        public struct nuint;
        [IntrinsicType("Unit")]
        public struct Unit;

        [IntrinsicType("Metadata")]
        public struct Metadata<T>;

        public interface Arrow<T>
        {
            [OperatorFunction("IndirectMemberAccess")]
            public func Arrow() -> ref T;
        }

        [IntrinsicType("Reference")]
        public struct Reference<T> : Arrow<T>
        {
            nuint raw;
            Metadata<T> metadata;

            [IntrinsicFunction("Reference.Deref()")]
            public func Arrow.Arrow() -> ref T;
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T> : Arrow<T>
        {
            nuint addr;
            Metadata<T> metadata;

            [IntrinsicFunction("Pointer.Deref()")]
            public func Arrow.Arrow() -> ref T;
        }

        [IntrinsicType("Str")]
        public struct Str
        {
            int8& buffer;
            nuint length;
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

        public struct Point
        {
            public Int32 x;
            public Int32 y;
        }
        """;

    [Fact]
    public void Reference_ArrowOperator_AccessesInnerMember_Succeeds()
    {
        var source = Prelude + """
            public func Test(Point& p) -> Int32
            {
                return p->x;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Pointer_ArrowOperator_AccessesInnerMember_Succeeds()
    {
        var source = Prelude + """
            public func Test(Point* p) -> Int32
            {
                return p->y;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void DynReference_ArrowOperator_AccessesInnerMember_Succeeds()
    {
        var source = Prelude + """
            public func Test(dyn Point& p) -> Int32
            {
                return p->x;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Reference_DotOperator_DoesNotAutoDereference_ReportsMH2014()
    {
        var source = Prelude + """
            public func Test(Point& p) -> Int32
            {
                return p.x;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH2014");
        Assert.Contains("x", diag.Message);
        Assert.Contains("Reference", diag.Message);
    }

    [Fact]
    public void Pointer_DotOperator_DoesNotAutoDereference_ReportsMH2014()
    {
        var source = Prelude + """
            public func Test(Point* p) -> Int32
            {
                return p.y;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH2014");
        Assert.Contains("y", diag.Message);
        Assert.Contains("Pointer", diag.Message);
    }
}
