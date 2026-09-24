using Maho.Resolution;
using Maho.Syntax;

namespace Maho.Tests;

public sealed class UsingDirectiveTests
{
    [Fact]
    public void UsingDirective_ImportsNamespaceSymbolsIntoVisibleScope()
    {
        var compilation = Compilation.FromSource("""
            namespace Std
            {
                public struct Int32
                {
                    public struct int;
                    public int value;
                }
            }

            using Std;

            public struct User
            {
                public Int32 age;
            }
            """, "User.mh");

        Assert.False(compilation.HasErrors);
        Assert.NotNull(compilation.Context);

        // Int32 in User.age should be resolved to Std.Int32
        var userType = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.ToString() == "User");
        var int32Type = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.ToString() == "Int32");
        Assert.NotNull(int32Type.ContainingNamespace);
        Assert.Equal("Std", int32Type.ContainingNamespace.Name.ToString());
    }

    [Fact]
    public void QualifiedNamespaceAccess_WorksWithoutUsingDirective()
    {
        var compilation = Compilation.FromSource("""
            namespace Std
            {
                public struct Int32;
            }

            public struct User
            {
                public Std.Int32 age;
            }
            """, "User.mh");

        Assert.False(compilation.HasErrors);
        Assert.NotNull(compilation.Context);

        var int32Type = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.ToString() == "Int32");
        Assert.Equal("Std", int32Type.ContainingNamespace?.Name.ToString());
    }

    [Fact]
    public void NestedNamespaceUsing_ImportsDeepSymbols()
    {
        var compilation = Compilation.FromSource("""
            namespace Std.Collections
            {
                public struct List;
            }

            using Std.Collections;

            public struct Container
            {
                public List items;
            }
            """, "Container.mh");

        Assert.False(compilation.HasErrors);
        Assert.NotNull(compilation.Context);

        var listType = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.ToString() == "List");
        Assert.Equal("Collections", listType.ContainingNamespace?.Name.ToString());
    }

    [Fact]
    public void UsingDirective_AppliesToFileScope_AcrossMultipleNamespaces()
    {
        var compilation = Compilation.FromSource("""
            namespace Helper
            {
                public struct HelperData;
            }

            using Helper;

            namespace ConsumerA
            {
                public struct ConsumerModel
                {
                    public HelperData data;
                }
            }

            namespace ConsumerB
            {
                public struct OtherModel
                {
                    public HelperData data;
                }
            }
            """, "Consumers.mh");

        Assert.False(compilation.HasErrors);
        Assert.NotNull(compilation.Context);

        var consumerModel = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.ToString() == "ConsumerModel");
        var otherModel = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.ToString() == "OtherModel");
        var helperData = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.ToString() == "HelperData");

        var fieldA = Assert.Single(compilation.Context.FieldSymbols, f => f.Name.ToString() == "data" && f.EnclosingScope == compilation.Context.GetSyntaxScope(consumerModel.Syntax!.Body, compilation.Context.GlobalScope));
        var fieldB = Assert.Single(compilation.Context.FieldSymbols, f => f.Name.ToString() == "data" && f.EnclosingScope == compilation.Context.GetSyntaxScope(otherModel.Syntax!.Body, compilation.Context.GlobalScope));
        Assert.True(fieldA.Type.IsResolved);
        Assert.True(fieldB.Type.IsResolved);
        Assert.Equal((SymbolKind.Type, helperData.ID), fieldA.Type.Handle);
        Assert.Equal((SymbolKind.Type, helperData.ID), fieldB.Type.Handle);
    }

    [Fact]
    public void UsingDirective_IsScopedToNamespaceBlock()
    {
        var (sourceText, diagnostics, _, root) = CompilerTestBed.Parse("""
            namespace Helper
            {
                public struct HelperData;
            }

            namespace ConsumerA
            {
                using Helper;

                public struct ConsumerModel
                {
                    public HelperData data;
                }
            }

            namespace ConsumerB
            {
                public struct OtherModel
                {
                    public HelperData data;
                }
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        var resolver = new Resolver();
        var context = resolver.Resolve(SyntaxTree.CreateSingleRoot(root));

        // ConsumerA should resolve HelperData
        var consumerModel = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "ConsumerModel");
        var helperData = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "HelperData");
        var fieldA = Assert.Single(context.FieldSymbols, f => f.Name.ToString() == "data" && f.EnclosingScope == context.GetSyntaxScope(consumerModel.Syntax!.Body, context.GlobalScope));
        Assert.True(fieldA.Type.IsResolved);
        Assert.Equal((SymbolKind.Type, helperData.ID), fieldA.Type.Handle);

        // ConsumerB should NOT resolve HelperData (it will be unresolved / Error)
        var otherModel = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "OtherModel");
        var fieldB = Assert.Single(context.FieldSymbols, f => f.Name.ToString() == "data" && f.EnclosingScope == context.GetSyntaxScope(otherModel.Syntax!.Body, context.GlobalScope));
        Assert.True(fieldB.Type.IsError, "HelperData in ConsumerB should be unresolved without using Helper");
    }

    [Fact]
    public void UsingAlias_And_UsingDirective_Coexist()
    {
        var compilation = Compilation.FromSource("""
            namespace Std
            {
                public struct Int32;
                public struct String;
            }

            using Std;
            using Text = Std.String;

            public struct Doc
            {
                public Int32 id;
                public Text body;
            }
            """, "Doc.mh");

        Assert.False(compilation.HasErrors);
        Assert.NotNull(compilation.Context);

        var docType = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.ToString() == "Doc");
        var alias = Assert.Single(compilation.Context.AliasSymbols, a => a.Name.ToString() == "Text");
        Assert.True(alias.Target.IsResolved);
    }

    [Fact]
    public void UsingDirective_And_Alias_ScopedToTypeBlock()
    {
        var compilation = Compilation.FromSource("""
            namespace Helper
            {
                public struct HelperData;
            }

            public struct Container
            {
                using Helper;
                using DataAlias = HelperData;

                public DataAlias fieldA;
                public HelperData fieldB;
            }

            public struct OtherContainer
            {
                public HelperData outsideField;
            }
            """, "TypeScoped.mh");

        Assert.NotNull(compilation.Context);

        var helperData = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.ToString() == "HelperData");
        var container = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.ToString() == "Container");
        var otherContainer = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.ToString() == "OtherContainer");

        var fieldA = Assert.Single(compilation.Context.FieldSymbols, f => f.Name.ToString() == "fieldA" && f.EnclosingScope == compilation.Context.GetSyntaxScope(container.Syntax!.Body, compilation.Context.GlobalScope));
        var fieldB = Assert.Single(compilation.Context.FieldSymbols, f => f.Name.ToString() == "fieldB" && f.EnclosingScope == compilation.Context.GetSyntaxScope(container.Syntax!.Body, compilation.Context.GlobalScope));
        var outsideField = Assert.Single(compilation.Context.FieldSymbols, f => f.Name.ToString() == "outsideField" && f.EnclosingScope == compilation.Context.GetSyntaxScope(otherContainer.Syntax!.Body, compilation.Context.GlobalScope));

        Assert.True(fieldA.Type.IsResolved, "DataAlias should resolve within Container");
        Assert.True(fieldB.Type.IsResolved, "HelperData should resolve within Container due to member using");
        Assert.Equal((SymbolKind.Type, helperData.ID), fieldB.Type.Handle);

        Assert.True(outsideField.Type.IsError, "HelperData should NOT be visible in OtherContainer");
    }

    [Fact]
    public void UsingDirective_And_Alias_ScopedToFunctionBlock()
    {
        var compilation = Compilation.FromSource("""
            namespace Helper
            {
                public struct HelperData;
            }

            public struct Service
            {
                public void Process()
                {
                    using Helper;
                    using LocalData = HelperData;

                    LocalData x;
                    HelperData y;
                }

                public void Other()
                {
                    HelperData z;
                }
            }
            """, "FunctionScoped.mh");

        Assert.NotNull(compilation.Context);

        var helperData = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.ToString() == "HelperData");
        var service = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.ToString() == "Service");

        var localX = Assert.Single(compilation.Context.LocalVariableSymbols, v => v.Name.ToString() == "x");
        var localY = Assert.Single(compilation.Context.LocalVariableSymbols, v => v.Name.ToString() == "y");
        var localZ = Assert.Single(compilation.Context.LocalVariableSymbols, v => v.Name.ToString() == "z");

        Assert.True(localX.Type.IsResolved, "LocalData should resolve inside Process function body");
        Assert.True(localY.Type.IsResolved, "HelperData should resolve inside Process function body due to local using");
        Assert.Equal((SymbolKind.Type, helperData.ID), localY.Type.Handle);

        Assert.True(localZ.Type.IsError, "HelperData should NOT resolve in Other function body");
    }

    [Fact]
    public void UsingDirective_And_Alias_ScopedToInnerBlockStatement()
    {
        var compilation = Compilation.FromSource("""
            namespace Helper
            {
                public struct HelperData;
            }

            public struct Runner
            {
                public void Run()
                {
                    {
                        using Helper;
                        using BlockData = HelperData;

                        BlockData inside;
                    }

                    HelperData outside;
                }
            }
            """, "InnerBlockScoped.mh");

        Assert.NotNull(compilation.Context);

        var insideVar = Assert.Single(compilation.Context.LocalVariableSymbols, v => v.Name.ToString() == "inside");
        var outsideVar = Assert.Single(compilation.Context.LocalVariableSymbols, v => v.Name.ToString() == "outside");

        Assert.True(insideVar.Type.IsResolved, "BlockData should resolve inside the nested block");
        Assert.True(outsideVar.Type.IsError, "HelperData should NOT resolve outside the nested block in the same function");
    }

    [Fact]
    public void InnerBlock_Using_Shadows_OuterScope_Using()
    {
        var compilation = Compilation.FromSource("""
            namespace NsA
            {
                public struct Item;
            }

            namespace NsB
            {
                public struct Item;
            }

            using NsA;

            public struct Demo
            {
                public Item outerItem;

                public void Test()
                {
                    {
                        using NsB;
                        Item innerItem;
                    }
                }
            }
            """, "Shadowing.mh");

        Assert.NotNull(compilation.Context);

        var itemA = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.ToString() == "Item" && t.ContainingNamespace?.Name.ToString() == "NsA");
        var itemB = Assert.Single(compilation.Context.TypeSymbols, t => t.Name.ToString() == "Item" && t.ContainingNamespace?.Name.ToString() == "NsB");

        var outerField = Assert.Single(compilation.Context.FieldSymbols, f => f.Name.ToString() == "outerItem");
        var innerLocal = Assert.Single(compilation.Context.LocalVariableSymbols, v => v.Name.ToString() == "innerItem");

        Assert.True(outerField.Type.IsResolved);
        Assert.Equal((SymbolKind.Type, itemA.ID), outerField.Type.Handle);

        Assert.True(innerLocal.Type.IsResolved);
        Assert.Equal((SymbolKind.Type, itemB.ID), innerLocal.Type.Handle);
    }
}
