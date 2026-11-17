using System;
using System.IO;
using System.Linq;
using Miryo;
using Miryo.Build;
using Xunit;

namespace Maho.Tests;

public sealed class WorkspaceBuildTests
{
    [Fact]
    public void WorkspaceParser_ValidWorkspace_ParsesProperties()
    {
        string wsDsl = """
            Name : "TestWorkspace";
            Members : [
                "src/Core/Core.mhpr",
                "src/Cli",
                "samples/*"
            ];
            Default : [ "Cli" ];
            Exclude : [ "samples/Broken" ];
            TargetDirectory : "build/out";
            Shared : {
                GlobalAliases : {
                    Int : "Std.Int32",
                    String : "Std.String"
                },
                GlobalUnsafeEnabled : true
            };
            """;

        var config = MahoProjectFileParser.ParseWorkspace(wsDsl);

        Assert.Equal("TestWorkspace", config.Name);
        Assert.Equal(3, config.Members.Length);
        Assert.Equal("src/Core/Core.mhpr", config.Members[0]);
        Assert.Equal("src/Cli", config.Members[1]);
        Assert.Equal("samples/*", config.Members[2]);
        Assert.Single(config.Default);
        Assert.Equal("Cli", config.Default[0]);
        Assert.Single(config.Exclude);
        Assert.Equal("samples/Broken", config.Exclude[0]);
        Assert.Equal("build/out", config.TargetDirectory);
        Assert.NotNull(config.Shared);
        Assert.True(config.Shared!.GlobalUnsafeEnabled);
        Assert.Equal(2, config.Shared.GlobalAliases.Count);
        Assert.Equal("Std.Int32", config.Shared.GlobalAliases["Int"]);
        Assert.Equal("Std.String", config.Shared.GlobalAliases["String"]);
    }

    [Fact]
    public void WorkspaceParser_SyntaxErrors_ThrowsInvalidOperationException()
    {
        string invalidKeyDsl = """
            Name : "BadWorkspace";
            UnknownProp : 123;
            """;

        var ex1 = Assert.Throws<MahoProjectParseException>(() => MahoProjectFileParser.ParseWorkspace(invalidKeyDsl));
        Assert.Contains("Unknown workspace property 'UnknownProp'", ex1.Message);

        string missingSemicolonDsl = """
            Name : "BadWorkspace"
            Members : [ "A" ];
            """;

        var ex2 = Assert.Throws<MahoProjectParseException>(() => MahoProjectFileParser.ParseWorkspace(missingSemicolonDsl));
        Assert.Contains("Expected ';'", ex2.Message);
    }

    [Fact]
    public void WorkspaceLoader_SingleWorkspace_DiscoversProjectsAndMembers()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "MiryoWs_Discover_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string proj1Dir = Path.Combine(tempDir, "src", "Core");
            Directory.CreateDirectory(proj1Dir);
            File.WriteAllText(Path.Combine(proj1Dir, "Core.mhpr"), "EntryFile : \"Core.mh\";\nImplicitTopLevel : true;\n");
            File.WriteAllText(Path.Combine(proj1Dir, "Core.mh"), "public struct CoreService;");

            string proj2Dir = Path.Combine(tempDir, "src", "App");
            Directory.CreateDirectory(proj2Dir);
            File.WriteAllText(Path.Combine(proj2Dir, "App.mhpr"), "EntryFile : \"Main.mh\";\nProjectsReferenced : [ \"../Core/Core.mhpr\" ];\n");
            File.WriteAllText(Path.Combine(proj2Dir, "Main.mh"), "public struct AppWidget;");

            string wsFile = Path.Combine(tempDir, "Maho.mrws");
            File.WriteAllText(wsFile, """
                Name : "MySuite";
                Members : [
                    "src/Core",
                    "src/App/App.mhpr"
                ];
                Default : [ "App" ];
                """);

            var ws = MiryoBuildSystem.LoadWorkspace(wsFile);

            Assert.Equal("MySuite", ws.Name);
            Assert.Equal(2, ws.Projects.Count);
            Assert.NotNull(ws.DependencyGraph.FindProject("Core"));
            Assert.NotNull(ws.DependencyGraph.FindProject("App"));
            Assert.Equal(Path.Combine(tempDir, "target"), ws.TargetDirectory);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void WorkspaceLoader_NestedWorkspaces_RecursivelyFlattensAndRebasesPaths()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "MiryoWs_Nested_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Sub-workspace in sub/
            string subDir = Path.Combine(tempDir, "sub");
            string subProjDir = Path.Combine(subDir, "SubLib");
            Directory.CreateDirectory(subProjDir);
            File.WriteAllText(Path.Combine(subProjDir, "SubLib.mhpr"), "EntryFile : \"Lib.mh\";\nImplicitTopLevel : true;\n");
            File.WriteAllText(Path.Combine(subProjDir, "Lib.mh"), "public struct SubLibItem;");

            string subWsFile = Path.Combine(subDir, "Sub.mrws");
            File.WriteAllText(subWsFile, """
                Name : "SubWorkspace";
                Members : [ "SubLib" ];
                """);

            // Root project in root/
            string rootProjDir = Path.Combine(tempDir, "RootApp");
            Directory.CreateDirectory(rootProjDir);
            File.WriteAllText(Path.Combine(rootProjDir, "RootApp.mhpr"), "EntryFile : \"Root.mh\";\nImplicitTopLevel : true;\n");
            File.WriteAllText(Path.Combine(rootProjDir, "Root.mh"), "public struct RootAppItem;");

            // Root workspace referencing sub/Sub.mrws
            string rootWsFile = Path.Combine(tempDir, "Root.mrws");
            File.WriteAllText(rootWsFile, """
                Name : "RootWorkspace";
                Members : [
                    "RootApp",
                    "sub/Sub.mrws"
                ];
                """);

            var rootWs = MiryoBuildSystem.LoadWorkspace(rootWsFile);

            Assert.Equal("RootWorkspace", rootWs.Name);
            Assert.Equal(2, rootWs.Projects.Count);
            Assert.NotNull(rootWs.DependencyGraph.FindProject("RootApp"));
            Assert.NotNull(rootWs.DependencyGraph.FindProject("SubLib"));
            Assert.Single(rootWs.ChildWorkspaces);
            Assert.Equal("SubWorkspace", rootWs.ChildWorkspaces[0].Name);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void WorkspaceLoader_CyclicWorkspaces_ThrowsInvalidOperationException()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "MiryoWs_WsCycle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string wsA = Path.Combine(tempDir, "A.mrws");
            string wsB = Path.Combine(tempDir, "B.mrws");

            File.WriteAllText(wsA, """
                Name : "WorkspaceA";
                Members : [ "B.mrws" ];
                """);

            File.WriteAllText(wsB, """
                Name : "WorkspaceB";
                Members : [ "A.mrws" ];
                """);

            var ex = Assert.Throws<InvalidOperationException>(() => MiryoBuildSystem.LoadWorkspace(wsA));
            Assert.Contains("Cyclic workspace", ex.Message);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ProjectDependencyGraph_TopologicalTiers_OrdersLeavesFirst()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "MiryoWs_Graph_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string dirA = Path.Combine(tempDir, "A");
            string dirB = Path.Combine(tempDir, "B");
            string dirC = Path.Combine(tempDir, "C");
            Directory.CreateDirectory(dirA);
            Directory.CreateDirectory(dirB);
            Directory.CreateDirectory(dirC);

            File.WriteAllText(Path.Combine(dirA, "A.mhpr"), "EntryFile : \"A.mh\";\n");
            File.WriteAllText(Path.Combine(dirA, "A.mh"), "public struct A;");

            File.WriteAllText(Path.Combine(dirB, "B.mhpr"), "EntryFile : \"B.mh\";\nProjectsReferenced : [ \"../A/A.mhpr\" ];\n");
            File.WriteAllText(Path.Combine(dirB, "B.mh"), "public struct B;");

            File.WriteAllText(Path.Combine(dirC, "C.mhpr"), "EntryFile : \"C.mh\";\nProjectsReferenced : [ \"../B/B.mhpr\" ];\n");
            File.WriteAllText(Path.Combine(dirC, "C.mh"), "public struct C;");

            string wsFile = Path.Combine(tempDir, "Workspace.mrws");
            File.WriteAllText(wsFile, """
                Name : "GraphWs";
                Members : [ "A", "B", "C" ];
                """);

            var ws = MiryoBuildSystem.LoadWorkspace(wsFile);
            var tiers = ws.DependencyGraph.Tiers;

            Assert.Equal(3, tiers.Count);
            Assert.Equal("A", tiers[0][0].ProjectName);
            Assert.Equal("B", tiers[1][0].ProjectName);
            Assert.Equal("C", tiers[2][0].ProjectName);

            // Test execution plan targeting only B
            var planB = ws.DependencyGraph.GetExecutionPlan(["B"]);
            Assert.Equal(2, planB.Count);
            Assert.Equal("A", planB[0][0].ProjectName);
            Assert.Equal("B", planB[1][0].ProjectName);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ProjectDependencyGraph_CyclicProjects_ThrowsInvalidOperationException()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "MiryoWs_ProjCycle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string dirP1 = Path.Combine(tempDir, "P1");
            string dirP2 = Path.Combine(tempDir, "P2");
            Directory.CreateDirectory(dirP1);
            Directory.CreateDirectory(dirP2);

            File.WriteAllText(Path.Combine(dirP1, "P1.mhpr"), "EntryFile : \"P1.mh\";\nProjectsReferenced : [ \"../P2/P2.mhpr\" ];\n");
            File.WriteAllText(Path.Combine(dirP1, "P1.mh"), "public struct P1;");

            File.WriteAllText(Path.Combine(dirP2, "P2.mhpr"), "EntryFile : \"P2.mh\";\nProjectsReferenced : [ \"../P1/P1.mhpr\" ];\n");
            File.WriteAllText(Path.Combine(dirP2, "P2.mh"), "public struct P2;");

            string wsFile = Path.Combine(tempDir, "Workspace.mrws");
            File.WriteAllText(wsFile, """
                Name : "CycleWs";
                Members : [ "P1", "P2" ];
                """);

            var ex = Assert.Throws<InvalidOperationException>(() => MiryoBuildSystem.LoadWorkspace(wsFile));
            Assert.Contains("Cyclic project dependency detected in workspace", ex.Message);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MiryoCommandLine_WorkspaceBuild_OutputFormatAndSummary()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "MiryoWs_CliBuild_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(tempDir);

            // Project Lib
            string libDir = Path.Combine(tempDir, "Lib");
            Directory.CreateDirectory(libDir);
            File.WriteAllText(Path.Combine(libDir, "Lib.mhpr"), "EntryFile : \"Lib.mh\";\nImplicitTopLevel : true;\n");
            File.WriteAllText(Path.Combine(libDir, "Lib.mh"), "public struct CoreLib;");

            // Project App referencing Lib
            string appDir = Path.Combine(tempDir, "App");
            Directory.CreateDirectory(appDir);
            File.WriteAllText(Path.Combine(appDir, "App.mhpr"), "EntryFile : \"App.mh\";\nImplicitTopLevel : true;\nProjectsReferenced : [ \"../Lib/Lib.mhpr\" ];\n");
            File.WriteAllText(Path.Combine(appDir, "App.mh"), "public struct AppMain;");

            // Project Extra (independent)
            string extraDir = Path.Combine(tempDir, "Extra");
            Directory.CreateDirectory(extraDir);
            File.WriteAllText(Path.Combine(extraDir, "Extra.mhpr"), "EntryFile : \"Extra.mh\";\nImplicitTopLevel : true;\n");
            File.WriteAllText(Path.Combine(extraDir, "Extra.mh"), "public struct ExtraMain;");

            string wsFile = Path.Combine(tempDir, "MahoSuite.mrws");
            File.WriteAllText(wsFile, """
                Name : "MahoSuite";
                Members : [ "Lib", "App", "Extra" ];
                Default : [ "App" ];
                """);

            string mahocDll = Path.Combine(AppContext.BaseDirectory, "mahoc.dll");
            string configFile = Path.Combine(tempDir, "miryo.config");
            File.WriteAllText(configFile, $"""
                [tools]
                mahoc = ["{mahocDll}"]
                """);

            using var swOut = new StringWriter();
            using var swErr = new StringWriter();
            // Default target is "App" which references "Lib". "Extra" should be skipped.
            int exitCode = MiryoCommandLine.Run(["build", "--color", "never", "--check", "--config", configFile, wsFile], swOut, swErr);

            Assert.Equal(0, exitCode);
            string output = swOut.ToString();

            // Verify header
            Assert.Contains("Building workspace 'MahoSuite' (3 projects)", output);
            Assert.Contains($"From: {wsFile}", output);

            // Verify tier indexed building
            Assert.Contains("[1/2] Building 'Lib' (Tier 0)...", output);
            Assert.Contains("[2/2] Building 'App' (Tier 1)...", output);
            Assert.Contains("Analyzing project configuration...", output);
            Assert.Contains("Executing core compile...", output);
            Assert.Contains("Errors: 0, Warnings: 0", output);

            // Verify final summary
            Assert.Contains("workspace build succeeded", output);
            Assert.Contains("succeeded: 2, skipped: 1, failed: 0", output);
            Assert.Contains("errors: 0, warnings: 0", output);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MiryoCommandLine_WorkspaceBuild_AllFlag_BuildsAllProjects()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "MiryoWs_CliAll_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(tempDir);

            string p1Dir = Path.Combine(tempDir, "P1");
            Directory.CreateDirectory(p1Dir);
            File.WriteAllText(Path.Combine(p1Dir, "P1.mhpr"), "EntryFile : \"P1.mh\";\nImplicitTopLevel : true;\n");
            File.WriteAllText(Path.Combine(p1Dir, "P1.mh"), "public struct P1;");

            string p2Dir = Path.Combine(tempDir, "P2");
            Directory.CreateDirectory(p2Dir);
            File.WriteAllText(Path.Combine(p2Dir, "P2.mhpr"), "EntryFile : \"P2.mh\";\nImplicitTopLevel : true;\n");
            File.WriteAllText(Path.Combine(p2Dir, "P2.mh"), "public struct P2;");

            string wsFile = Path.Combine(tempDir, "Suite.mrws");
            File.WriteAllText(wsFile, """
                Name : "Suite";
                Members : [ "P1", "P2" ];
                Default : [ "P1" ];
                """);

            string mahocDll = Path.Combine(AppContext.BaseDirectory, "mahoc.dll");
            string configFile = Path.Combine(tempDir, "miryo.config");
            File.WriteAllText(configFile, $"""
                [tools]
                mahoc = ["{mahocDll}"]
                """);

            using var swOut = new StringWriter();
            using var swErr = new StringWriter();
            int exitCode = MiryoCommandLine.Run(["build", "--all", "--check", "--config", configFile, wsFile], swOut, swErr);

            Assert.Equal(0, exitCode);
            string output = swOut.ToString();

            Assert.Contains("[1/2] Building", output);
            Assert.Contains("[2/2] Building", output);
            Assert.Contains("succeeded: 2, skipped: 0, failed: 0", output);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MiryoCommandLine_WorkspaceClean_CleansTargetAndProjects()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "MiryoWs_Clean_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string projDir = Path.Combine(tempDir, "ProjA");
            Directory.CreateDirectory(projDir);
            File.WriteAllText(Path.Combine(projDir, "ProjA.mhpr"), "EntryFile : \"A.mh\";\n");
            File.WriteAllText(Path.Combine(projDir, "A.mh"), "public struct A;");

            // Create fake build directories
            Directory.CreateDirectory(Path.Combine(tempDir, "target"));
            Directory.CreateDirectory(Path.Combine(projDir, "bin"));
            Directory.CreateDirectory(Path.Combine(projDir, "obj"));

            string wsFile = Path.Combine(tempDir, "Workspace.mrws");
            File.WriteAllText(wsFile, """
                Name : "CleanSuite";
                Members : [ "ProjA" ];
                """);

            using var swOut = new StringWriter();
            using var swErr = new StringWriter();
            int exitCode = MiryoCommandLine.Run(["clean", wsFile], swOut, swErr);

            Assert.Equal(0, exitCode);
            Assert.False(Directory.Exists(Path.Combine(tempDir, "target")));
            Assert.False(Directory.Exists(Path.Combine(projDir, "bin")));
            Assert.False(Directory.Exists(Path.Combine(projDir, "obj")));
            Assert.Contains("cleaned 3 build artifact directories", swOut.ToString());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MiryoCommandLine_WorkspaceBuild_DependencyFailure_DownstreamSkippedAndDiagnosticsAtEnd()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "MiryoWs_FailDownstream_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(tempDir);

            // BadLib has syntax/type error
            string badDir = Path.Combine(tempDir, "BadLib");
            Directory.CreateDirectory(badDir);
            File.WriteAllText(Path.Combine(badDir, "BadLib.mhpr"), "EntryFile : \"Bad.mh\";\nImplicitTopLevel : true;\n");
            File.WriteAllText(Path.Combine(badDir, "Bad.mh"), "public struct Bad { invalid syntax ??? }");

            // GoodApp depends on BadLib
            string goodDir = Path.Combine(tempDir, "GoodApp");
            Directory.CreateDirectory(goodDir);
            File.WriteAllText(Path.Combine(goodDir, "GoodApp.mhpr"), "EntryFile : \"Good.mh\";\nImplicitTopLevel : true;\nProjectsReferenced : [ \"../BadLib/BadLib.mhpr\" ];\n");
            File.WriteAllText(Path.Combine(goodDir, "Good.mh"), "public struct GoodWidget;");

            string wsFile = Path.Combine(tempDir, "Maho.mrws");
            File.WriteAllText(wsFile, """
                Name : "FailSuite";
                Members : [ "BadLib", "GoodApp" ];
                Default : [ "GoodApp" ];
                """);

            string mahocDll = Path.Combine(AppContext.BaseDirectory, "mahoc.dll");
            string configFile = Path.Combine(tempDir, "miryo.config");
            File.WriteAllText(configFile, $"""
                [tools]
                mahoc = ["{mahocDll}"]
                """);

            using var swOut = new StringWriter();
            using var swErr = new StringWriter();
            int exitCode = MiryoCommandLine.Run(["build", "--color", "never", "--check", "--config", configFile, wsFile], swOut, swErr);

            Assert.Equal(1, exitCode);
            string output = swOut.ToString();
            string errOutput = swErr.ToString();

            // Verify BadLib built and failed
            Assert.Contains("[1/2] Building 'BadLib' (Tier 0)...", output);
            Assert.Contains("Errors: 9, Warnings: 0", output);

            // Verify GoodApp skipped due to BadLib
            Assert.Contains("[2/2] Skipping 'GoodApp' (Tier 1) due to failure in 'BadLib'", output);

            // Verify summary stats
            Assert.Contains("workspace build failed", output);
            Assert.Contains("succeeded: 0, skipped: 1, failed: 1", output);
            Assert.Contains("errors: 9, warnings: 0", output);

            // Verify diagnostics block displayed at the end with project path
            Assert.Contains("compiler errors in '", errOutput);
            Assert.Contains("BadLib.mhpr", errOutput);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MiryoCommandLine_WorkspaceBuild_ProjectFlag_BuildsOnlyTargetAndDeps()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "MiryoWs_ProjFlag_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(tempDir);

            string aDir = Path.Combine(tempDir, "A");
            Directory.CreateDirectory(aDir);
            File.WriteAllText(Path.Combine(aDir, "A.mhpr"), "EntryFile : \"A.mh\";\nImplicitTopLevel : true;\n");
            File.WriteAllText(Path.Combine(aDir, "A.mh"), "public struct A;");

            string bDir = Path.Combine(tempDir, "B");
            Directory.CreateDirectory(bDir);
            File.WriteAllText(Path.Combine(bDir, "B.mhpr"), "EntryFile : \"B.mh\";\nImplicitTopLevel : true;\nProjectsReferenced : [ \"../A/A.mhpr\" ];\n");
            File.WriteAllText(Path.Combine(bDir, "B.mh"), "public struct B;");

            string cDir = Path.Combine(tempDir, "C");
            Directory.CreateDirectory(cDir);
            File.WriteAllText(Path.Combine(cDir, "C.mhpr"), "EntryFile : \"C.mh\";\nImplicitTopLevel : true;\n");
            File.WriteAllText(Path.Combine(cDir, "C.mh"), "public struct C;");

            string wsFile = Path.Combine(tempDir, "Workspace.mrws");
            File.WriteAllText(wsFile, """
                Name : "ProjectWs";
                Members : [ "A", "B", "C" ];
                """);

            string mahocDll = Path.Combine(AppContext.BaseDirectory, "mahoc.dll");
            string configFile = Path.Combine(tempDir, "miryo.config");
            File.WriteAllText(configFile, $"""
                [tools]
                mahoc = ["{mahocDll}"]
                """);

            using var swOut = new StringWriter();
            using var swErr = new StringWriter();
            int exitCode = MiryoCommandLine.Run(["build", "-p", "B", "--color", "never", "--check", "--config", configFile, wsFile], swOut, swErr);

            Assert.Equal(0, exitCode);
            string output = swOut.ToString();

            // Total 3 projects in workspace, plan has 2 (A and B). C is skipped.
            Assert.Contains("[1/2] Building 'A' (Tier 0)...", output);
            Assert.Contains("[2/2] Building 'B' (Tier 1)...", output);
            Assert.DoesNotContain("Building 'C'", output);
            Assert.Contains("succeeded: 2, skipped: 1, failed: 0", output);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MiryoCommandLine_WorkspaceBuild_ExcludeFlag_ExcludesProject()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "MiryoWs_ExclFlag_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(tempDir);

            string p1Dir = Path.Combine(tempDir, "P1");
            Directory.CreateDirectory(p1Dir);
            File.WriteAllText(Path.Combine(p1Dir, "P1.mhpr"), "EntryFile : \"P1.mh\";\nImplicitTopLevel : true;\n");
            File.WriteAllText(Path.Combine(p1Dir, "P1.mh"), "public struct P1;");

            string p2Dir = Path.Combine(tempDir, "P2");
            Directory.CreateDirectory(p2Dir);
            File.WriteAllText(Path.Combine(p2Dir, "P2.mhpr"), "EntryFile : \"P2.mh\";\nImplicitTopLevel : true;\n");
            File.WriteAllText(Path.Combine(p2Dir, "P2.mh"), "public struct P2;");

            string wsFile = Path.Combine(tempDir, "Workspace.mrws");
            File.WriteAllText(wsFile, """
                Name : "ExclWs";
                Members : [ "P1", "P2" ];
                """);

            string mahocDll = Path.Combine(AppContext.BaseDirectory, "mahoc.dll");
            string configFile = Path.Combine(tempDir, "miryo.config");
            File.WriteAllText(configFile, $"""
                [tools]
                mahoc = ["{mahocDll}"]
                """);

            using var swOut = new StringWriter();
            using var swErr = new StringWriter();
            int exitCode = MiryoCommandLine.Run(["build", "--all", "--exclude", "P2", "--color", "never", "--check", "--config", configFile, wsFile], swOut, swErr);

            Assert.Equal(0, exitCode);
            string output = swOut.ToString();

            Assert.Contains("[1/1] Building 'P1'", output);
            Assert.DoesNotContain("Building 'P2'", output);
            Assert.Contains("succeeded: 1, skipped: 1, failed: 0", output);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MiryoCommandLine_WorkspaceRun_RequiresProjectIfMultiple()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "MiryoWs_RunMultiple_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string p1Dir = Path.Combine(tempDir, "P1");
            Directory.CreateDirectory(p1Dir);
            File.WriteAllText(Path.Combine(p1Dir, "P1.mhpr"), "EntryFile : \"P1.mh\";\nImplicitTopLevel : true;\n");
            File.WriteAllText(Path.Combine(p1Dir, "P1.mh"), "public struct P1;");

            string p2Dir = Path.Combine(tempDir, "P2");
            Directory.CreateDirectory(p2Dir);
            File.WriteAllText(Path.Combine(p2Dir, "P2.mhpr"), "EntryFile : \"P2.mh\";\nImplicitTopLevel : true;\n");
            File.WriteAllText(Path.Combine(p2Dir, "P2.mh"), "public struct P2;");

            string wsFile = Path.Combine(tempDir, "Workspace.mrws");
            File.WriteAllText(wsFile, """
                Name : "MultiWs";
                Members : [ "P1", "P2" ];
                """);

            using var swOut = new StringWriter();
            using var swErr = new StringWriter();
            int exitCode = MiryoCommandLine.Run(["run", wsFile], swOut, swErr);

            Assert.Equal(1, exitCode);
            Assert.Contains("workspace 'MultiWs' contains multiple projects. Specify which project to run using '-p <project_name>'", swErr.ToString());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
