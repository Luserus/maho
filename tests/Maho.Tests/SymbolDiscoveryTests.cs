using Maho.Resolution;
using Maho.Syntax;

namespace Maho.Tests;

public sealed class SymbolDiscoveryTests
{
    [Fact]
    public void Resolve_TopLevelPragmaCreatesImplicitMainAndLocalVariables()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            int scriptValue = 1;
            call();
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        FunctionSymbol main = Assert.Single(context.FunctionSymbols);
        Assert.Equal("Main", main.Name.ToString());
        Assert.Null(main.Syntax);
        Assert.Empty(context.GlobalVariableSymbols);

        LocalVariableSymbol scriptValue = Assert.Single(context.LocalVariableSymbols);
        Assert.Equal("scriptValue", scriptValue.Name.ToString());
        Assert.Equal(ResolutionContext.GetHandle(main), scriptValue.Parent);
        Assert.Equal(ResolutionContext.GetHandle(scriptValue), Assert.Single(main.LocalVariables));
    }

    [Fact]
    public void Resolve_TopLevelPragmaAppliesOnlyToItsCompilationUnit()
    {
        var (_, enabledDiagnostics, _, enabledRoot) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            int scriptValue = 1;
            """);
        var (_, ordinaryDiagnostics, _, ordinaryRoot) = CompilerTestBed.Parse("""
            int globalValue = 1;
            """);

        Assert.Empty(enabledDiagnostics.Diagnostics);
        Assert.Empty(ordinaryDiagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(new SyntaxTree("project", [enabledRoot, ordinaryRoot]));

        Assert.Single(context.FunctionSymbols, symbol => symbol.Name.ToString() == "Main" && symbol.Syntax is null);
        Assert.Single(context.LocalVariableSymbols, symbol => symbol.Name.ToString() == "scriptValue");
        Assert.Single(context.GlobalVariableSymbols, symbol => symbol.Name.ToString() == "globalValue");
    }

    [Fact]
    public void Resolve_GlobalModifiedBlockKeepsVariablesOutOfImplicitMain()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            int localValue = 1;
            global
            {
                int globalValue = 2;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        Assert.Single(context.LocalVariableSymbols, symbol => symbol.Name.ToString() == "localValue");
        Assert.Single(context.GlobalVariableSymbols, symbol => symbol.Name.ToString() == "globalValue");
    }

    [Fact]
    public void Resolve_NamespaceDirectivesApplyToTheirContainingTopLevelScope()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            namespace A;

            {
                namespace B;
                namespace C;
                struct ScopedTest;
            }

            struct AfterBlock;
            struct A.B.C.QualifiedTest;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        NamespaceTrieNode namespaceA = Assert.Single(context.GlobalNamespace.Next, entry => entry.Key.ToString() == "A").Value;
        NamespaceTrieNode namespaceB = Assert.Single(namespaceA.Next, entry => entry.Key.ToString() == "B").Value;
        NamespaceTrieNode namespaceC = Assert.Single(namespaceB.Next, entry => entry.Key.ToString() == "C").Value;

        Assert.Equal(3, context.TypeSymbols.Count);
        Assert.Same(namespaceA, Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "AfterBlock").ContainingNamespace);
        Assert.Same(namespaceC, Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "ScopedTest").ContainingNamespace);
        Assert.Same(namespaceC, Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "QualifiedTest").ContainingNamespace);
    }

    [Fact]
    public void Resolve_DiscoversParsedDeclarationsAndGenericParameters()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            namespace Example
            {
                public class Outer<T>
                {
                    public int field;
                    public int Property { get; set; }

                    public static int Method<U>(int parameter)
                    {
                        public class LocalType<W>;
                        public static int Nested<V>() { return 0; }
                        int local = 0;
                        return local;
                    }

                    { public class MemberType<X>; }
                }
            }

            public int g2;

            {
                public int global;
                public static int Function<Y>() { return 0; }
                public class TopLevelType<Z>;
            }

            using ExampleAlias = Example.Outer<int>;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        Assert.Equal(2, context.TypeSymbols.Count);
        Assert.Equal(2, context.NestedTypeSymbols.Count);
        Assert.Single(context.FunctionSymbols);
        Assert.Equal(2, context.MethodSymbols.Count);
        Assert.Equal(2, context.GlobalVariableSymbols.Count);
        Assert.Single(context.FieldSymbols);
        Assert.Single(context.PropertySymbols);
        Assert.Single(context.ParameterSymbols);
        Assert.Single(context.LocalVariableSymbols);
        Assert.Single(context.AliasSymbols);
        Assert.Equal("ExampleAlias", context.AliasSymbols[0].Name.ToString());
        Assert.All(context.GenericParameterSymbols, parameter => Assert.NotNull(parameter.GenericSymbol));
        Assert.True(context.GlobalNamespace.Next.ContainsKey(new SymbolPart("Example")));
    }

    [Fact]
    public void Resolve_DiscoversAliasSymbolsAcrossAllBlockScopesAndGenericParameters()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Target;
            public struct Constraint;

            namespace RootNs
            {
                using NsAlias = Target;
                using GenericNsAlias<T> = Target;

                public class Container<C>
                {
                    using MemberAlias = Target;
                    using GenericMemberAlias<M> = Target;

                    unsafe
                    {
                        using MemberBlockAlias = Target;
                    }

                    public static void Method<U>()
                    {
                        using LocalAlias = Target;
                        using GenericLocalAlias<L> = Target;

                        {
                            using InnerBlockAlias = Target;
                        }
                    }
                }
            }

            using TopAlias = Target;
            using GenericTopAlias<T> where T : Constraint = Target;

            global
            {
                using GlobalBlockAlias = Target;
            }

            {
                using ScopedBlockAlias = Target;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        Assert.Equal(12, context.AliasSymbols.Count);

        NamespaceTrieNode rootNs = Assert.Single(context.GlobalNamespace.Next, entry => entry.Key.ToString() == "RootNs").Value;
        TypeSymbol container = Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "Container");
        MethodSymbol method = Assert.Single(context.MethodSymbols, symbol => symbol.Name.ToString() == "Method");
        var containerHandle = ResolutionContext.GetHandle(container);
        var methodHandle = ResolutionContext.GetHandle(method);

        // 1. Namespace-level aliases
        AliasSymbol nsAlias = Assert.Single(context.AliasSymbols, a => a.Name.ToString() == "NsAlias");
        Assert.Same(rootNs, nsAlias.ContainingNamespace);
        Assert.Null(nsAlias.ContainingSymbol);
        Assert.Empty(nsAlias.GenericParameters);
        Assert.NotNull(nsAlias.Syntax);

        AliasSymbol genericNsAlias = Assert.Single(context.AliasSymbols, a => a.Name.ToString() == "GenericNsAlias");
        Assert.Same(rootNs, genericNsAlias.ContainingNamespace);
        Assert.Null(genericNsAlias.ContainingSymbol);
        Assert.Single(genericNsAlias.GenericParameters);
        GenericParameterSymbol nsParam = context.GenericParameterSymbols[genericNsAlias.GenericParameters[0].ID];
        Assert.Equal("T", nsParam.Name.ToString());
        Assert.Same(genericNsAlias, nsParam.GenericSymbol);

        // 2. Member-level aliases (including member blocks)
        AliasSymbol memberAlias = Assert.Single(context.AliasSymbols, a => a.Name.ToString() == "MemberAlias");
        Assert.Equal(containerHandle, memberAlias.ContainingSymbol);
        Assert.Null(memberAlias.ContainingNamespace);
        Assert.Empty(memberAlias.GenericParameters);

        AliasSymbol genericMemberAlias = Assert.Single(context.AliasSymbols, a => a.Name.ToString() == "GenericMemberAlias");
        Assert.Equal(containerHandle, genericMemberAlias.ContainingSymbol);
        Assert.Single(genericMemberAlias.GenericParameters);
        GenericParameterSymbol memberParam = context.GenericParameterSymbols[genericMemberAlias.GenericParameters[0].ID];
        Assert.Equal("M", memberParam.Name.ToString());
        Assert.Same(genericMemberAlias, memberParam.GenericSymbol);

        AliasSymbol memberBlockAlias = Assert.Single(context.AliasSymbols, a => a.Name.ToString() == "MemberBlockAlias");
        Assert.Equal(containerHandle, memberBlockAlias.ContainingSymbol);

        // 3. Local-level aliases (including local block statements)
        AliasSymbol localAlias = Assert.Single(context.AliasSymbols, a => a.Name.ToString() == "LocalAlias");
        Assert.Equal(methodHandle, localAlias.ContainingSymbol);
        Assert.Null(localAlias.ContainingNamespace);
        Assert.Empty(localAlias.GenericParameters);

        AliasSymbol genericLocalAlias = Assert.Single(context.AliasSymbols, a => a.Name.ToString() == "GenericLocalAlias");
        Assert.Equal(methodHandle, genericLocalAlias.ContainingSymbol);
        Assert.Single(genericLocalAlias.GenericParameters);
        GenericParameterSymbol localParam = context.GenericParameterSymbols[genericLocalAlias.GenericParameters[0].ID];
        Assert.Equal("L", localParam.Name.ToString());
        Assert.Same(genericLocalAlias, localParam.GenericSymbol);

        AliasSymbol innerBlockAlias = Assert.Single(context.AliasSymbols, a => a.Name.ToString() == "InnerBlockAlias");
        Assert.Equal(methodHandle, innerBlockAlias.ContainingSymbol);

        // 4. Top-level aliases (including top-level blocks)
        AliasSymbol topAlias = Assert.Single(context.AliasSymbols, a => a.Name.ToString() == "TopAlias");
        Assert.Same(context.GlobalNamespace, topAlias.ContainingNamespace);
        Assert.Null(topAlias.ContainingSymbol);
        Assert.Empty(topAlias.GenericParameters);

        AliasSymbol genericTopAlias = Assert.Single(context.AliasSymbols, a => a.Name.ToString() == "GenericTopAlias");
        Assert.Same(context.GlobalNamespace, genericTopAlias.ContainingNamespace);
        Assert.Null(genericTopAlias.ContainingSymbol);
        Assert.Single(genericTopAlias.GenericParameters);
        GenericParameterSymbol topParam = context.GenericParameterSymbols[genericTopAlias.GenericParameters[0].ID];
        Assert.Equal("T", topParam.Name.ToString());
        Assert.Same(genericTopAlias, topParam.GenericSymbol);

        AliasSymbol globalBlockAlias = Assert.Single(context.AliasSymbols, a => a.Name.ToString() == "GlobalBlockAlias");
        Assert.Same(context.GlobalNamespace, globalBlockAlias.ContainingNamespace);

        AliasSymbol scopedBlockAlias = Assert.Single(context.AliasSymbols, a => a.Name.ToString() == "ScopedBlockAlias");
        Assert.Same(context.GlobalNamespace, scopedBlockAlias.ContainingNamespace);

        // All aliases have their syntax scopes registered in context
        Assert.All(context.AliasSymbols, alias =>
        {
            Assert.NotNull(alias.Syntax);
            Scope aliasScope = context.GetSyntaxScope(alias.Syntax, Scope.GlobalScope);
            Assert.NotSame(Scope.GlobalScope, aliasScope);
        });
    }
}