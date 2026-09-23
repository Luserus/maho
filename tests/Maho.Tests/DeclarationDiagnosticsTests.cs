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
            public class Foo;
            public class Foo;
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
            public class Outer
            {
                public class Inner;
                public class Inner;
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
            public partial class Bar;
            public class Bar;
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
        var compilation = Compilation.FromSource("""
            public partial class Entity;
            public partial class Entity;
            public partial class Entity;

            public struct User
            {
                public Entity entity;
            }
            """);

        Assert.False(compilation.HasErrors);
        Assert.Empty(compilation.Diagnostics);

        // Verify that referencing Entity resolves correctly
        var user = Assert.Single(compilation.Context!.TypeSymbols, t => t.Name.ToString() == "User");
        var field = Assert.Single(compilation.Context.FieldSymbols, f => f.Name.ToString() == "entity");
        Assert.True(field.Type.IsResolved);
    }

    [Fact]
    public void PartialType_ConflictingKinds_EmitsMH0531()
    {
        var compilation = Compilation.FromSource("""
            public partial class Conflict;
            public partial struct Conflict;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0531");
        Assert.Contains("conflicting type kinds", diag.Message);
    }

    [Fact]
    public void PartialFunction_ZeroBodies_SucceedsWithoutWarningsOrErrors()
    {
        var compilation = Compilation.FromSource("""
            public struct void;

            public partial void DoWork();
            public partial void DoWork();
            public partial void DoWork();
            """);

        Assert.False(compilation.HasErrors);
        Assert.Empty(compilation.Diagnostics);
    }

    [Fact]
    public void PartialFunction_OneBody_SucceedsWithoutErrors()
    {
        var compilation = Compilation.FromSource("""
            public struct void;
            public struct int;

            public partial void Calculate(int x);
            public partial void Calculate(int x)
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
            public partial void Action(int x)
            {
                return;
            }

            public partial void Action(int x)
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
            public partial class Service
            {
                public partial void Process();

                public partial void Process()
                {
                    return;
                }

                public partial void Process()
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
        var compilation = Compilation.FromSource("""
            public struct void;
            public struct IntA;
            public struct IntB;

            public partial void Compute(IntA a);
            public partial void Compute(IntA a)
            {
                return;
            }

            public partial void Compute(IntB b);
            public partial void Compute(IntB b)
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

            public partial RetA Query();
            public partial RetB Query();
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

            public void Execute(Param p) { return; }
            public void Execute(Param p) { return; }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0532");
        Assert.Contains("same parameter types is already declared", diag.Message);
    }

    [Fact]
    public void CyclicTypeHierarchy_DirectSelfInheritance_EmitsMH0538()
    {
        var compilation = Compilation.FromSource("""
            public class Loop : Loop;
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0538");
        Assert.Contains("type 'Loop' participates in a cycle", diag.Message);
    }

    [Fact]
    public void CyclicTypeHierarchy_MutualCycle_EmitsMH0538()
    {
        var compilation = Compilation.FromSource("""
            public class First : Second;
            public class Second : First;
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
            public class Widget
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
        var (_, _, _, root1) = CompilerTestBed.Parse("public partial class MultiFile;");
        var (_, _, _, root2) = CompilerTestBed.Parse("public partial class MultiFile;");
        var syntaxTree = new SyntaxTree("TestProject", [root1, root2]);
        var context = new Resolver().Resolve(syntaxTree);

        Assert.False(context.Diagnostics.HasErrors);
    }

    [Fact]
    public void DuplicateType_MultiFile_NonPartial_EmitsMH0530()
    {
        var (_, _, _, root1) = CompilerTestBed.Parse("public class DupeMulti;");
        var (_, _, _, root2) = CompilerTestBed.Parse("public class DupeMulti;");
        var syntaxTree = new SyntaxTree("TestProject", [root1, root2]);
        var context = new Resolver().Resolve(syntaxTree);

        Assert.True(context.Diagnostics.HasErrors);
        var diag = Assert.Single(context.Diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0530");
        Assert.Contains("DupeMulti", diag.Message);
    }

    [Fact]
    public void PartialFunction_MultiFile_MultipleBodies_EmitsMH0533()
    {
        var (_, _, _, root1) = CompilerTestBed.Parse("public partial void Work() { return; }");
        var (_, _, _, root2) = CompilerTestBed.Parse("public partial void Work() { return; }");
        var syntaxTree = new SyntaxTree("TestProject", [root1, root2]);
        var context = new Resolver().Resolve(syntaxTree);

        Assert.True(context.Diagnostics.HasErrors);
        var diag = Assert.Single(context.Diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0533");
        Assert.Contains("cannot have more than one defining declaration with a body", diag.Message);
    }

    [Fact]
    public void Types_DifferentGenericArity_AllowedInSameScope()
    {
        var compilation = Compilation.FromSource("""
            public class Bag<T>;
            public class Bag<T, U>;
            """);

        Assert.False(compilation.HasErrors);
        Assert.Empty(compilation.Diagnostics);
    }

    [Fact]
    public void DuplicateType_SameGenericArity_NonPartial_EmitsMH0530()
    {
        var compilation = Compilation.FromSource("""
            public class Holder<T>;
            public class Holder<T>;
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
}
