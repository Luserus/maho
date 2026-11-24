using Maho.Resolution;

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
    public void SplitBase_ThinReference_FailsWithMH0555()
    {
        var source = PreludeAndHierarchy + """
            public func Test(Derived& d) -> ()
            {
                MiddleB& refB = d;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0555" && d.Message.Contains("MiddleB"));
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
    public void SplitBase_MethodArgument_ThinFailsMH0555_DynSucceeds()
    {
        var thinSource = PreludeAndHierarchy + """
            public func ConsumeThin(MiddleB& b) -> () {}

            public func Test(Derived& d) -> ()
            {
                ConsumeThin(d);
            }
            """;

        var thinComp = Compilation.FromSource(thinSource);
        Assert.True(thinComp.HasErrors);
        Assert.Contains(thinComp.Diagnostics, d => d.Code == "MH0555");

        var dynSource = PreludeAndHierarchy + """
            public func ConsumeDyn(dyn MiddleB& b) -> () {}

            public func Test(Derived& d) -> ()
            {
                ConsumeDyn(d);
            }
            """;

        var dynComp = Compilation.FromSource(dynSource);
        Assert.False(dynComp.HasErrors, string.Join("\n", dynComp.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void SplitBase_AsCast_ThinFailsMH0555_DynSucceeds()
    {
        var thinSource = PreludeAndHierarchy + """
            public func Test(Derived& d) -> ()
            {
                var b = d as MiddleB&;
            }
            """;

        var thinComp = Compilation.FromSource(thinSource);
        Assert.True(thinComp.HasErrors);
        Assert.Contains(thinComp.Diagnostics, d => d.Code == "MH0555");

        var dynSource = PreludeAndHierarchy + """
            public func Test(Derived& d) -> ()
            {
                var b = d as dyn MiddleB&;
            }
            """;

        var dynComp = Compilation.FromSource(dynSource);
        Assert.False(dynComp.HasErrors, string.Join("\n", dynComp.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void ImplicitDyn_Interface_ResolvesAsDynSemanticType_OutsideGenerics()
    {
        var source = PreludeAndHierarchy + """
            public interface IGreetable
            {
                public func Greet(&this) -> Int32;
            }

            public struct Greeter : IGreetable
            {
                public func Greet(&this) -> Int32 { return 42; }
            }

            public func TestAlias(&IGreetable g) -> Int32
            {
                return g.Greet();
            }

            public func TestOwned(IGreetable g) -> Int32
            {
                return g.Greet();
            }
            """;

        var comp = Compilation.FromSource(source);
        Assert.False(comp.HasErrors, string.Join("\n", comp.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var greetableSym = comp.Context.TypeSymbols.First(t => t.Name.Text == "IGreetable");
        var testAliasSym = comp.Context.FunctionSymbols.First(f => f.Name.Text == "TestAlias");
        var paramAlias = comp.Context.GetParameterSymbol(testAliasSym.Parameters[0])!;
        Assert.True(paramAlias.Type.SpecialType is AliasSemanticType);
        var aliasInner = ((AliasSemanticType)paramAlias.Type.SpecialType).TargetType;
        Assert.True(aliasInner.SpecialType is DynSemanticType);

        var testOwnedSym = comp.Context.FunctionSymbols.First(f => f.Name.Text == "TestOwned");
        var paramOwned = comp.Context.GetParameterSymbol(testOwnedSym.Parameters[0])!;
        Assert.True(paramOwned.Type.SpecialType is DynSemanticType);

        // Verify struct base types is trait bound, NOT dyn
        var greeterSym = comp.Context.TypeSymbols.First(t => t.Name.Text == "Greeter");
        var ifaceBase = greeterSym.BaseTypes.First(b => b.Handle == ResolutionContext.GetHandle(greetableSym));
        Assert.False(ifaceBase.SpecialType is DynSemanticType);
    }

    [Fact]
    public void GenericConstraint_Interface_RemainsStaticTraitBound()
    {
        var source = PreludeAndHierarchy + """
            public interface IWorker
            {
                public func Work(&this) -> Int32;
            }

            public func Execute<T>(T item) -> Int32 where T : IWorker
            {
                return 1;
            }
            """;

        var comp = Compilation.FromSource(source);
        Assert.False(comp.HasErrors, string.Join("\n", comp.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var executeFn = comp.Context.FunctionSymbols.First(f => f.Name.Text == "Execute");
        var tParam = comp.Context.GenericParameterSymbols[executeFn.GenericParameters[0].ID];
        Assert.Single(tParam.Constraints);
        Assert.False(tParam.Constraints[0].SpecialType is DynSemanticType);
    }
}
