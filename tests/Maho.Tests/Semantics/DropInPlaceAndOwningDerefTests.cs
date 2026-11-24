using System.Linq;
using Maho.Resolution;
using Maho.Resolution.Ownership;
using Xunit;

namespace Maho.Tests;

public sealed class DropInPlaceAndOwningDerefTests
{
    private const string Prelude = CompilerTestBed.FullPrelude + """
        public interface OwningDeref<TReturn>
        {
            [OperatorFunction("OwningDeref")]
            public func OwningDeref(mut &this) -> TReturn;
        }

        public interface Destructor
        {
            [OperatorFunction("Destructor")]
            public func Drop(&this) -> ();
        }

        public struct Box<T> : Destructor, Deref<T&>, OwningDeref<mut &T>
        {
            public T& ptr;

            public func Deref.Deref(&this) -> T&
            {
                return this.ptr;
            }

            public func OwningDeref.OwningDeref(mut &this) -> mut &T
            {
                return this.ptr;
            }

            public func Destructor.Drop(&this) -> ()
            {
            }
        }
        """;

    [Fact]
    public void MultidimensionalFixedArray_SyntaxAndTypeResolution_Succeeds()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func Test() -> ()
            {
                int32[5, 4] grid2D;
                int32[2, 3, 4] grid3D;
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var fn = Assert.Single(compilation.Context.FunctionSymbols, f => f.Name.Text == "Test");
        var var2D = Assert.Single(fn.LocalVariables, v => v.Name.Text == "grid2D");
        var fix2D = Assert.IsType<FixedArraySemanticType>(var2D.Type.SpecialType);
        Assert.Equal(2, fix2D.Dimension);
        Assert.Equal(2, fix2D.Sizes.Count);

        var var3D = Assert.Single(fn.LocalVariables, v => v.Name.Text == "grid3D");
        var fix3D = Assert.IsType<FixedArraySemanticType>(var3D.Type.SpecialType);
        Assert.Equal(3, fix3D.Dimension);
        Assert.Equal(3, fix3D.Sizes.Count);
    }

    [Fact]
    public void Box_PartialMove_DropsRemainingInitializedFieldInPlace_ThenDropsBox()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct Resource : Destructor
            {
                public int32 id;
                public func Destructor.Drop(&this) -> () {}
            }

            public struct Pair
            {
                public Resource first;
                public Resource second;
            }

            public func Consume(Resource r) -> () {}

            public func Test(Box<Pair> b) -> ()
            {
                var r1 = b.first;
                Consume(r1);
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var fn = Assert.Single(compilation.Context.FunctionSymbols, f => f.Name.Text == "Test");
        Assert.True(compilation.Context.TryGetOwnershipFacts(ResolutionContext.GetHandle(fn), out var facts));

        var fieldFirst = Assert.Single(compilation.Context.FieldSymbols, f => f.Name.Text == "first");
        var fieldSecond = Assert.Single(compilation.Context.FieldSymbols, f => f.Name.Text == "second");
        var handleFirst = ResolutionContext.GetHandle(fieldFirst);
        var handleSecond = ResolutionContext.GetHandle(fieldSecond);

        var allDrops = facts!.Body.Blocks.SelectMany(b => b.Statements)
            .Where(s => s.Kind == OwnershipStatementKind.Drop)
            .ToList();

        // b.*.first was moved out, so it must NOT be dropped!
        Assert.DoesNotContain(allDrops, d => d.Place.Projections.Any(p => p.Field == handleFirst));
        // b.*.second remained initialized, so it MUST be dropped in place!
        Assert.Contains(allDrops, d => d.Place.Projections.Any(p => p.Field == handleSecond));
        // b itself (the container) MUST be dropped (freeing memory)!
        Assert.Contains(allDrops, d => d.Place.Projections.Count == 0 && d.Place.Root.Name.Text == "b");
    }

    [Fact]
    public void Box_MovingPartiallyMovedBox_ReportsMH1007()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct Resource : Destructor
            {
                public int32 id;
                public func Destructor.Drop(&this) -> () {}
            }

            public struct Pair
            {
                public Resource first;
                public Resource second;
            }

            public func ConsumeResource(Resource r) -> () {}
            public func ConsumeBox(Box<Pair> b) -> () {}

            public func Test(Box<Pair> b) -> ()
            {
                var r1 = b.first;
                ConsumeResource(r1);
                ConsumeBox(b);
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1007");
    }
}
