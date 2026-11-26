namespace Maho.Tests;

public sealed class IndirectMemberAccessTests
{
    private const string Prelude = """
        [IntrinsicType("Layout")]
        public interface Layout;
        [IntrinsicType("Sized")]
        public interface Sized : Layout;
        [IntrinsicType("Unsized")]
        public interface Unsized : Layout;

        intrinsic attribute IntrinsicType(&Str name);
        intrinsic attribute IntrinsicFunction(&Str signature);
        intrinsic attribute OperatorFunction(&Str name);

        [IntrinsicType("UInt8")]
        public struct uint8;
        [IntrinsicType("Int8")]
        public struct int8;
        [IntrinsicType("Int32")]
        public struct Int32;
        [IntrinsicType("NUInt")]
        public struct nuint;

        [IntrinsicType("Slice")]
        public struct Slice<T, D : int = 1>;

        [IntrinsicType("Metadata")]
        public struct Metadata<T> where T : Layout;

        public interface Deref<T> where T : Layout
        {
            [OperatorFunction("Dereference")]
            func Deref(&this) -> &T;
        }

        public interface Arrow<T> where T : Layout
        {
            [OperatorFunction("IndirectMemberAccess")]
            func Arrow(&this) -> &T;
        }

        [IntrinsicType("Reference")]
        public struct Reference<T> : Arrow<T>, Deref<T> where T : Layout
        {
            nuint raw;
            Metadata<T> metadata;

            [IntrinsicFunction("Reference.Deref()")]
            func Deref.Deref(&this) -> &T;

            func Arrow.Arrow(&this) -> &T
            {
                return *this;
            }
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T> : Arrow<T>, Deref<T> where T : Layout
        {
            nuint addr;
            Metadata<T> metadata;

            [IntrinsicFunction("Pointer.Deref()")]
            func Deref.Deref(&this) -> &T;

            func Arrow.Arrow(&this) -> &T
            {
                return *this;
            }
        }

        [IntrinsicType("Str")]
        public struct Str
        {
            uint8[] data;
        }

        [IntrinsicType("ValueType")]
        public struct ValType;

        [IntrinsicType("Struct")]
        public struct Struct : ValType;

        public interface Callable<TReturn, TParams...>
        {
            [OperatorFunction("Callable")]
            func Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            func Callable.Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Delegate")]
        public struct Delegate<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            ValType* env;
            [IntrinsicFunction("Delegate.Invoke(TParams...)")]
            func Callable.Invoke(&this, TParams args) -> TReturn;
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
