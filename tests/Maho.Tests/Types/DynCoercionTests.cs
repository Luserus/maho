namespace Maho.Tests;

public sealed class DynCoercionTests
{
    private const string PreludeAndHierarchy = """
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

        public struct Base
        {
            public Int32 id;
        }

        public struct MiddleA : Base
        {
            public Int32 a;
        }

        public struct MiddleB : Base
        {
            public Int32 b;
        }

        public struct Derived : MiddleA, MiddleB
        {
            public Int32 d;
        }
        """;

    [Fact]
    public void ContiguousBase_ThinReference_Succeeds()
    {
        var source = PreludeAndHierarchy + """
            public func Test(Derived& d) -> MiddleA&
            {
                MiddleA& refA = d;
                return refA;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void SplitBase_ThinReference_FailsWithMH0320()
    {
        var source = PreludeAndHierarchy + """
            public func Test(Derived& d) -> Unit
            {
                MiddleB& refB = d;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0320" && d.Message.Contains("MiddleB"));
    }

    [Fact]
    public void SplitBase_DynReference_Succeeds()
    {
        var source = PreludeAndHierarchy + """
            public func Test(Derived& d) -> dyn MiddleB&
            {
                dyn MiddleB& refB = d;
                return refB;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void SplitBase_MethodArgument_ThinFailsMH0320_DynSucceeds()
    {
        var thinSource = PreludeAndHierarchy + """
            public func ConsumeThin(MiddleB& b) -> Unit {}

            public func Test(Derived& d) -> Unit
            {
                ConsumeThin(d);
            }
            """;

        var thinComp = Compilation.FromSource(thinSource);
        Assert.True(thinComp.HasErrors);
        Assert.Contains(thinComp.Diagnostics, d => d.Code == "MH0320");

        var dynSource = PreludeAndHierarchy + """
            public func ConsumeDyn(dyn MiddleB& b) -> Unit {}

            public func Test(Derived& d) -> Unit
            {
                ConsumeDyn(d);
            }
            """;

        var dynComp = Compilation.FromSource(dynSource);
        Assert.False(dynComp.HasErrors, string.Join("\n", dynComp.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void SplitBase_AsCast_ThinFailsMH0320_DynSucceeds()
    {
        var thinSource = PreludeAndHierarchy + """
            public func Test(Derived& d) -> Unit
            {
                var b = d as MiddleB&;
            }
            """;

        var thinComp = Compilation.FromSource(thinSource);
        Assert.True(thinComp.HasErrors);
        Assert.Contains(thinComp.Diagnostics, d => d.Code == "MH0320");

        var dynSource = PreludeAndHierarchy + """
            public func Test(Derived& d) -> Unit
            {
                var b = d as dyn MiddleB&;
            }
            """;

        var dynComp = Compilation.FromSource(dynSource);
        Assert.False(dynComp.HasErrors, string.Join("\n", dynComp.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }
}
