using Maho.Analysis;
using Maho.Build;
using Maho.Syntax;

namespace Maho.Tests;

public sealed class BuildSystemTests
{
    [Fact]
    public void Directive_Hierarchy_IncludesPragmaAndUsingDirectives()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            #pragma toplevel enable
            using Std;

            namespace MyNamespace
            {
                using Math;

                public struct Point;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        // Compilation unit directives
        Assert.Equal(2, root.Directives.Count);
        Assert.IsType<Directive>(root.Directives[0], exactMatch: false);
        Assert.IsType<Directive>(root.Directives[1], exactMatch: false);

        var pragma = Assert.IsType<PragmaDirective>(root.Directives[0]);
        Assert.Equal("toplevel", pragma.Name.Value);

        var fileUsing = Assert.IsType<UsingDirective>(root.Directives[1]);
        var simple = Assert.IsType<SimpleName>(fileUsing.Namespace);
        Assert.Equal("Std", simple.Name.Value);

        // Typed helper accessors
        Assert.Single(root.Pragmas);
        Assert.Single(root.Usings);

        // Members list should NOT contain Directives
        Assert.Single(root.Members);
        var ns = Assert.IsType<NamespaceDeclaration>(root.Members[0]);
        var body = Assert.IsType<NamespaceBlockBody>(ns.Body);

        // Namespace block body directives
        Assert.Single(body.Directives);
        var nsUsing = Assert.IsType<UsingDirective>(body.Directives[0]);
        var nsSimple = Assert.IsType<SimpleName>(nsUsing.Namespace);
        Assert.Equal("Math", nsSimple.Name.Value);
        Assert.Single(body.Usings);

        // Namespace block members list should only contain TopLevel declarations
        Assert.Single(body.Members);
        Assert.IsType<TopLevelTypeDeclaration>(body.Members[0]);
    }

    [Fact]
    public void MahoBuildSystem_LoadsProject_AndResolvesOptions()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_BuildSystemTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string projectFile = Path.Combine(tempDir, "App.mhpr");
            File.WriteAllText(projectFile, """
                EntryFile : "Main.mh";
                ImplicitTopLevel : true;
                """);

            string mainFile = Path.Combine(tempDir, "Main.mh");
            File.WriteAllText(mainFile, "var result = 42;");

            string helperFile = Path.Combine(tempDir, "Helper.mh");
            File.WriteAllText(helperFile, "public struct Helper;");

            var project = MahoBuildSystem.LoadProject(projectFile);

            Assert.Equal("App", project.ProjectName);
            Assert.Equal(tempDir, project.ProjectDirectory);
            Assert.Equal(2, project.SourceFiles.Count);
            Assert.True(project.Options.ImplicitTopLevel);
            Assert.Equal(Path.GetFullPath(mainFile), project.Options.EntryFile);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MahoCompiler_AnalyzesNonMhExtensions_DirectlyProvided()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_NonMhExtension_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string customFile = Path.Combine(tempDir, "Script.source");
            File.WriteAllText(customFile, """
                public struct NonMhWidget
                {
                    public int value;
                }
                """);

            // MahoCompiler should analyze arbitrary file paths provided to it without requiring .mh extension
            var result = MahoCompiler.AnalyzeFiles([customFile]);

            Assert.False(result.HasErrors);
            Assert.Single(result.Files);
            Assert.NotNull(result.Compilation);
            Assert.Single(result.Compilation.Context!.TypeSymbols, t => t.Name.ToString() == "NonMhWidget");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DirectoryCompilation_DoesNotImplicitlyEnableTopLevel_WhenSingleFileInDir()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_DirSingleFile_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string fileInDir = Path.Combine(tempDir, "Program.mh");
            File.WriteAllText(fileInDir, "return 42;");

            // Analyzing a directory (even if it has only 1 file) should NOT implicitly enable top-level statements
            // because directory builds represent structured projects, not terminal one-liners.
            var result = MahoBuildSystem.AnalyzeDirectory(tempDir);

            // Expect MH0011 error because top-level is disabled by default for directory/project builds
            Assert.True(result.HasErrors);
            Assert.Contains(result.Compilation!.Diagnostics, d => d.Code == "MH0011");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
