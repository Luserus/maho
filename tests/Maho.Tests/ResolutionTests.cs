using Maho.Resolution;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Tests;

public sealed class ResolutionTests
{
    [Fact]
    public void Resolve_SingleUnit_CreatesDeclarationSymbolsAndScopes()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            namespace Demo;

            public class Box<T>
            {
                public T Value;
            }

            public static int Main(int argc)
            {
                int value;
                return 0;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionResult result = CompilerTestBed.ResolveProject(root).Units[0];
        TopLevelTypeDeclaration typeWrapper = Assert.IsType<TopLevelTypeDeclaration>(root.Members[1]);
        TopLevelFunctionDeclaration functionWrapper = Assert.IsType<TopLevelFunctionDeclaration>(root.Members[2]);

        Assert.True(result.TryResolveDeclaredSymbol(typeWrapper.Type, out Symbol? typeSymbol));
        Assert.True(result.TryResolveDeclaredSymbol(functionWrapper.Function, out Symbol? functionSymbol));

        TypeSymbol resolvedType = Assert.IsType<TypeSymbol>(typeSymbol);
        FunctionSymbol resolvedFunction = Assert.IsType<FunctionSymbol>(functionSymbol);

        Assert.Equal(1, resolvedType.Arity);
        Assert.Single(resolvedType.TypeParameters);
        Assert.Equal(1, resolvedFunction.ParameterCount);
        Assert.True(result.TryResolveScope(functionWrapper.Function.Body, out Scope? functionScope));
        Assert.NotNull(functionScope);
        Assert.Contains(functionScope.DeclaredSymbols, symbol => symbol is ParameterSymbol parameter && parameter.Name.ToString() == "argc");
        Assert.Contains(functionScope.DeclaredSymbols, symbol => symbol is VariableSymbol variable && variable.Name.ToString() == "value");
    }

    [Fact]
    public void Resolve_MultipleUnits_MergesSharedNamespaceScope()
    {
        var (_, diagnostics1, _, root1) = CompilerTestBed.Parse("""
            namespace Demo;
            public class Foo;
            """);
        var (_, diagnostics2, _, root2) = CompilerTestBed.Parse("""
            namespace Demo;
            public class Bar;
            """);

        Assert.Empty(diagnostics1.Diagnostics);
        Assert.Empty(diagnostics2.Diagnostics);

        ResolutionProjectResult result = CompilerTestBed.ResolveProject(root1, root2);

        IReadOnlyList<Symbol> demoNamespaceSymbols = result.GlobalScope.LookupLocal(SymbolName.FromLiteral("Demo"));
        NamespaceSymbol demoNamespace = Assert.IsType<NamespaceSymbol>(Assert.Single(demoNamespaceSymbols));
        Scope namespaceScope = result.SymbolScopes[demoNamespace];

        Assert.Contains(namespaceScope.DeclaredSymbols, symbol => symbol is TypeSymbol type && type.Name.ToString() == "Foo");
        Assert.Contains(namespaceScope.DeclaredSymbols, symbol => symbol is TypeSymbol type && type.Name.ToString() == "Bar");
        Assert.Equal(2, result.Units.Count);
    }
}
