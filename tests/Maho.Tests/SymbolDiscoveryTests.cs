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

            public int g2;

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
        Assert.Equal(2, context.GlobalVariableSymbols.Count);
        Assert.Single(context.FieldSymbols);
        Assert.Single(context.PropertySymbols);
        Assert.Single(context.ParameterSymbols);
        Assert.Single(context.LocalVariableSymbols);

        TypeSymbol outer = Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "Outer");
        FunctionSymbol function = Assert.Single(context.FunctionSymbols);;
        Assert.All(context.TypeParameterSymbols, parameter => Assert.NotNull(parameter.GenericSymbol));

        Assert.True(context.GlobalNamespace.Next.ContainsKey(new SymbolPart("Example")));
        Assert.True(context.Scopes.Count > 1);
    }

    [Fact]
    public void Resolve_ResolvesDeclarationTypesAndNestedMembers()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Value;
            public struct Container : Value
            {
                public Value field;
                public Value Property { get; }
                public Value Method(Value parameter)
                {
                    Value local = parameter;
                    return local;
                }
            }
            public Value Function(Value parameter) { return parameter; }
            public Value global;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));
        TypeSymbol value = Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "Value");
        var valueHandle = ResolutionContext.GetHandle(value);
        ProductTypeSymbol container = Assert.IsType<ProductTypeSymbol>(Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "Container"));

        Assert.Equal(valueHandle, Assert.Single(container.BaseTypes));
        Assert.Equal(valueHandle, Assert.Single(context.FieldSymbols).Type);
        Assert.Equal(valueHandle, Assert.Single(context.PropertySymbols).Type);
        Assert.Equal(valueHandle, Assert.Single(context.MethodSymbols).ReturnType);
        Assert.Equal(valueHandle, Assert.Single(context.FunctionSymbols).ReturnType);
        Assert.Equal(valueHandle, Assert.Single(context.GlobalVariableSymbols).Type);
        Assert.All(context.ParameterSymbols, parameter => Assert.Equal(valueHandle, parameter.Type));
        Assert.Equal(valueHandle, Assert.Single(context.LocalVariableSymbols).Type);
        Assert.Single(container.Fields);
        Assert.Single(container.Properties);
        Assert.Single(container.Methods);
    }

    [Fact]
    public void Resolve_LabelsAndGotoBindWithinTheirFunction()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public static int Loop()
            {
            again:
                goto again;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));
        LabelSymbol label = Assert.Single(context.LabelSymbols);
        TopLevelFunctionDeclaration declaration = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members));
        FunctionBlockBody body = Assert.IsType<FunctionBlockBody>(declaration.Function.Body);
        LocalGotoStatement branch = Assert.IsType<LocalGotoStatement>(body.Locals[1]);

        Assert.True(context.ResolvedTree.TryGetReference(branch, out var target));
        Assert.Equal(ResolutionContext.GetHandle(label), target);
    }

    [Fact]
    public void Resolve_DiscoversEveryVariableDeclaratorInEachScope()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Value
            {
                public Value firstField, secondField;
                public Value Method()
                {
                    Value firstLocal, secondLocal;
                    return firstLocal;
                }
            }
            public Value firstGlobal, secondGlobal;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        Assert.Equal(["firstGlobal", "secondGlobal"], context.GlobalVariableSymbols.Select(symbol => symbol.Name.ToString()).Order());
        Assert.Equal(["firstField", "secondField"], context.FieldSymbols.Select(symbol => symbol.Name.ToString()).Order());
        Assert.Equal(["firstLocal", "secondLocal"], context.LocalVariableSymbols.Select(symbol => symbol.Name.ToString()).Order());
        Assert.Equal(2, Assert.Single(context.MethodSymbols).LocalVariables.Count);
    }
}
