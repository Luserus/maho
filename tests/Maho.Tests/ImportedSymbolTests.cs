using Maho.Resolution;

namespace Maho.Tests;

public sealed class ImportedSymbolTests
{
    [Fact]
    public void ImportedSymbols_CanBeResolvedFromReferencedContextWithoutPollutingLocalScope()
    {
        // 1. Build an external library compilation
        var libCompilation = Compilation.FromSource("""
            namespace Lib;

            public struct SharedWidget
            {
                public struct int;
                public int id;
            }
            """, "Lib.mh");

        Assert.False(libCompilation.HasErrors);
        Assert.NotNull(libCompilation.Context);

        // 2. Build a consumer compilation referencing the library
        var consumerCompilation = Compilation.FromSource("""
            namespace App;
            using Lib;

            public struct AppModel
            {
                public SharedWidget widget;
            }
            """, "App.mh", referencedCompilations: [libCompilation]);

        Assert.False(consumerCompilation.HasErrors);
        Assert.NotNull(consumerCompilation.Context);

        // Initialize with project reference
        var syntaxTree = consumerCompilation.SyntaxTrees[0];
        var resolver = new Resolver();
        var context = resolver.Resolve(syntaxTree, referencedProjects: [libCompilation.Context]);

        // Verify that SharedWidget is resolvable via using directive in consumer scope
        var sharedWidgetPart = new SymbolPart("SharedWidget");
        var appScope = context.GetSyntaxScope(syntaxTree.Roots[0], context.GlobalScope);
        var resolvedSymbols = appScope.GetSymbols(sharedWidgetPart);
        Assert.Single(resolvedSymbols);
        Assert.Equal("SharedWidget", resolvedSymbols[0].Name.ToString());

        // Verify that qualified lookup also resolves from the referenced project
        var qualifiedSymbols = context.GlobalScope[new SymbolName([new SymbolPart("Lib"), sharedWidgetPart])];
        Assert.Single(qualifiedSymbols);
        Assert.Equal("SharedWidget", qualifiedSymbols[0].Name.ToString());

        // Verify that local GlobalScope.Symbols is NOT polluted with the imported symbol
        Assert.Empty(context.GlobalScope.GetLocalSymbols(sharedWidgetPart));
        Assert.Single(context.GlobalScope.ImportedScopes);
    }

    [Fact]
    public void ImportedSymbols_AreShadowedByLocalDeclarations()
    {
        // 1. Library defines Widget
        var libCompilation = Compilation.FromSource("""
            public struct Widget;
            """, "Lib.mh");

        // 2. App also defines Widget locally
        var appCompilation = Compilation.FromSource("""
            public struct Widget;
            """, "App.mh");

        var syntaxTree = appCompilation.SyntaxTrees[0];
        var resolver = new Resolver();
        var context = resolver.Resolve(syntaxTree, referencedProjects: [libCompilation.Context!]);

        // Local Widget should shadow imported Widget
        var widgetPart = new SymbolPart("Widget");
        var localSymbols = context.GlobalScope.GetLocalSymbols(widgetPart);
        Assert.Single(localSymbols);

        var allSymbols = context.GlobalScope.GetSymbols(widgetPart);
        Assert.Single(allSymbols);
        Assert.Same(localSymbols[0], allSymbols[0]);
    }

    [Fact]
    public void PolymorphicOutputs_SupportDiagnosticsDebugAndIlVariants()
    {
        var diag = new DiagnosticInfo("MH0040", "Test error", DiagnosticSeverity.Error, default);

        CompilationOutput debugOutput = new DebugCompilationOutput(
            "test.mh",
            lexerJson: "{\"kind\":\"lexer\"}",
            parserJson: "{\"kind\":\"parser\"}",
            diagnostics: [diag]);

        Assert.Equal(CompilationOutputKind.Debug, debugOutput.Kind);
        Assert.True(debugOutput.HasErrors);
        Assert.False(debugOutput.Success);
        Assert.IsType<DebugCompilationOutput>(debugOutput);

        CompilationOutput ilOutput = new IlCompilationOutput(
            ilBytes: [0x01, 0x02, 0x03],
            ilDisassembly: "ldc.i4 42\nret",
            diagnostics: []);

        Assert.Equal(CompilationOutputKind.Il, ilOutput.Kind);
        Assert.False(ilOutput.HasErrors);
        Assert.True(ilOutput.Success);
        var typedIl = Assert.IsType<IlCompilationOutput>(ilOutput);
        Assert.Equal(3, typedIl.IlBytes!.Length);

        CompilationOutput diagOutput = new DiagnosticsCompilationOutput("test.mh", [diag]);
        Assert.Equal(CompilationOutputKind.Diagnostics, diagOutput.Kind);
        Assert.True(diagOutput.HasErrors);
    }

    [Fact]
    public void MahoCompiler_VersionString_ReturnsAssemblyVersion()
    {
        Assert.NotNull(MahoCompiler.VersionString);
        Assert.NotEmpty(MahoCompiler.VersionString);
        Assert.Equal("0.1.0", MahoCompiler.VersionString);
    }
}
