using Maho.Resolution;
using Maho.Syntax;

namespace Maho.Tests;

public sealed class DerivedInterfaceAndOverrideTests
{
    private const string Prelude = CompilerTestBed.FullPrelude;

    [Fact]
    public void UnsealedType_DefaultInheritance_DoesNotRebindInterfaceGenericArgs()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public interface IAdd<TResult, TLeft, TRight>
            {
                [OperatorFunction("Addition")]
                func Add(TLeft lhs, TRight rhs) -> TResult;
            }

            public unsealed struct Base : IAdd<Base, Base, Base>
            {
                public int32 val;

                func IAdd.Add(Base lhs, Base rhs) -> Base
                {
                    return new Base { val = lhs.val };
                }
            }

            public struct Child : Base;

            public func Check() -> ()
            {
                return ();
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics));
        var ctx = compilation.Context!;
        var childSym = Assert.Single(ctx.TypeSymbols, t => t.Name.Text == "Child");
        var childHandle = ResolutionContext.GetHandle(childSym);

        var childConfs = new List<InterfaceConformance>();
        ctx.CollectConformancesForTarget(childHandle, childConfs);
        Assert.DoesNotContain(childConfs, c => c.InheritedFromTypeHandle is not null);
    }

    [Fact]
    public void DerivedMarker_ZeroFieldSubclass_SynthesizesReboundConformanceAndInheritsBaseMethod()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public interface IAdd<TResult, TLeft, TRight>
            {
                [OperatorFunction("Addition")]
                func Add(TLeft lhs, TRight rhs) -> TResult;
            }

            public unsealed struct Base : IAdd<derived Base, derived Base, derived Base>
            {
                public int32 val;

                func IAdd.Add(Base lhs, Base rhs) -> Base
                {
                    return new Base { val = lhs.val };
                }
            }

            public struct EmptyChild : Base;

            public func AddTwo<T>(T a, T b) -> T where T : IAdd<T, T, T>
            {
                return a + b;
            }

            public func Test(EmptyChild a, EmptyChild b) -> EmptyChild
            {
                EmptyChild c = a + b;
                return AddTwo::<EmptyChild>(c, b);
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics));
        var ctx = compilation.Context!;
        var baseSym = Assert.Single(ctx.TypeSymbols, t => t.Name.Text == "Base");
        var childSym = Assert.Single(ctx.TypeSymbols, t => t.Name.Text == "EmptyChild");
        var addSym = Assert.Single(ctx.TypeSymbols, t => t.Name.Text == "IAdd");

        var childConfs = new List<InterfaceConformance>();
        ctx.CollectConformancesForTarget(ResolutionContext.GetHandle(childSym), childConfs);
        var synth = Assert.Single(childConfs, c => c.InterfaceHandle == ResolutionContext.GetHandle(addSym));
        Assert.Equal(ResolutionContext.GetHandle(baseSym), synth.InheritedFromTypeHandle);
        var gen = Assert.IsType<GenericSemanticType>(synth.InterfaceType.SpecialType);
        Assert.All(gen.TypeArguments, arg => Assert.Equal(ResolutionContext.GetHandle(childSym), arg.Handle));
    }

    [Fact]
    public void DerivedMarker_PartialMarking_OnlyRebindsMarkedPositions()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public interface IMix<TFirst, TSecond>
            {
                func Mix(this&, TFirst a, TSecond& b) -> int32;
            }

            public unsealed struct Base : IMix<Base, derived Base>
            {
                public int32 v;

                public func Mix(this&, Base a, Base& b) -> int32
                {
                    return this->v;
                }
            }

            public struct Child : Base
            {
                public int32 extra;
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics));
        var ctx = compilation.Context!;
        var baseSym = Assert.Single(ctx.TypeSymbols, t => t.Name.Text == "Base");
        var childSym = Assert.Single(ctx.TypeSymbols, t => t.Name.Text == "Child");
        var mixSym = Assert.Single(ctx.TypeSymbols, t => t.Name.Text == "IMix");

        var childConfs = new List<InterfaceConformance>();
        ctx.CollectConformancesForTarget(ResolutionContext.GetHandle(childSym), childConfs);
        var synth = Assert.Single(childConfs, c => c.InterfaceHandle == ResolutionContext.GetHandle(mixSym));
        var gen = Assert.IsType<GenericSemanticType>(synth.InterfaceType.SpecialType);
        Assert.Equal(ResolutionContext.GetHandle(baseSym), gen.TypeArguments[0].Handle);
        Assert.Equal(ResolutionContext.GetHandle(childSym), gen.TypeArguments[1].Handle);
    }

    [Fact]
    public void DerivedMarker_FieldAddingSubclass_AllowsContravariantRefInputsOnNonSelfReturn()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public interface IEq<TRhs>
            {
                func Eq(this&, TRhs& other) -> bool;
            }

            public unsealed struct Base : IEq<derived Base>
            {
                public int32 id;

                public func Eq(this&, Base& other) -> bool
                {
                    return true;
                }
            }

            public struct FieldChild : Base
            {
                public int32 tag;
            }

            public func AreEqual<T>(T& a, T& b) -> bool where T : IEq<T>
            {
                return a->Eq(b);
            }

            public func Test(FieldChild& a, FieldChild& b) -> bool
            {
                return AreEqual::<FieldChild>(a, b);
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics));
    }

    [Fact]
    public void DerivedMarker_FieldAddingSubclass_WithoutOverrideOnSelfReturn_ReportsMH0594()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public interface ICloneable<TSelf>
            {
                func Clone(this&) -> TSelf;
            }

            public unsealed struct Base : ICloneable<derived Base>
            {
                public int32 id;

                public virtual func Clone(this&) -> Base
                {
                    return new Base { id = this->id };
                }
            }

            public struct FieldChild : Base
            {
                public int32 extra;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0594");
    }

    [Fact]
    public void DerivedMarker_MultiLevel_IntermediateAddsFields_LeafWithoutOverride_ReportsMH0594()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public interface ICloneable<TSelf>
            {
                func Clone(this&) -> TSelf;
            }

            public unsealed struct Base : ICloneable<derived Base>
            {
                public int32 id;

                public virtual func Clone(this&) -> Base
                {
                    return new Base { id = this->id };
                }
            }

            public unsealed struct Mid : Base
            {
                public int32 extra;
            }

            public struct Leaf : Mid;
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0594");
    }

    [Fact]
    public void DerivedMarker_FieldAddingSubclass_WithOverride_Succeeds()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public interface ICloneable<TSelf>
            {
                func Clone(this&) -> TSelf;
            }

            public unsealed struct Base : ICloneable<derived Base>
            {
                public int32 id;

                public virtual func Clone(this&) -> Base
                {
                    return new Base { id = this->id };
                }
            }

            public struct FieldChild : Base
            {
                public int32 extra;

                public override func Clone(this&) -> FieldChild
                {
                    return new FieldChild { id = this->id, extra = this->extra };
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics));
    }

    [Fact]
    public void OverrideMethod_ValidationErrors_ReportMH0595()
    {
        var nonVirtualCompilation = Compilation.FromSource(Prelude + """
            public unsealed struct Base
            {
                public func Foo(this&) -> int32 { return 1; }
            }
            public struct Child : Base
            {
                public override func Foo(this&) -> int32 { return 2; }
            }
            """);
        Assert.Contains(nonVirtualCompilation.Diagnostics, d => d.Code == "MH0595");

        var missingBaseCompilation = Compilation.FromSource(Prelude + """
            public unsealed struct Base;
            public struct Child : Base
            {
                public override func Missing(this&) -> int32 { return 2; }
            }
            """);
        Assert.Contains(missingBaseCompilation.Diagnostics, d => d.Code == "MH0595");

        var badReturnCompilation = Compilation.FromSource(Prelude + """
            public unsealed struct Base
            {
                public virtual func Foo(this&) -> int32 { return 1; }
            }
            public struct Child : Base
            {
                public override func Foo(this&) -> bool { return true; }
            }
            """);
        Assert.Contains(badReturnCompilation.Diagnostics, d => d.Code == "MH0595");

        var staticOverrideCompilation = Compilation.FromSource(Prelude + """
            public unsealed struct Base;
            public struct Child : Base
            {
                public static override func Foo() -> int32 { return 2; }
            }
            """);
        Assert.Contains(staticOverrideCompilation.Diagnostics, d => d.Code == "MH0595");
    }

    [Fact]
    public void DerivedMarker_InvalidPlacementOrMismatchedTarget_ReportsMH0595()
    {
        var mismatchedTarget = Compilation.FromSource(Prelude + """
            public interface IEq<TRhs> { func Eq(this&, TRhs& r) -> bool; }
            public struct Other;
            public unsealed struct Base : IEq<derived Other>
            {
                public func Eq(this&, Base& r) -> bool { return true; }
            }
            """);
        Assert.Contains(mismatchedTarget.Diagnostics, d => d.Code == "MH0595");

        var inLocalAndField = Compilation.FromSource(Prelude + """
            public interface IBox<T>;
            public struct Holder : IBox<Holder>
            {
                public IBox<derived Holder> field;
            }
            public func BadLocal() -> Unit
            {
                IBox<derived Holder> local;
                return ();
            }
            """);
        Assert.Contains(inLocalAndField.Diagnostics, d => d.Code == "MH0595");

        var inExtension = Compilation.FromSource(Prelude + """
            public interface IEq<TRhs>;
            public struct Target;
            public extension Target : IEq<derived Target> { }
            """);
        Assert.Contains(inExtension.Diagnostics, d => d.Code == "MH0595");

        var inNonInterfaceBase = Compilation.FromSource(Prelude + """
            public unsealed struct GenBase<T>;
            public struct Child : GenBase<derived Child>;
            """);
        Assert.Contains(inNonInterfaceBase.Diagnostics, d => d.Code == "MH0595");
    }

    [Fact]
    public void ContextualDerived_RemainsValidTypeAndVariableIdentifier()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public interface IWrap<T>;
            public struct derived : IWrap<derived>
            {
                public int32 val;
            }

            public func UseDerived(derived& derived) -> IWrap<derived>&
            {
                return derived;
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics));
    }

    [Fact]
    public void DerivedMarker_GenericBaseWithConcreteInheritance_SubstitutesTypeArgsAndDerivedSelf()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public interface IEq<TRhs>
            {
                func Eq(this&, TRhs& other) -> bool;
            }

            public unsealed struct Boxed<T> : IEq<derived Boxed<T>>
            {
                public T item;

                public func Eq(this&, Boxed<T>& other) -> bool
                {
                    return true;
                }
            }

            public struct IntBox : Boxed<int32>
            {
                public int32 extra;
            }

            public func Check(IntBox& a, IntBox& b) -> bool
            {
                return a->Eq(b);
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics));
        var ctx = compilation.Context!;
        var intBoxSym = Assert.Single(ctx.TypeSymbols, t => t.Name.Text == "IntBox");
        var eqSym = Assert.Single(ctx.TypeSymbols, t => t.Name.Text == "IEq");

        var confs = new List<InterfaceConformance>();
        ctx.CollectConformancesForTarget(ResolutionContext.GetHandle(intBoxSym), confs);
        var synth = Assert.Single(confs, c => c.InterfaceHandle == ResolutionContext.GetHandle(eqSym));
        var gen = Assert.IsType<GenericSemanticType>(synth.InterfaceType.SpecialType);
        Assert.Equal(ResolutionContext.GetHandle(intBoxSym), Assert.Single(gen.TypeArguments).Handle);
    }
}
