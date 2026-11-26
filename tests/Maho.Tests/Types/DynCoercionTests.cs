using Maho.Resolution;

namespace Maho.Tests;

public sealed class DynCoercionTests
{
    private const string PreludeAndHierarchy = """
        intrinsic attribute IntrinsicType(&Str name);
        intrinsic attribute IntrinsicFunction(&Str signature);
        intrinsic attribute OperatorFunction(&Str name);

        [IntrinsicType("Layout")]
        public interface Layout;
        [IntrinsicType("Sized")]
        public interface Sized : Layout;
        [IntrinsicType("Unsized")]
        public interface Unsized : Layout;

        [IntrinsicType("UInt8")]
        public struct uint8;
        [IntrinsicType("Int8")]
        public struct int8;
        [IntrinsicType("Int32")]
        public struct Int32;
        [IntrinsicType("NUInt")]
        public struct nuint;

        public interface Arrow<TReturn>
        {
            [OperatorFunction("IndirectMemberAccess")]
            func Arrow(&this) -> TReturn;
        }

        public interface Deref<TReturn>
        {
            [OperatorFunction("Dereference")]
            func Deref(&this) -> TReturn;
        }

        [IntrinsicType("Metadata")]
        public struct Metadata<T> where T : Layout;

        [IntrinsicType("Reference")]
        public struct Reference<T> : Arrow<&T>, Deref<&T> where T : Layout
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

        [IntrinsicType("Slice")]
        public struct Slice<T, D : int = 1>;

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
    public void BareInterface_AndIndirection_WithoutDyn_FailsWithMH0583_DynAliasAndRefSucceed()
    {
        var bareSource = PreludeAndHierarchy + """
            public interface IGreetable
            {
                func Greet(&this) -> Int32;
            }

            public func TestAlias(&IGreetable g) -> Int32
            {
                return g.Greet();
            }
            """;

        var bareComp = Compilation.FromSource(bareSource);
        Assert.True(bareComp.HasErrors);
        Assert.Contains(bareComp.Diagnostics, d => d.Code == "MH0583");

        var dynSource = PreludeAndHierarchy + """
            public interface IGreetable
            {
                func Greet(&this) -> Int32;
            }

            public struct Greeter : IGreetable
            {
                public func Greet(&this) -> Int32 { return 42; }
            }

            public func TestDynAlias(dyn &IGreetable g) -> Int32
            {
                return g.Greet();
            }

            public func TestDynRef(dyn IGreetable& g) -> Int32
            {
                return g->Greet();
            }

            public func TestDynRefDeref(dyn IGreetable& g) -> Int32
            {
                return (*g).Greet();
            }
            """;

        var comp = Compilation.FromSource(dynSource);
        Assert.False(comp.HasErrors, string.Join("\n", comp.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var greetableSym = comp.Context.TypeSymbols.First(t => t.Name.Text == "IGreetable");
        var testAliasSym = comp.Context.FunctionSymbols.First(f => f.Name.Text == "TestDynAlias");
        var paramAlias = comp.Context.GetParameterSymbol(testAliasSym.Parameters[0])!;
        var dynAlias = Assert.IsType<DynSemanticType>(paramAlias.Type.SpecialType);
        Assert.IsType<AliasSemanticType>(dynAlias.TargetType.SpecialType);
        Assert.True(paramAlias.Type.IsSized(comp.Context));

        var testRefSym = comp.Context.FunctionSymbols.First(f => f.Name.Text == "TestDynRef");
        var paramRef = comp.Context.GetParameterSymbol(testRefSym.Parameters[0])!;
        var dynRef = Assert.IsType<DynSemanticType>(paramRef.Type.SpecialType);
        Assert.IsType<ReferenceSemanticType>(dynRef.TargetType.SpecialType);
        Assert.True(paramRef.Type.IsSized(comp.Context));

        // Verify struct base type is trait bound, NOT dyn
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
                func Work(&this) -> Int32;
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
