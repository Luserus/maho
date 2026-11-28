using Maho.Resolution;
using Maho.Resolution.Layout;

namespace Maho.Tests;

public sealed class C3LinearizationTests
{
    [Fact]
    public void DiamondInheritance_LinearizesAndDeduplicatesBaseTypes()
    {
        var compilation = Compilation.FromSource("""
            [IntrinsicType("Layout")]
            public interface Layout;
            [IntrinsicType("Sized")]
            public interface Sized : Layout;
            [IntrinsicType("Unsized")]
            public interface Unsized : Layout;

            intrinsic attribute IntrinsicType(Str& name);

            [IntrinsicType("UInt8")]
            public struct uint8;
            [IntrinsicType("Int8")]
            public struct int8;
            [IntrinsicType("NUInt")]
            public struct nuint;

            [IntrinsicType("Slice")]
            public struct Slice<T, D : int = 1>;

            [IntrinsicType("Metadata")]
            public struct Metadata<T> where T : Layout;

            [IntrinsicType("Reference")]
            public struct Reference<T> where T : Layout { nuint raw; Metadata<T> metadata; }

            [IntrinsicType("Str")]
            public struct Str { uint8[] data; }

            [IntrinsicType("ValueType")]
            public struct ValType;

            [IntrinsicType("Struct")]
            public struct Struct : ValType;

            [IntrinsicType("Int32")]
            public struct Int32;

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
            """);

        var errors = string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));
        Assert.False(compilation.HasErrors);

        var derivedSym = compilation.Context.TypeSymbols.FirstOrDefault(t => t.Name.Text == "Derived");
        Assert.NotNull(derivedSym);

        var baseNames = derivedSym.LinearizedBases
            .Skip(1)
            .Select(bh => compilation.Context.GetTypeSymbol(bh)?.Name.Text ?? bh.ToString())
            .ToList();

        // Must contain MiddleA, MiddleB, Base, each exactly once, in C3 order
        Assert.Equal("MiddleA", baseNames[0]);
        Assert.Equal("MiddleB", baseNames[1]);
        Assert.Equal("Base", baseNames[2]);

        // Base must appear only once in the base types list
        Assert.Single(baseNames, n => n == "Base");

        // Now verify topological field sequence and contiguity
        var derivedHandle = ResolutionContext.GetHandle(derivedSym);
        var fieldSequence = TypeContiguityAnalyzer.GetFieldSequence(derivedHandle, compilation.Context);
        Assert.Equal(4, fieldSequence.Count);

        var middleASym = compilation.Context.TypeSymbols.First(t => t.Name.Text == "MiddleA");
        var middleBSym = compilation.Context.TypeSymbols.First(t => t.Name.Text == "MiddleB");
        var baseSym = compilation.Context.TypeSymbols.First(t => t.Name.Text == "Base");

        var middleAHandle = ResolutionContext.GetHandle(middleASym);
        var middleBHandle = ResolutionContext.GetHandle(middleBSym);
        var baseHandle = ResolutionContext.GetHandle(baseSym);

        // MiddleA is contiguous
        Assert.True(TypeContiguityAnalyzer.IsBaseContiguous(derivedHandle, middleAHandle, compilation.Context));

        // Base is contiguous
        Assert.True(TypeContiguityAnalyzer.IsBaseContiguous(derivedHandle, baseHandle, compilation.Context));

        // MiddleB is SPLIT (its Base.id is followed by MiddleA.a, not MiddleB.b)
        Assert.False(TypeContiguityAnalyzer.IsBaseContiguous(derivedHandle, middleBHandle, compilation.Context));
    }

    [Fact]
    public void InconsistentInheritanceOrder_ReportsMH0539()
    {
        var compilation = Compilation.FromSource("""
            public struct A;
            public struct B;
            public struct X : A, B;
            public struct Y : B, A;
            public struct Z : X, Y;
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0539" && d.Message.Contains('Z'));
    }
}
