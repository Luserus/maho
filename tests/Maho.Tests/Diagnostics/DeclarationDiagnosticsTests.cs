using Maho.Diagnostics;
using Maho.Resolution;
using Maho.Syntax;

namespace Maho.Tests;

public sealed class DeclarationDiagnosticsTests
{
    [Fact]
    public void DuplicateType_NonPartial_EmitsMH0530()
    {
        var compilation = Compilation.FromSource("""
            public struct Foo;
            public struct Foo;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0530");
        Assert.Contains("type 'Foo' is already declared in this scope", diag.Message);
        Assert.Equal(2, diag.Labels.Count);
        Assert.Equal(DiagnosticLabelStyle.Primary, diag.Labels[0].Style);
        Assert.Equal(DiagnosticLabelStyle.Secondary, diag.Labels[1].Style);
    }

    [Fact]
    public void DuplicateType_InNamespace_NonPartial_EmitsMH0530()
    {
        var compilation = Compilation.FromSource("""
            namespace Sample
            {
                public struct Point;
                public struct Point;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0530");
        Assert.Contains("Point", diag.Message);
    }

    [Fact]
    public void DuplicateType_Nested_NonPartial_EmitsMH0530()
    {
        var compilation = Compilation.FromSource("""
            public struct Outer
            {
                public struct Inner;
                public struct Inner;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0530");
        Assert.Contains("Inner", diag.Message);
    }

    [Fact]
    public void DuplicateType_MixedPartialAndNonPartial_EmitsMH0530()
    {
        var compilation = Compilation.FromSource("""
            public partial struct Bar;
            public struct Bar;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0530");
        Assert.Contains("Bar", diag.Message);
    }

    [Fact]
    public void DuplicateType_AliasAndTypeSameName_EmitsMH0530()
    {
        var compilation = Compilation.FromSource("""
            namespace Sample
            {
                public struct Target;
                public struct Item;
                using Item = Target;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0530");
        Assert.Contains("Item", diag.Message);
    }

    [Fact]
    public void PartialType_MultipleDeclarations_SucceedsWithoutErrors()
    {
        var compilation = Compilation.FromSource($$"""
            {{CompilerTestBed.FullPrelude}}
            public partial struct Entity;
            public partial struct Entity;
            public partial struct Entity;

            public struct User
            {
                public Entity entity;
            }
            """);

        Assert.False(compilation.HasErrors);
        Assert.Empty(compilation.Diagnostics);

        // Verify that referencing Entity resolves correctly
        var user = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.ToString() == "User");
        var field = Assert.Single(compilation.Context.FieldSymbols, f => f.Name.ToString() == "entity");
        Assert.True(field.Type.IsResolved);
    }

    [Fact]
    public void PartialType_ConflictingKinds_EmitsMH0531()
    {
        var compilation = Compilation.FromSource("""
            public partial struct Conflict;
            public partial enum Conflict;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0531");
        Assert.Contains("conflicting type kinds", diag.Message);
    }

    [Fact]
    public void PartialFunction_ZeroBodies_SucceedsWithoutWarningsOrErrors()
    {
        var compilation = Compilation.FromSource($$"""
            {{CompilerTestBed.FullPrelude}}

            public partial func DoWork() -> ();
            public partial func DoWork() -> ();
            public partial func DoWork() -> ();
            """);

        Assert.False(compilation.HasErrors);
        Assert.Empty(compilation.Diagnostics);
    }

    [Fact]
    public void PartialFunction_OneBody_SucceedsWithoutErrors()
    {
        var compilation = Compilation.FromSource($$"""
            {{CompilerTestBed.FullPrelude}}

            public partial func Calculate(int32 x) -> ();
            public partial func Calculate(int32 x) -> ()
            {
                return;
            }
            """);

        Assert.False(compilation.HasErrors);
        Assert.Empty(compilation.Diagnostics);
    }

    [Fact]
    public void PartialFunction_MultipleBodies_EmitsMH0533()
    {
        var compilation = Compilation.FromSource("""
            public struct int;

            public partial func Action(int x) -> ()
            {
                return;
            }

            public partial func Action(int x) -> ()
            {
                return;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0533");
        Assert.Contains("cannot have more than one defining declaration with a body", diag.Message);
        Assert.Equal(2, diag.Labels.Count);
        Assert.Equal(DiagnosticLabelStyle.Primary, diag.Labels[0].Style);
        Assert.Equal(DiagnosticLabelStyle.Secondary, diag.Labels[1].Style);
    }

    [Fact]
    public void PartialMethod_MultipleBodiesInType_EmitsMH0533()
    {
        var compilation = Compilation.FromSource("""

            public partial struct Service
            {
                public partial func Process() -> ();

                public partial func Process() -> ()
                {
                    return;
                }

                public partial func Process() -> ()
                {
                    return;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0533");
        Assert.Contains("cannot have more than one defining declaration with a body", diag.Message);
    }

    [Fact]
    public void PartialFunction_Overloads_CanEachHaveBody()
    {
        var compilation = Compilation.FromSource($$"""
            {{CompilerTestBed.FullPrelude}}
            public struct IntA : Struct;
            public struct IntB : Struct;

            public partial func Compute(IntA a) -> ();
            public partial func Compute(IntA a) -> ()
            {
                return;
            }

            public partial func Compute(IntB b) -> ();
            public partial func Compute(IntB b) -> ()
            {
                return;
            }
            """);

        Assert.False(compilation.HasErrors);
        Assert.Empty(compilation.Diagnostics);
    }

    [Fact]
    public void PartialFunction_ConflictingReturnTypes_EmitsMH0534()
    {
        var compilation = Compilation.FromSource("""
            public struct RetA;
            public struct RetB;

            public partial func Query() -> RetA;
            public partial func Query() -> RetB;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0534");
        Assert.Contains("conflicting return types", diag.Message);
    }

    [Fact]
    public void DuplicateFunction_NonPartial_EmitsMH0532()
    {
        var compilation = Compilation.FromSource("""
            public struct Param;

            public func Execute(Param p) -> () { return; }
            public func Execute(Param p) -> () { return; }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0532");
        Assert.Contains("same parameter types is already declared", diag.Message);
    }

    [Fact]
    public void CyclicTypeHierarchy_DirectSelfInheritance_EmitsMH0538()
    {
        var compilation = Compilation.FromSource("""
            public struct Loop : Loop;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0538");
        Assert.Contains("type 'Loop' participates in a cycle", diag.Message);
    }

    [Fact]
    public void CyclicTypeHierarchy_MutualCycle_EmitsMH0538()
    {
        var compilation = Compilation.FromSource("""
            public struct First : Second;
            public struct Second : First;
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0538");
    }

    [Fact]
    public void DuplicateVariable_Global_EmitsMH0535()
    {
        var compilation = Compilation.FromSource("""
            public struct Val;
            public Val x;
            public Val x;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0535");
        Assert.Contains("variable 'x' is already declared", diag.Message);
    }

    [Fact]
    public void DuplicateVariable_Field_EmitsMH0535()
    {
        var compilation = Compilation.FromSource("""
            public struct Data
            {
                public int field;
                public int field;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0535");
        Assert.Contains("variable 'field' is already declared", diag.Message);
    }

    [Fact]
    public void DuplicateProperty_EmitsMH0536()
    {
        var compilation = Compilation.FromSource("""
            public struct Widget
            {
                public int Size { get; }
                public int Size { get; }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0536");
        Assert.Contains("property 'Size' is already declared", diag.Message);
    }

    [Fact]
    public void AmbiguousTypeReference_EmitsMH0501()
    {
        var compilation = Compilation.FromSource("""
            namespace N1
            {
                public struct Common;
            }
            namespace N2
            {
                public struct Common;
            }

            using N1;
            using N2;

            public struct Consumer
            {
                public Common item;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0501");
        Assert.Contains("type 'Common' is ambiguous in the current scope", diag.Message);
    }

    [Fact]
    public void PartialType_MultiFile_SucceedsWithoutErrors()
    {
        var (_, _, _, root1) = CompilerTestBed.Parse("public partial struct MultiFile;");
        var (_, _, _, root2) = CompilerTestBed.Parse("public partial struct MultiFile;");
        var context = new Resolver().Resolve([
            SyntaxTree.CreateSingleRoot(root1, "first.mh"),
            SyntaxTree.CreateSingleRoot(root2, "second.mh")
        ]);

        Assert.False(context.Diagnostics.HasErrors);
    }

    [Fact]
    public void DuplicateType_MultiFile_NonPartial_EmitsMH0530()
    {
        var (_, _, _, root1) = CompilerTestBed.Parse("public struct DupeMulti;");
        var (_, _, _, root2) = CompilerTestBed.Parse("public struct DupeMulti;");
        var context = new Resolver().Resolve([
            SyntaxTree.CreateSingleRoot(root1, "first.mh"),
            SyntaxTree.CreateSingleRoot(root2, "second.mh")
        ]);

        Assert.True(context.Diagnostics.HasErrors);
        var diag = Assert.Single(context.Diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0530");
        Assert.Contains("DupeMulti", diag.Message);
    }

    [Fact]
    public void PartialFunction_MultiFile_MultipleBodies_EmitsMH0533()
    {
        var (_, _, _, root1) = CompilerTestBed.Parse("public partial func Work() -> () { return; }");
        var (_, _, _, root2) = CompilerTestBed.Parse("public partial func Work() -> () { return; }");
        var context = new Resolver().Resolve([
            SyntaxTree.CreateSingleRoot(root1, "first.mh"),
            SyntaxTree.CreateSingleRoot(root2, "second.mh")
        ]);

        Assert.True(context.Diagnostics.HasErrors);
        var diag = Assert.Single(context.Diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0533");
        Assert.Contains("cannot have more than one defining declaration with a body", diag.Message);
    }

    [Fact]
    public void Types_DifferentGenericArity_AllowedInSameScope()
    {
        var compilation = Compilation.FromSource("""
            public struct Bag<T>;
            public struct Bag<T, U>;
            """);

        Assert.False(compilation.HasErrors);
        Assert.Empty(compilation.Diagnostics);
    }

    [Fact]
    public void DuplicateType_SameGenericArity_NonPartial_EmitsMH0530()
    {
        var compilation = Compilation.FromSource("""
            public struct Holder<T>;
            public struct Holder<T>;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0530");
        Assert.Contains("Holder", diag.Message);
    }

    [Fact]
    public void UnresolvedType_EmitsMH0500()
    {
        var compilation = Compilation.FromSource("""
            public NonExistentType globalVar;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0500");
        Assert.Contains("could not resolve type 'NonExistentType'", diag.Message);
    }

    [Fact]
    public void UnresolvedGenericType_ReportsTypeWithArity()
    {
        var compilation = Compilation.FromSource("""
            public struct int32;
            public Result<int32> globalVar;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0500");
        Assert.Contains("could not resolve type 'Result<T>'", diag.Message);
        var primaryLabel = Assert.Single(diag.Labels, l => l.Style == DiagnosticLabelStyle.Primary);
        Assert.Contains("type 'Result<T>' not found", primaryLabel.Message);
    }

    [Fact]
    public void UnresolvedGenericType_WhenNonGenericExistsInScope_ProvidesArityMismatchNote()
    {
        var compilation = Compilation.FromSource("""
            public struct int32;
            public struct Result;

            public Result<int32> globalVar;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0500");
        Assert.Contains("could not resolve type 'Result<T>'", diag.Message);
        var note = Assert.Single(diag.Notes);
        Assert.Contains("non-generic type 'Result' exists in this scope, but generic type 'Result<T>' with 1 type argument was not found", note.Message);
    }

    [Fact]
    public void UnresolvedNonGenericType_WhenGenericExistsInScope_ProvidesArityMismatchNote()
    {
        var compilation = Compilation.FromSource("""
            public struct Result<T>;

            public Result globalVar;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0500");
        Assert.Contains("could not resolve type 'Result'", diag.Message);
        var note = Assert.Single(diag.Notes);
        Assert.Contains("generic type 'Result<T>' exists in this scope with 1 type argument, but non-generic type 'Result' was not found", note.Message);
    }

    [Fact]
    public void UnresolvedType_InTupleDestructuring_EmitsMH0500ExactlyOnce()
    {
        var compilation = Compilation.FromSource("""
            public func Test() -> ()
            {
                UnknownType (a, b, c) = (1, 2, 3);
                return default;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0500");
        Assert.Contains("could not resolve type 'UnknownType'", diag.Message);
    }

    [Fact]
    public void UnresolvedType_InMultipleDeclarators_EmitsMH0500ExactlyOnce()
    {
        var compilation = Compilation.FromSource("""
            public func Test() -> ()
            {
                UnknownType a, b, c;
                return default;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0500");
        Assert.Contains("could not resolve type 'UnknownType'", diag.Message);
    }
}


