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
    public void Resolve_GlobalBlockKeepsVariablesOutOfImplicitMain()
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
        Assert.Contains(context.TypeSymbols, symbol => symbol.Name.ToString() == "ScopedTest");
        Assert.Contains(context.TypeSymbols, symbol => symbol.Name.ToString() == "QualifiedTest");
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

            {
                public int global;
                public static int Function<Y>() { return 0; }
                public class TopLevelType<Z>;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        Assert.Equal(2, context.TypeSymbols.Count);
        Assert.Equal(2, context.NestedTypeSymbols.Count);
        Assert.Single(context.FunctionSymbols);
        Assert.Equal(2, context.MethodSymbols.Count);
        Assert.Single(context.GlobalVariableSymbols);
        Assert.Single(context.FieldSymbols);
        Assert.Single(context.PropertySymbols);
        Assert.Single(context.ParameterSymbols);
        Assert.Single(context.LocalVariableSymbols);
        Assert.Equal(7, context.TypeParameterSymbols.Count);

        TypeSymbol outer = Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "Outer");
        FunctionSymbol function = Assert.Single(context.FunctionSymbols);
        Assert.Single(outer.TypeParameters);
        Assert.Single(function.TypeParameters);
        Assert.All(context.TypeParameterSymbols, parameter => Assert.NotNull(parameter.GenericSymbol));

        Assert.True(context.GlobalNamespace.Next.ContainsKey(new SymbolName("Example")));
        Assert.True(context.Scopes.Count > 1);
    }
}
