using System.Linq;
using Maho.Resolution;
using Xunit;

namespace Maho.Tests;

public sealed class ExplicitThisAndArrowTests
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
            public func Arrow(&this) -> &T;
        }

        public interface Deref<T>
        {
            [OperatorFunction("Dereference")]
            public func Deref(&this) -> &T;
        }

        [IntrinsicType("Reference")]
        public struct Reference<T> : Arrow<T>, Deref<T>
        {
            nuint raw;
            Metadata<T> metadata;

            [IntrinsicFunction("Reference.Deref()")]
            public func Deref.Deref(&this) -> &T;

            public func Arrow.Arrow(&this) -> &T
            {
                return *this;
            }

            public func Address(&this) -> nuint
            {
                return this.raw;
            }
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T> : Arrow<T>, Deref<T>
        {
            nuint addr;
            Metadata<T> metadata;

            [IntrinsicFunction("Pointer.Deref()")]
            public func Deref.Deref(&this) -> &T;

            public func Arrow.Arrow(&this) -> &T
            {
                return *this;
            }
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

            public func Sum(this) -> Int32
            {
                return this.x;
            }

            public func SumUnqualified(&this) -> Int32
            {
                return x;
            }

            public func GetX(&this) -> Int32
            {
                return this.x;
            }

            public func SetX(mut &this, Int32 val) -> Int32
            {
                this.x = val;
                return val;
            }

            public func SetXWithShadowing(mut &this, Int32 x) -> Int32
            {
                this.x = x;
                return x;
            }

            public func GetXFromRef(this&) -> Int32
            {
                return this->x;
            }

            public func GetYFromPtr(this*) -> Int32
            {
                return this->y;
            }

            public static func Create(Int32 xVal, Int32 yVal) -> Point
            {
                Point pt;
                pt.x = xVal;
                pt.y = yVal;
                return pt;
            }
        }
        """;

    [Fact]
    public void Receiver_Value_DirectDotAccess_Succeeds()
    {
        var source = Prelude + """
            public func Test(Point p) -> Int32
            {
                return p.Sum();
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Receiver_UnqualifiedMemberAccess_Succeeds()
    {
        var source = Prelude + """
            public func Test(Point p) -> Int32
            {
                return p.SumUnqualified();
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Receiver_Alias_DirectDotAccess_Succeeds()
    {
        var source = Prelude + """
            public func Test(Point p) -> Int32
            {
                return p.GetX();
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Receiver_MutAlias_DirectDotAccessAndMutation_Succeeds()
    {
        var source = Prelude + """
            public func Test(Point p) -> Int32
            {
                return p.SetX(42);
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Parameter_Shadows_Member_And_This_Disambiguates_Succeeds()
    {
        var source = Prelude + """
            public func Test(Point p) -> Int32
            {
                return p.SetXWithShadowing(100);
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Receiver_SafePointer_RequiresArrow_Succeeds()
    {
        var source = Prelude + """
            public func Test(Point p) -> Int32
            {
                return p.GetXFromRef();
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Receiver_RawPointer_RequiresArrow_Succeeds()
    {
        var source = Prelude + """
            public func Test(Point p) -> Int32
            {
                return p.GetYFromPtr();
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Static_Function_Without_This_Called_Via_Type_Succeeds()
    {
        var source = Prelude + """
            public func Test() -> Point
            {
                return Point.Create(10, 20);
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Arrow_Returning_Alias_SingleArrowAccess_Succeeds()
    {
        var source = Prelude + """
            public struct MyBox<T> : Arrow<T>
            {
                T value;

                public func Arrow.Arrow(&this) -> &T
                {
                    return this.value;
                }
            }

            public func Test(MyBox<Point> b) -> Int32
            {
                return b->x;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Arrow_Returning_Reference_DoubleArrowAccess_Succeeds()
    {
        var source = Prelude + """
            public interface ArrowRef<T>
            {
                [OperatorFunction("IndirectMemberAccess")]
                public func Arrow(&this) -> T&;
            }

            public struct RefWrapper<T> : ArrowRef<T>
            {
                T& target;

                public func ArrowRef.Arrow(&this) -> T&
                {
                    return this.target;
                }
            }

            public func TestDoubleArrow(RefWrapper<Point> w) -> Int32
            {
                return w->->x;
            }

            public func TestAccessWrapperDirectReferenceMember(RefWrapper<Point> w) -> nuint
            {
                return w->Address();
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Arrow_Returning_Reference_SingleArrowAccessToInnerMember_FailsWithMH2014()
    {
        var source = Prelude + """
            public interface ArrowRef<T>
            {
                [OperatorFunction("IndirectMemberAccess")]
                public func Arrow(&this) -> T&;
            }

            public struct RefWrapper<T> : ArrowRef<T>
            {
                T& target;

                public func ArrowRef.Arrow(&this) -> T&
                {
                    return this.target;
                }
            }

            public func TestSingleArrow(RefWrapper<Point> w) -> Int32
            {
                return w->x;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors, "Expected errors but none were reported!");
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2014");
    }

    [Fact]
    public void Receiver_AtNonZeroIndex_ReportsError()
    {
        var source = Prelude + """
            public struct TestStruct
            {
                public func Bad(Int32 a, &this) -> Int32
                {
                    return a;
                }
            }
            """;
        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors, "Expected errors but none were reported!");
    }

    [Fact]
    public void Calling_InstanceMethod_Statically_OnType_Fails()
    {
        var source = Prelude + """
            public func Test() -> Int32
            {
                return Point.GetX();
            }
            """;
        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors, "Expected errors but none were reported!");
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2019");
    }

    [Fact]
    public void Calling_StaticMethod_OnInstance_Fails()
    {
        var source = Prelude + """
            public func Test(Point p) -> Point
            {
                return p.Create(1, 2);
            }
            """;
        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors, "Expected errors but none were reported!");
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2020");
    }
}
