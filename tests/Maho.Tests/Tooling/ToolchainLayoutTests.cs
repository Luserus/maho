using System.Text.Json.Nodes;
using Maho.Server;
using Miryo;
using Miryo.Toolchain;
using CompilerManifest = Miryo.Toolchain.CompilerManifest;

namespace Maho.Tests;

public sealed class ToolchainLayoutTests
{
    [Fact]
    public void Compilation_ToJson_ReturnsValidJsonStructure()
    {
        var compilation = MahoCompiler.CompileSource("var x = 42;", "test.mh");
        string json = compilation.ToJson();

        Assert.False(string.IsNullOrWhiteSpace(json));
        var node = JsonNode.Parse(json);
        Assert.NotNull(node);
        Assert.NotNull(node["files"]);
        Assert.NotNull(node["hasErrors"]);
        Assert.NotNull(node["elapsedTicks"]);
    }

    [Fact]
    public void SyntaxTree_ToJson_ReturnsValidJson()
    {
        var compilation = MahoCompiler.CompileSource("var y = 100;", "sample.mh");
        Assert.NotEmpty(compilation.SyntaxTrees);

        var tree = compilation.SyntaxTrees[0];
        string json = tree.ToJson();

        Assert.False(string.IsNullOrWhiteSpace(json));
        var node = JsonNode.Parse(json);
        Assert.NotNull(node);
        Assert.Equal("sample.mh", node["filePath"]?.ToString());
    }

    [Fact]
    public void VersionFlags_MatchToolchainDirectoryVersion()
    {
        // -v flag reports full version with patch
        Assert.Matches(@"^\d+\.\d+\.\d+", CommandLine.VersionString);
        Assert.Matches(@"^\d+\.\d+\.\d+", MiryoCommandLine.VersionString);

        // Toolchain directory version matches major.minor
        Assert.Equal("0.2", CommandLine.ToolchainVersion);
        Assert.Equal("1.0", MiryoCommandLine.ToolchainVersion);
    }

    [Fact]
    public void MahoPaths_NormalizeVersion_ProducesVersionMatchingFlag()
    {
        Assert.Equal("1.0", MahoPaths.NormalizeVersion("1.0.0"));
        Assert.Equal("1.0", MahoPaths.NormalizeVersion("1.0"));
        Assert.Equal("1.2", MahoPaths.NormalizeVersion("v1.2.3"));
        Assert.Equal("2.1", MahoPaths.NormalizeVersion("2.1"));
        Assert.Equal("3.0", MahoPaths.NormalizeVersion("3"));
        Assert.Equal("0.2", MahoPaths.NormalizeVersion("0.2.0"));
        Assert.Equal("0.2", MahoPaths.NormalizeVersion("0.2"));
    }

    [Fact]
    public void MahoPaths_ResolvesExpectedLayoutHierarchy()
    {
        string tempHome = Path.Combine(Path.GetTempPath(), "maho_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(Path.Combine(tempHome, "bin"), MahoPaths.GetBinDir(tempHome));
            Assert.Equal(Path.Combine(tempHome, "tools"), MahoPaths.GetToolsDir(tempHome));
            Assert.Equal(Path.Combine(tempHome, "tools", "0.2"), MahoPaths.GetToolDir("0.2", tempHome));
            Assert.Equal(Path.Combine(tempHome, "tools", "0.2"), MahoPaths.GetToolDir("0.2.0", tempHome));
            Assert.Equal(Path.Combine(tempHome, "shims"), MahoPaths.GetShimsDir(tempHome));
            Assert.Equal(Path.Combine(tempHome, "compiler", "0.2"), MahoPaths.GetCompilerDir("0.2", tempHome));
            Assert.Equal(Path.Combine(tempHome, "compiler", "0.2"), MahoPaths.GetCompilerDir("0.2.0", tempHome));
            Assert.Equal(Path.Combine(tempHome, "compiler", "0.2"), MahoPaths.GetCompilerSdkDir("0.2", tempHome));
            Assert.Equal(Path.Combine(tempHome, "compiler", "0.2"), MahoPaths.GetCompilerSdkDir("0.2.0", tempHome));
            Assert.Equal(Path.Combine(tempHome, "runtime"), MahoPaths.GetRuntimeDir(tempHome));
            Assert.Equal(Path.Combine(tempHome, "server"), MahoPaths.GetServerDir(tempHome));
            Assert.Equal(Path.Combine(tempHome, "server", "manifest.json"), MahoPaths.GetServerManifestPath(tempHome));
            Assert.Equal(Path.Combine(tempHome, "server", "manifest.json"), MahoPaths.GetRootManifestPath(tempHome));
        }
        finally
        {
            if (Directory.Exists(tempHome))
                Directory.Delete(tempHome, true);
        }
    }

    [Fact]
    public void MahoPaths_FindServerManager_FindsExecutableInServerDirectory()
    {
        string tempHome = Path.Combine(Path.GetTempPath(), "maho_sm_" + Guid.NewGuid().ToString("N"));
        string serverDir = Path.Combine(tempHome, "server");
        Directory.CreateDirectory(serverDir);
        try
        {
            string exeName = OperatingSystem.IsWindows() ? "server-manager.exe" : "server-manager";
            string exePath = Path.Combine(serverDir, exeName);
            File.WriteAllText(exePath, "#!/bin/sh\n");

            string? found = MahoPaths.FindServerManager(tempHome);
            Assert.NotNull(found);
            Assert.Equal(exePath, found);
        }
        finally
        {
            Directory.Delete(tempHome, true);
        }
    }

    [Fact]
    public void Manifests_SaveAndLoad_RoundtripsAccurately()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "maho_manifest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string rootPath = Path.Combine(tempDir, "manifest.json");
            var root = new ToolchainRootManifest("0.2", ["0.2", "0.3"], ["0.2"]);
            root.Save(rootPath);

            var loaded = ToolchainRootManifest.Load(rootPath);
            Assert.Equal("0.2", loaded.ActiveCompilerVersion);
            Assert.Equal(2, loaded.InstalledTools.Count);
            Assert.Single(loaded.InstalledSdkCompilers);

            string toolManifestPath = Path.Combine(tempDir, "tools", "0.2", "manifest.json");
            var tool = new ToolManifest("0.2.0", "0.2", new Dictionary<string, string>
            {
                ["mahoc"] = "mahoc",
                ["maho-server"] = "maho-server"
            });
            tool.Save(toolManifestPath);

            var loadedTool = ToolManifest.Load(toolManifestPath);
            Assert.Equal("0.2.0", loadedTool.Version);
            Assert.Equal("0.2", loadedTool.TargetCompilerVersion);
            Assert.Equal("mahoc", loadedTool.Tools["mahoc"]);

            string compPath = Path.Combine(tempDir, "compiler", "0.2", "manifest.json");
            var comp = new CompilerManifest("0.2.0", "Maho.dll");
            comp.Save(compPath);

            var loadedComp = CompilerManifest.Load(compPath);
            Assert.Equal("0.2.0", loadedComp.Version);
            Assert.Equal("Maho.dll", loadedComp.Assembly);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MiryoInvocation_ParsesBracketedPropertiesAndFlags()
    {
        string[] args = ["compile", "[v: 0.2]", "--check", "--color", "never"];
        var inv = MiryoInvocation.Parse(args, defaultVersion: "0.2");

        Assert.Equal("compile", inv.Command);
        Assert.Equal("0.2", inv.TargetVersion);
        Assert.Equal("0.2", inv.Properties["v"]);
        Assert.Equal(["--check", "--color", "never"], inv.Arguments);
    }

    [Fact]
    public void MiryoInvocation_DefaultVersion_UsesActiveWhenNoPropertyProvided()
    {
        string[] args = ["build", "-o", "bin/"];
        var inv = MiryoInvocation.Parse(args, defaultVersion: "0.2");

        Assert.Equal("build", inv.Command);
        Assert.Equal("0.2", inv.TargetVersion);
        Assert.Equal(["-o", "bin/"], inv.Arguments);
    }

    [Fact]
    public void MiryoInvocation_MultipleBracketProperties()
    {
        string[] args = ["compile", "[v: 0.2, target: release]", "main.mh"];
        var inv = MiryoInvocation.Parse(args);

        Assert.Equal("compile", inv.Command);
        Assert.Equal("0.2", inv.TargetVersion);
        Assert.Equal("release", inv.Properties["target"]);
        Assert.Equal(["main.mh"], inv.Arguments);
    }

    [Fact]
    public void LocalToolchainManager_CreatesShims()
    {
        string tempHome = Path.Combine(Path.GetTempPath(), "maho_shims_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempHome);
        try
        {
            var mgr = new LocalToolchainManager(tempHome);
            mgr.UpdateShims("0.2");

            string shimsDir = MahoPaths.GetShimsDir(tempHome);
            Assert.True(Directory.Exists(shimsDir));

            if (OperatingSystem.IsWindows())
            {
                Assert.True(File.Exists(Path.Combine(shimsDir, "mahoc.cmd")));
            }
            else
            {
                Assert.True(File.Exists(Path.Combine(shimsDir, "mahoc")));
            }
        }
        finally
        {
            Directory.Delete(tempHome, true);
        }
    }

    [Fact]
    public void MiryoConfiguration_LoadsFromColocatedBinDirectory()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "miryo_cfg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string cfgPath = Path.Combine(tempDir, "miryo.config");
            File.WriteAllText(cfgPath, @"
[tools]
mahoc = [""../tools/0.2/mahoc""]
mahoc_server = [""../tools/0.2/maho-server""]

[templates]
bin_template = ""EntryFile : \""src/Program.mh\"";\n""
");

            var config = MiryoConfiguration.Load(cfgPath);
            Assert.True(config.IsLoadedFromDisk);
            Assert.Null(config.LoadWarning);
            Assert.Equal("EntryFile : \"src/Program.mh\";\n", config.GetTemplate("bin_template"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
