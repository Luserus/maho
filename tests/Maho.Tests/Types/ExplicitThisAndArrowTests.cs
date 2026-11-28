namespace Maho.Tests;

public sealed class ExplicitThisAndArrowTests
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
        public struct Int32;
        [IntrinsicType("NUInt")]
        public struct nuint;

        [IntrinsicType("Slice")]
        public struct Slice<T, D : int = 1>;

        [IntrinsicType("Metadata")]
        public struct Metadata<T> where T : Layout;

        public interface Arrow<TReturn>
        {
            [OperatorFunction("IndirectMemberAccess")]
            func Arrow(this&) -> TReturn;
        }

        public interface Deref<TReturn>
        {
            [OperatorFunction("Dereference")]
            func Deref(this&) -> TReturn;
        }

        [IntrinsicType("Reference")]
        public struct Reference<T> : Arrow<T&>, Deref<T&> where T : Layout
        {
            nuint raw;
            Metadata<T> metadata;

            [IntrinsicFunction("Reference.Deref()")]
            func Deref.Deref(this&) -> T&;

            func Arrow.Arrow(this&) -> T&
            {
                return &(*(*this));
            }

            public func Address(this&) -> nuint
            {
                return this->raw;
            }
        }

        [IntrinsicType("MutableReference")]
        public struct MutableReference<T> : Arrow<T mut&>, Deref<T mut&> where T : Layout
        {
            nuint raw;
            Metadata<T> metadata;

            [IntrinsicFunction("MutableReference.Deref()")]
            func Deref.Deref(this&) -> T mut&;

            func Arrow.Arrow(this&) -> T mut&
            {
                return &(*(*this));
            }
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T> : Arrow<T&>, Deref<T&> where T : Layout
        {
            nuint addr;
            Metadata<T> metadata;

            [IntrinsicFunction("Pointer.Deref()")]
            func Deref.Deref(this&) -> T&;

            func Arrow.Arrow(this&) -> T&
            {
                return &(*(*this));
            }

            public func Address(this&) -> nuint
            {
                return this->addr;
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
            func Invoke(this&, TParams args) -> TReturn;
        }

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            func Callable.Invoke(this&, TParams args) -> TReturn;
        }

        public struct Point
        {
            public Int32 x;
            public Int32 y;

            public func Sum(this) -> Int32
            {
                return this.x;
            }

            public func SumUnqualified(this&) -> Int32
            {
                return x;
            }

            public func GetX(this&) -> Int32
            {
                return this->x;
            }

            public func SetX(this mut&, Int32 val) -> Int32
            {
                this->x = val;
                return val;
            }

            public func SetXWithShadowing(this mut&, Int32 x) -> Int32
            {
                this->x = x;
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

            // No receiver means this is implicitly static; `static` is optional syntax.
            public func Create(Int32 xVal, Int32 yVal) -> Point
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
    public void Receiverless_Function_IsImplicitlyStatic_AndCalledViaType_Succeeds()
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
    public void Arrow_Returning_Reference_SingleLevel_SingleArrowAccess_Succeeds()
    {
        var source = Prelude + """
            public struct MyBox<T> : Arrow<T&>
            {
                T value;

                func Arrow.Arrow(this&) -> T&
                {
                    return &this->value;
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
    public void Arrow_Returning_NestedPointer_DoubleArrowAccess_Succeeds()
    {
        var source = Prelude + """
            public struct PtrWrapper<T> : Arrow<T*> where T : Layout
            {
                T* target;

                func Arrow.Arrow(this&) -> T*
                {
                    return this->target;
                }
            }

            public func TestDoubleArrow(PtrWrapper<Point> w) -> Int32
            {
                return w->->x;
            }

            public func TestAccessWrapperDirectPointerMember(PtrWrapper<Point> w) -> nuint
            {
                return w->Address();
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Arrow_Returning_NestedPointer_SingleArrowAccessToInnerMember_FailsWithMH2014()
    {
        var source = Prelude + """
            public struct PtrWrapper<T> : Arrow<T*> where T : Layout
            {
                T* target;

                func Arrow.Arrow(this&) -> T*
                {
                    return this->target;
                }
            }

            public func TestSingleArrow(PtrWrapper<Point> w) -> Int32
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
