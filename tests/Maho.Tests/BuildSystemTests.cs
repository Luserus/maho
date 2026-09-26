using Miryo.Build;
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
            Assert.True(project.ImplicitTopLevel);
            Assert.Equal(Path.GetFullPath(mainFile), project.EntryFile);
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
                    public struct int;
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
            string[] files = MiryoBuildSystem.ResolveSourceFiles(tempDir);
            var result = MahoCompiler.AnalyzeFiles(files, rootPath: tempDir);

            // Expect MH0160 error because top-level is disabled by default for directory/project builds
            Assert.True(result.HasErrors);
            Assert.Contains(result.Compilation!.Diagnostics, d => d.Code == "MH0160");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MahoBuildSystem_LoadsProject_WithSourcesObject_ResolvesSourceFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_SourcesObjTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string projectFile = Path.Combine(tempDir, "App.mhpr");
            File.WriteAllText(projectFile, """
                EntryFile : "Main.mh";
                Sources : {
                    Directory : "$",
                    SourceFiles : [ "Main.mh" ],
                    ByName : "*.mh"
                };
                """);

            string mainFile = Path.Combine(tempDir, "Main.mh");
            File.WriteAllText(mainFile, "var result = 42;");

            string extraFile = Path.Combine(tempDir, "Extra.mh");
            File.WriteAllText(extraFile, "public struct Extra;");

            var project = MahoBuildSystem.LoadProject(projectFile);

            Assert.NotNull(project.Configuration.Sources);
            Assert.Equal("$", project.Configuration.Sources.Directory);
            Assert.Equal(["Main.mh"], project.Configuration.Sources.SourceFiles);
            Assert.Equal("*.mh", project.Configuration.Sources.ByName);
            Assert.Equal(2, project.SourceFiles.Count);
            Assert.Contains(Path.GetFullPath(mainFile), project.SourceFiles);
            Assert.Contains(Path.GetFullPath(extraFile), project.SourceFiles);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MahoBuildSystem_LoadsProject_WithExplicitSourceFiles_FiltersOutUnlistedFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_ExplicitSourcesTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string projectFile = Path.Combine(tempDir, "App.mhpr");
            File.WriteAllText(projectFile, """
                EntryFile : "Main.mh";
                Sources : {
                    SourceFiles : [ "Main.mh" ]
                };
                """);

            string mainFile = Path.Combine(tempDir, "Main.mh");
            File.WriteAllText(mainFile, "var result = 42;");

            string ignoredFile = Path.Combine(tempDir, "Ignored.mh");
            File.WriteAllText(ignoredFile, "public struct Ignored;");

            var project = MahoBuildSystem.LoadProject(projectFile);

            Assert.Single(project.SourceFiles);
            Assert.Equal(Path.GetFullPath(mainFile), project.SourceFiles[0]);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MahoBuildSystem_LoadsProject_WithCustomDirectory_ResolvesFromSubdirectory()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_CustomDirTest_" + Guid.NewGuid().ToString("N"));
        string srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);
        try
        {
            string projectFile = Path.Combine(tempDir, "App.mhpr");
            File.WriteAllText(projectFile, """
                Sources : {
                    Directory : "src",
                    ByName : "*.mh"
                };
                """);

            string srcFile = Path.Combine(srcDir, "Code.mh");
            File.WriteAllText(srcFile, "public struct Widget;");

            string rootFile = Path.Combine(tempDir, "RootIgnored.mh");
            File.WriteAllText(rootFile, "public struct RootIgnored;");

            var project = MahoBuildSystem.LoadProject(projectFile);

            Assert.Single(project.SourceFiles);
            Assert.Equal(Path.GetFullPath(srcFile), project.SourceFiles[0]);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MahoBuildSystem_LoadsProject_WithSourcesArrayShorthand_ResolvesFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_ArrayShorthandTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string projectFile = Path.Combine(tempDir, "App.mhpr");
            File.WriteAllText(projectFile, """
                Sources : [ "Main.mh" ];
                """);

            string mainFile = Path.Combine(tempDir, "Main.mh");
            File.WriteAllText(mainFile, "public struct Main;");

            var project = MahoBuildSystem.LoadProject(projectFile);

            Assert.Single(project.SourceFiles);
            Assert.Equal(Path.GetFullPath(mainFile), project.SourceFiles[0]);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MahoBuildSystem_MissingSourceFile_ThrowsFileNotFoundException()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_MissingFileTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string projectFile = Path.Combine(tempDir, "App.mhpr");
            File.WriteAllText(projectFile, """
                Sources : {
                    SourceFiles : [ "DoesNotExist.mh" ]
                };
                """);

            Assert.Throws<FileNotFoundException>(() => MahoBuildSystem.LoadProject(projectFile));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MahoBuildSystem_MissingDirectory_ThrowsDirectoryNotFoundException()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_MissingDirTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string projectFile = Path.Combine(tempDir, "App.mhpr");
            File.WriteAllText(projectFile, """
                Sources : {
                    Directory : "non_existent_folder"
                };
                """);

            Assert.Throws<DirectoryNotFoundException>(() => MahoBuildSystem.LoadProject(projectFile));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MahoBuildSystem_EntryFile_EnsuresInclusionWithNonStandardExtension()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Maho_EntryNonMhTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string projectFile = Path.Combine(tempDir, "App.mhpr");
            File.WriteAllText(projectFile, """
                EntryFile : "Main.custom";
                """);

            string mainFile = Path.Combine(tempDir, "Main.custom");
            File.WriteAllText(mainFile, "public struct EntryCustom;");

            string helperFile = Path.Combine(tempDir, "Helper.mh");
            File.WriteAllText(helperFile, "public struct Helper;");

            var project = MahoBuildSystem.LoadProject(projectFile);

            Assert.Equal(2, project.SourceFiles.Count);
            Assert.Contains(Path.GetFullPath(mainFile), project.SourceFiles);
            Assert.Contains(Path.GetFullPath(helperFile), project.SourceFiles);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
