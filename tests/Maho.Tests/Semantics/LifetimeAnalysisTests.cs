using Maho.Resolution.Ownership;
using Maho.Resolution;

namespace Maho.Tests;

public sealed class LifetimeAnalysisTests
{
    [Fact]
    public void Parameters_StartInitialized_InOwnershipBody()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func Read(int32 value) -> int32 { return value; }
            """);

        var function = Assert.Single(compilation.Context.FunctionSymbols, f => f.Name.Text == "Read");
        Assert.True(compilation.Context.TryGetOwnershipFacts(ResolutionContext.GetHandle(function), out var facts));
        var parameter = compilation.Context.GetParameterSymbol(Assert.Single(function.Parameters))!;
        Assert.Contains(facts!.Body.Entry.Statements, statement =>
            statement.Kind == OwnershipStatementKind.Assign && ReferenceEquals(statement.Place.Root, parameter));
        Assert.DoesNotContain(compilation.Diagnostics, d => d.Code == "MH1002");
    }

    [Fact]
    public void ImplicitMove_ReportsSecondUse()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Move<T>
            {
                [OperatorFunction("Move")]
                public func Move(T& dest, T& source) -> void;
            }

            public struct Resource : Move<Resource>
            {
                public func Move(Resource& dest, Resource& source) -> void {}
            }

            public func Consume(Resource resource) -> void {}

            public func Test()
            {
                Resource value;
                Consume(value);
                Consume(value);
            }
            """);

        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1001");
    }

    [Fact]
    public void ConditionalMove_ReportsPossibleSecondUse()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Move<T>
            {
                [OperatorFunction("Move")]
                public func Move(T& dest, T& source) -> void;
            }

            public struct Resource : Move<Resource>
            {
                public func Move(Resource& dest, Resource& source) -> void {}
            }

            public func Consume(Resource resource) -> void {}

            public func Test(bool choose)
            {
                Resource value;
                if (choose) Consume(value);
                Consume(value);
            }
            """);

        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1001");
    }

    [Fact]
    public void SafeAlias_PreventsMovingItsReferent()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Move<T>
            {
                [OperatorFunction("Move")]
                public func Move(T& dest, T& source) -> void;
            }

            public struct Resource : Move<Resource>
            {
                public func Move(Resource& dest, Resource& source) -> void {}
            }

            public func Consume(Resource resource) -> void {}

            public func Test(Resource value)
            {
                &Resource view = value;
                Consume(value);
                view;
            }
            """);

        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1003");
    }

    [Fact]
    public void SafeAlias_EndsAfterItsLastUse()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Move<T>
            {
                [OperatorFunction("Move")]
                public func Move(T& dest, T& source) -> void;
            }

            public struct Resource : Move<Resource>
            {
                public func Move(Resource& dest, Resource& source) -> void {}
            }

            public func Consume(Resource resource) -> void {}

            public func Test(Resource value)
            {
                &Resource view = value;
                view;
                Consume(value);
            }
            """);

        Assert.DoesNotContain(compilation.Diagnostics, d => d.Code == "MH1003");
    }

    [Fact]
    public void SafeReference_UsesIntrinsicDeref_AndKeepsReferentBorrowed()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Move<T>
            {
                [OperatorFunction("Move")]
                public func Move(T& dest, T& source) -> void;
            }

            public struct Resource : Move<Resource>
            {
                public func Move(Resource& dest, Resource& source) -> void {}
            }

            public func Consume(Resource resource) -> void {}

            public func ReadThroughReference(int32 value) -> int32
            {
                int32& reference = &value;
                return *reference;
            }

            public func ReferencePreventsMove(Resource value) -> void
            {
                Resource& reference = &value;
                Consume(value);
                *reference;
            }
            """);

        Assert.DoesNotContain(compilation.Diagnostics, d => d.Code == "MH2013");
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1003");
    }

    [Fact]
    public void AliasToValue_ImplicitlyCopiesTheReferent()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func CopyAlias(int32 value) -> int32
            {
                &int32 alias = value;
                int32 copy = alias;
                return copy;
            }

            public func CopyDeref(int32 value) -> int32
            {
                int32& reference = &value;
                int32 copy = *reference;
                return copy;
            }
            """);

        Assert.DoesNotContain(compilation.Diagnostics, diagnostic => diagnostic.Code is "MH1001" or "MH1003" or "MH1005");
    }

    [Fact]
    public void AliasToMoveOnlyValue_ReportsCopyErrorWithoutMovingReferent()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Move<T>
            {
                [OperatorFunction("Move")]
                public func Move(T& dest, T& source) -> void;
            }

            public struct Resource : Move<Resource>
            {
                public func Move(Resource& dest, Resource& source) -> void {}
            }

            public func CannotCopy(Resource value) -> void
            {
                &Resource alias = value;
                Resource copy = alias;
                alias;
            }
            """);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "MH1005");
        Assert.DoesNotContain(compilation.Diagnostics, diagnostic => diagnostic.Code == "MH1001");
    }

    [Fact]
    public void Reborrow_KeepsOriginalReferentBorrowed()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Move<T>
            {
                [OperatorFunction("Move")]
                public func Move(T& dest, T& source) -> void;
            }

            public struct Resource : Move<Resource>
            {
                public func Move(Resource& dest, Resource& source) -> void {}
            }

            public func Consume(Resource resource) -> void {}

            public func Test(Resource value)
            {
                &Resource first = value;
                &Resource second = first;
                Consume(value);
                second;
            }
            """);

        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1003");
    }

    [Fact]
    public void DestructorValue_GetsDropBeforeStorageDeath()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Destructor
            {
                [OperatorFunction("Destructor")]
                public func Drop(&this) -> void;
            }

            public struct Resource : Destructor
            {
                public func Drop(&this) -> void {}
            }

            public func Consume(Resource resource) -> void {}
            """);

        var function = Assert.Single(compilation.Context.FunctionSymbols, f => f.Name.Text == "Consume");
        Assert.True(compilation.Context.TryGetOwnershipFacts(ResolutionContext.GetHandle(function), out var facts));
        Assert.Contains(facts!.Body.Entry.Statements, statement => statement.Kind == OwnershipStatementKind.Drop);
    }

    [Fact]
    public void ReturningAliasOfLocal_ReportsEscape()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct Box { int32 value; }

            public func Escape() -> &Box
            {
                Box local;
                &Box alias = local;
                return alias;
            }
            """);

        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1004");
    }

    [Fact]
    public void PartialMove_CompositeStruct_AllowsIndividualFieldMove()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Move<T>
            {
                [OperatorFunction("Move")]
                public func Move(T& dest, T& source) -> void;
            }

            public struct Resource : Move<Resource>
            {
                public func Move(Resource& dest, Resource& source) -> void {}
            }

            public struct Pair
            {
                public Resource a;
                public Resource b;
            }

            public func Consume(Resource r) -> void {}

            public func Test() -> void
            {
                Pair p;
                Consume(p.a);
                Consume(p.b);
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void PartialMove_ReuseMovedField_ReportsMH1001()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Move<T>
            {
                [OperatorFunction("Move")]
                public func Move(T& dest, T& source) -> void;
            }

            public struct Resource : Move<Resource>
            {
                public func Move(Resource& dest, Resource& source) -> void {}
            }

            public struct Pair
            {
                public Resource a;
                public Resource b;
            }

            public func Consume(Resource r) -> void {}

            public func Test() -> void
            {
                Pair p;
                Consume(p.a);
                Consume(p.a);
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1001");
    }

    [Fact]
    public void PartialMove_WholeObjectMoveInvariant_ReportsMH1007()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Move<T>
            {
                [OperatorFunction("Move")]
                public func Move(T& dest, T& source) -> void;
            }

            public struct Resource : Move<Resource>
            {
                public func Move(Resource& dest, Resource& source) -> void {}
            }

            public struct Pair
            {
                public Resource a;
                public Resource b;
            }

            public func Consume(Resource r) -> void {}
            public func ConsumePair(Pair p) -> void {}

            public func Test() -> void
            {
                Pair p;
                Consume(p.a);
                ConsumePair(p);
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1007");
    }

    [Fact]
    public void PartialMove_Reinitialization_AllowsWholeObjectMove()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Move<T>
            {
                [OperatorFunction("Move")]
                public func Move(T& dest, T& source) -> void;
            }

            public struct Resource : Move<Resource>
            {
                public func Move(Resource& dest, Resource& source) -> void {}
            }

            public struct Pair
            {
                public Resource a;
                public Resource b;
            }

            public func Consume(Resource r) -> void {}
            public func ConsumePair(Pair p) -> void {}

            public func Test() -> void
            {
                Pair p;
                Resource r1;
                Resource r2;
                p.a = r1;
                p.b = r2;
                Consume(p.a);
                Resource replacement;
                p.a = replacement;
                ConsumePair(p);
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void MonolithicType_PartialMoveDisallowed_ReportsMH1006()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Move<T>
            {
                [OperatorFunction("Move")]
                public func Move(T& dest, T& source) -> void;
            }

            public struct Resource : Move<Resource>
            {
                public func Move(Resource& dest, Resource& source) -> void {}
            }

            public struct Monolithic : Move<Monolithic>
            {
                public Resource res;
                public func Move(Monolithic& dest, Monolithic& source) -> void {}
            }

            public func Consume(Resource r) -> void {}

            public func Test() -> void
            {
                Monolithic m;
                Consume(m.res);
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1006");
    }

    [Fact]
    public void DisjointFieldBorrows_DistinctFields_DoNotConflict()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct Pair
            {
                public int32 a;
                public int32 b;
            }

            public func Test() -> void
            {
                Pair p;
                mut &int32 refA = p.a;
                mut &int32 refB = p.b;
                refA = 10;
                refB = 20;
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void DropElaborator_EmitsConditionalDrop_WhenFieldConditionallyMoved()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Move<T>
            {
                [OperatorFunction("Move")]
                public func Move(T& dest, T& source) -> void;
            }

            public interface Destructor
            {
                [OperatorFunction("Destructor")]
                public func Drop(&this) -> void;
            }

            public struct Resource : Move<Resource>, Destructor
            {
                public func Move(Resource& dest, Resource& source) -> void {}
                public func Drop(&this) -> void {}
            }

            public struct Pair
            {
                public Resource a;
                public Resource b;
            }

            public func Consume(Resource r) -> void {}

            public func Test(bool cond) -> void
            {
                Pair p;
                Resource r1;
                Resource r2;
                p.a = r1;
                p.b = r2;
                if (cond)
                {
                    Consume(p.a);
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var function = Assert.Single(compilation.Context.FunctionSymbols, f => f.Name.Text == "Test");
        Assert.True(compilation.Context.TryGetOwnershipFacts(ResolutionContext.GetHandle(function), out var facts));

        var fieldA = Assert.Single(compilation.Context.FieldSymbols, f => f.Name.Text == "a");
        var fieldB = Assert.Single(compilation.Context.FieldSymbols, f => f.Name.Text == "b");
        var handleA = ResolutionContext.GetHandle(fieldA);
        var handleB = ResolutionContext.GetHandle(fieldB);

        var allDrops = facts!.Body.Blocks.SelectMany(b => b.Statements)
            .Where(s => s.Kind == OwnershipStatementKind.Drop)
            .ToList();

        // Field 'a' was conditionally moved in 'if (cond)', so on scope exit it is conditionally dropped
        Assert.Contains(allDrops, d => d.Conditional && d.Place.Projections.Any(p => p.Field == handleA));
        // Field 'b' was never moved, so on scope exit it is unconditionally dropped
        Assert.Contains(allDrops, d => !d.Conditional && d.Place.Projections.Any(p => p.Field == handleB));
    }

    [Fact]
    public void TemporaryMaterialization_MutThisReceiver_Succeeds()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct Counter
            {
                public int32 count;
                public func Increment(mut &this) -> void
                {
                    this.count = 42;
                }
            }

            public func MakeCounter() -> Counter
            {
                Counter c;
                return c;
            }

            public func Test() -> void
            {
                Counter c;
                c.Increment();
                MakeCounter().Increment();
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void TemporaryBorrow_EscapingToLocal_ReportsMH1008()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func Test() -> void
            {
                var addr = &42;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1008");
    }

    [Fact]
    public void TemporaryBorrow_EscapingReturn_ReportsMH1008()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func Test() -> &int32
            {
                return &42;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1008");
    }
}
