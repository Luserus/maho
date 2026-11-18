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
        Assert.DoesNotContain(compilation.Diagnostics, d => d.Code == "MH0401");
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

        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0400");
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

        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0400");
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

        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0402");
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

        Assert.DoesNotContain(compilation.Diagnostics, d => d.Code == "MH0402");
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
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0402");
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

        Assert.DoesNotContain(compilation.Diagnostics, diagnostic => diagnostic.Code is "MH0400" or "MH0402" or "MH0404");
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

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "MH0404");
        Assert.DoesNotContain(compilation.Diagnostics, diagnostic => diagnostic.Code == "MH0400");
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

        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0402");
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

        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0403");
    }
}
