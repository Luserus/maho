using Miryo;

namespace Maho.Tests;

public sealed class MiryoTests
{
    [Fact]
    public void MiryoConfiguration_ParsesSectionsAndArrays()
    {
        string ini = """
            # Toolchain configuration
            [tools]
            mahoc = ["./dist/mahoc", "mahoc"]
            il2llvmir = ["./dist/il2llvmir"]

            [templates]
            bin_template = "EntryFile : \"src/Program.mh\";\n"
            """;

        var config = MiryoConfiguration.Parse(ini, "/some/dir");
        var candidates = config.GetToolCandidates("mahoc");

        Assert.Equal(2, candidates.Count);
        Assert.Equal("./dist/mahoc", candidates[0]);
        Assert.Equal("mahoc", candidates[1]);

        string tmpl = config.GetTemplate("bin_template");
        Assert.Equal("EntryFile : \"src/Program.mh\";\n", tmpl);
    }

    [Fact]
    public void MiryoConfiguration_EvaluatesVariablesAndConcatenation()
    {
        string ini = """
            [templates]
            bin_template = "ProjectName : \"" + project_name + "\";\n"
            lib_template = "Lib : \"${project_name}\";\n"
            """;

        var config = MiryoConfiguration.Parse(ini, "/some/dir");

        string binTmpl = config.GetTemplate("bin_template", new Dictionary<string, string>
        {
            ["project_name"] = "CoolApp"
        });
        Assert.Equal("ProjectName : \"CoolApp\";\n", binTmpl);

        string libTmpl = config.GetTemplate("lib_template", new Dictionary<string, string>
        {
            ["project_name"] = "CoolLib"
        });
        Assert.Equal("Lib : \"CoolLib\";\n", libTmpl);
    }

    [Fact]
    public void MiryoCommandLine_HelpFlag_PrintsUsage()
    {
        using var sw = new StringWriter();
        int exitCode = MiryoCommandLine.Run(["--help"], sw, sw);
        Assert.Equal(0, exitCode);
        string output = sw.ToString();
        Assert.Contains("Miryo: v", output);
        Assert.Contains("Usage: miryo", output);
    }

    [Fact]
    public void MiryoCommandLine_VersionFlag_PrintsVersion()
    {
        using var sw = new StringWriter();
        int exitCode = MiryoCommandLine.Run(["--version"], sw, sw);
        Assert.Equal(0, exitCode);
        Assert.Contains("Miryo: v", sw.ToString());
    }

    [Fact]
    public void MiryoCommandLine_NewCommand_ScaffoldsBinaryProject()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Miryo_NewBin_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(tempDir);
            using var sw = new StringWriter();
            int exitCode = MiryoCommandLine.Run(["new", "SampleApp"], sw, sw);
            Assert.Equal(0, exitCode);

            string projDir = Path.Combine(tempDir, "SampleApp");
            Assert.True(Directory.Exists(projDir));
            Assert.True(File.Exists(Path.Combine(projDir, "SampleApp.mhpr")));
            Assert.True(File.Exists(Path.Combine(projDir, "src", "Program.mh")));
            Assert.True(File.Exists(Path.Combine(projDir, ".gitignore")));

            string mhprContent = File.ReadAllText(Path.Combine(projDir, "SampleApp.mhpr"));
            Assert.Contains("EntryFile", mhprContent);
            Assert.Contains("ImplicitTopLevel", mhprContent);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MiryoCommandLine_NewCommand_ScaffoldsLibraryProject()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Miryo_NewLib_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(tempDir);
            using var sw = new StringWriter();
            int exitCode = MiryoCommandLine.Run(["new", "SampleLib", "--lib"], sw, sw);
            Assert.Equal(0, exitCode);

            string projDir = Path.Combine(tempDir, "SampleLib");
            Assert.True(Directory.Exists(projDir));
            Assert.True(File.Exists(Path.Combine(projDir, "SampleLib.mhpr")));
            Assert.True(File.Exists(Path.Combine(projDir, "src", "Lib.mh")));
            Assert.True(File.Exists(Path.Combine(projDir, ".gitignore")));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MiryoCommandLine_NewCommand_WhenConfigNotFound_OutputsWarningAndUsesDefaults()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Miryo_NewWarn_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(tempDir);
            using var sw = new StringWriter();
            int exitCode = MiryoCommandLine.Run(["new", "WarnApp"], sw, sw);
            Assert.Equal(0, exitCode);

            string output = sw.ToString();
            Assert.Contains("Configuration file not found. Using default built-in template.", output);
            Assert.Contains("Created Maho application project 'WarnApp' successfully.", output);

            string projDir = Path.Combine(tempDir, "WarnApp");
            Assert.True(File.Exists(Path.Combine(projDir, "WarnApp.mhpr")));
            Assert.True(File.Exists(Path.Combine(projDir, "src", "Program.mh")));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MiryoCommandLine_NewCommand_CustomConfig_UsesConfiguredTemplatesWithoutWarning()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Miryo_NewCustom_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(tempDir);
            string configFile = Path.Combine(tempDir, "custom.config");
            File.WriteAllText(configFile, """
                [templates]
                bin_template = "Project : \"" + project_name + "\";\nEntryFile : \"src/Main.mh\";\n"
                bin_source_path = "src/Main.mh"
                bin_source = "// App " + project_name + "\nprintln(\"Custom!\");\n"
                gitignore = "custom_target/\n"
                """);

            using var sw = new StringWriter();
            int exitCode = MiryoCommandLine.Run(["new", "CustomApp", "--config", configFile], sw, sw);
            Assert.Equal(0, exitCode);

            string output = sw.ToString();
            Assert.DoesNotContain("Configuration file not found", output);
            Assert.Contains("Created Maho application project 'CustomApp' successfully.", output);

            string projDir = Path.Combine(tempDir, "CustomApp");
            Assert.True(Directory.Exists(projDir));

            string mhprContent = File.ReadAllText(Path.Combine(projDir, "CustomApp.mhpr"));
            Assert.Equal("Project : \"CustomApp\";\nEntryFile : \"src/Main.mh\";\n", mhprContent);

            string sourceContent = File.ReadAllText(Path.Combine(projDir, "src", "Main.mh"));
            Assert.Equal("// App CustomApp\nprintln(\"Custom!\");\n", sourceContent);

            string gitignoreContent = File.ReadAllText(Path.Combine(projDir, ".gitignore"));
            Assert.Equal("custom_target/\n", gitignoreContent);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MiryoCommandLine_NewCommand_ExplicitMissingConfig_OutputsWarningAndUsesDefaults()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Miryo_NewMissing_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(tempDir);
            string missingConfig = Path.Combine(tempDir, "non_existent.config");

            using var sw = new StringWriter();
            int exitCode = MiryoCommandLine.Run(["new", "FallbackApp", "--config", missingConfig], sw, sw);
            Assert.Equal(0, exitCode);

            string output = sw.ToString();
            Assert.Contains("Configuration file", output);
            Assert.Contains("not found. Using default built-in template.", output);
            Assert.Contains("Created Maho application project 'FallbackApp' successfully.", output);

            string projDir = Path.Combine(tempDir, "FallbackApp");
            Assert.True(Directory.Exists(projDir));
            Assert.True(File.Exists(Path.Combine(projDir, "FallbackApp.mhpr")));
            Assert.True(File.Exists(Path.Combine(projDir, "src", "Program.mh")));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MiryoCommandLine_Build_FailsWithPipelineNotImplementedMH9000()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Miryo_BuildFail_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(tempDir);
            string projectFile = Path.Combine(tempDir, "App.mhpr");
            string programFile = Path.Combine(tempDir, "Program.mh");
            File.WriteAllText(projectFile, "EntryFile : \"Program.mh\";\nImplicitTopLevel : true;\n");
            File.WriteAllText(programFile, "call();");

            // Point config to the compiled mahoc.dll
            string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
            string mahocDll = Path.Combine(repoRoot, "src/MahoCli/bin/Debug/net10.0/mahoc.dll");
            string configFile = Path.Combine(tempDir, "miryo.config");
            File.WriteAllText(configFile, $"""
                [tools]
                mahoc = ["{mahocDll}"]
                """);

            using var swOut = new StringWriter();
            using var swErr = new StringWriter();
            int exitCode = MiryoCommandLine.Run(["build", "--config", configFile, tempDir], swOut, swErr);

            // Per specification: lowering pipeline not implemented -> exit code 1
            Assert.Equal(1, exitCode);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MiryoCommandLine_Check_SucceedsWithExitCodeZero()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Miryo_CheckOk_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(tempDir);
            string projectFile = Path.Combine(tempDir, "App.mhpr");
            string programFile = Path.Combine(tempDir, "Program.mh");
            File.WriteAllText(projectFile, "EntryFile : \"Program.mh\";\nImplicitTopLevel : true;\n");
            File.WriteAllText(programFile, "public struct Widget;");

            string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
            string mahocDll = Path.Combine(repoRoot, "src/MahoCli/bin/Debug/net10.0/mahoc.dll");
            string configFile = Path.Combine(tempDir, "miryo.config");
            File.WriteAllText(configFile, $"""
                [tools]
                mahoc = ["{mahocDll}"]
                """);

            using var swOut = new StringWriter();
            using var swErr = new StringWriter();
            int exitCode = MiryoCommandLine.Run(["check", "--config", configFile, tempDir], swOut, swErr);

            Assert.Equal(0, exitCode);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MiryoCommandLine_Clean_RemovesTargetAndObjDirectories()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Miryo_Clean_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, "target"));
        Directory.CreateDirectory(Path.Combine(tempDir, "obj"));

        try
        {
            using var sw = new StringWriter();
            int exitCode = MiryoCommandLine.Run(["clean", tempDir], sw, sw);
            Assert.Equal(0, exitCode);

            Assert.False(Directory.Exists(Path.Combine(tempDir, "target")));
            Assert.False(Directory.Exists(Path.Combine(tempDir, "obj")));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
