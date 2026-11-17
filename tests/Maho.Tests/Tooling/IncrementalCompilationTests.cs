using System;
using System.IO;
using Xunit;

namespace Maho.Tests;

public class IncrementalCompilationTests : IDisposable
{
    private readonly string testDir;
    private readonly string preludePath;

    public IncrementalCompilationTests()
    {
        testDir = Path.Combine(Path.GetTempPath(), "MahoIncrementalTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        preludePath = Path.Combine(testDir, "Prelude.mh");
        File.WriteAllText(preludePath, CompilerTestBed.FullPrelude);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void FromFiles_FullReuseShortCircuit_ReturnsExactSameInstance()
    {
        string file1 = Path.Combine(testDir, "A.mh");
        string file2 = Path.Combine(testDir, "B.mh");

        File.WriteAllText(file1, "public struct Point { public int32 X; }");
        File.WriteAllText(file2, "func GetOrigin() -> int32 { return 0; }");

        var comp1 = MahoCompiler.AnalyzeFiles([preludePath, file1, file2]);
        Assert.False(comp1.HasErrors);

        // Passing cachedCompilation without touching files should return the exact same reference
        var comp2 = MahoCompiler.AnalyzeFiles([preludePath, file1, file2], cachedCompilation: comp1);
        Assert.Same(comp1, comp2);
    }

    [Fact]
    public void FromFiles_IncrementalParsing_ReusesUnchangedSyntaxTrees()
    {
        string fileA = Path.Combine(testDir, "A.mh");
        string fileB = Path.Combine(testDir, "B.mh");
        string fileC = Path.Combine(testDir, "C.mh");

        File.WriteAllText(fileA, "public struct User { public int32 Id; }");
        File.WriteAllText(fileB, "func GetUser() -> int32 { return 42; }");
        File.WriteAllText(fileC, "func PrintUser() { }");

        var comp1 = MahoCompiler.AnalyzeFiles([preludePath, fileA, fileB, fileC]);
        Assert.False(comp1.HasErrors);

        var treeA1 = comp1.GetSyntaxTree(fileA);
        var treeB1 = comp1.GetSyntaxTree(fileB);
        var treeC1 = comp1.GetSyntaxTree(fileC);
        Assert.NotNull(treeA1);
        Assert.NotNull(treeB1);
        Assert.NotNull(treeC1);

        // Modify file C only
        System.Threading.Thread.Sleep(50); // Ensure timestamp advances
        File.WriteAllText(fileC, "func PrintUser() -> int32 { return 100; }");

        var comp2 = MahoCompiler.AnalyzeFiles([preludePath, fileA, fileB, fileC], cachedCompilation: comp1);
        Assert.False(comp2.HasErrors);

        var treeA2 = comp2.GetSyntaxTree(fileA);
        var treeB2 = comp2.GetSyntaxTree(fileB);
        var treeC2 = comp2.GetSyntaxTree(fileC);

        // A and B should be the exact same SyntaxTree instances reused from comp1
        Assert.Same(treeA1, treeA2);
        Assert.Same(treeB1, treeB2);

        // C was modified, so it should be a fresh SyntaxTree
        Assert.NotSame(treeC1, treeC2);
    }

    [Fact]
    public void FromFiles_BodyOnlyEdit_ReusesSemanticModelForUnaffectedTrees()
    {
        string fileA = Path.Combine(testDir, "A.mh");
        string fileB = Path.Combine(testDir, "B.mh");

        File.WriteAllText(fileA, "func Calculate() -> int32 { return 10; }");
        File.WriteAllText(fileB, "func Run() -> int32 { return Calculate(); }");

        var comp1 = MahoCompiler.AnalyzeFiles([preludePath, fileA, fileB]);
        Assert.False(comp1.HasErrors);

        var modelB1 = comp1.GetSemanticModel(fileB);
        Assert.NotNull(modelB1);

        // Edit function body in file A only (leaving public signature 'func Calculate() -> int32' identical)
        System.Threading.Thread.Sleep(50);
        File.WriteAllText(fileA, "func Calculate() -> int32 { return 20; }");

        var comp2 = MahoCompiler.AnalyzeFiles([preludePath, fileA, fileB], cachedCompilation: comp1);
        Assert.False(comp2.HasErrors);

        var modelB2 = comp2.GetSemanticModel(fileB);
        Assert.NotNull(modelB2);

        // File B's semantic model should be completely preserved and reused!
        Assert.Same(modelB1, modelB2);

        // File A's model should be updated
        var modelA1 = comp1.GetSemanticModel(fileA);
        var modelA2 = comp2.GetSemanticModel(fileA);
        Assert.NotSame(modelA1, modelA2);
    }

    [Fact]
    public void FromFiles_SignatureChange_RebuildsAffectedModels()
    {
        string fileA = Path.Combine(testDir, "A.mh");
        string fileB = Path.Combine(testDir, "B.mh");

        File.WriteAllText(fileA, "func Greet() -> int32 { return 1; }");
        File.WriteAllText(fileB, "func CallGreet() -> int32 { return Greet(); }");

        var comp1 = MahoCompiler.AnalyzeFiles([preludePath, fileA, fileB]);
        Assert.False(comp1.HasErrors);

        // Modify signature in file A (add parameter x: int32)
        System.Threading.Thread.Sleep(50);
        File.WriteAllText(fileA, "func Greet(int32 x) -> int32 { return x; }");

        var comp2 = MahoCompiler.AnalyzeFiles([preludePath, fileA, fileB], cachedCompilation: comp1);

        // Because signature changed, comp2 now correctly reports error in fileB (call with 0 args to 1-param func)
        Assert.True(comp2.HasErrors);
        Assert.Contains(comp2.Diagnostics, d => d.Message.Contains("argument") || d.Message.Contains("parameter") || d.Message.Contains("overload") || d.Message.Contains("candidates"));
    }

    [Fact]
    public void FromFiles_NonDestructiveOptions_ReusesCompilation()
    {
        string file = Path.Combine(testDir, "Source.mh");
        File.WriteAllText(file, "func Main() { }");

        var options1 = new CompilationOptions { WarningsAsErrors = false, ColorMode = DiagnosticColorMode.Never };
        var comp1 = MahoCompiler.AnalyzeFiles([preludePath, file], options: options1);

        var options2 = new CompilationOptions { WarningsAsErrors = true, ColorMode = DiagnosticColorMode.Always };
        var comp2 = MahoCompiler.AnalyzeFiles([preludePath, file], options: options2, cachedCompilation: comp1);

        // Semantic analysis preserved, only options updated
        Assert.Equal(DiagnosticColorMode.Always, comp2.Options.ColorMode);
        Assert.True(comp2.Options.WarningsAsErrors);
        Assert.Same(comp1.GetSyntaxTree(file), comp2.GetSyntaxTree(file));
    }

    [Fact]
    public void FromFiles_DestructiveOptions_TriggersReanalysis()
    {
        string file = Path.Combine(testDir, "Source.mh");
        File.WriteAllText(file, "func DoWork() { } DoWork();");

        var options1 = new CompilationOptions { ImplicitTopLevel = true, EntryFile = file };
        var comp1 = MahoCompiler.AnalyzeFiles([preludePath, file], options: options1);
        Assert.False(comp1.HasErrors);

        var options2 = new CompilationOptions { ImplicitTopLevel = false, EntryFile = file };
        var comp2 = MahoCompiler.AnalyzeFiles([preludePath, file], options: options2, cachedCompilation: comp1);

        // Disabling ImplicitTopLevel makes top-level statements invalid
        Assert.True(comp2.HasErrors);
    }

    [Fact]
    public void FromFiles_MetadataChanged_SameContent_ReusesSyntaxTree()
    {
        string file = Path.Combine(testDir, "SameContent.mh");
        File.WriteAllText(file, "func Ping() -> int32 { return 1; }");

        var comp1 = MahoCompiler.AnalyzeFiles([preludePath, file]);
        Assert.False(comp1.HasErrors);
        var tree1 = comp1.GetSyntaxTree(file);
        Assert.NotNull(tree1);

        // Advance write time by updating timestamp without changing content
        System.Threading.Thread.Sleep(50);
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));

        var comp2 = MahoCompiler.AnalyzeFiles([preludePath, file], cachedCompilation: comp1);
        Assert.False(comp2.HasErrors);
        var tree2 = comp2.GetSyntaxTree(file);

        // Content was identical, so cached tree is reused without re-parsing
        Assert.Same(tree1, tree2);
    }

    [Fact]
    public void FromSource_FullReuseShortCircuit_ReturnsSameCompilation()
    {
        string code = CompilerTestBed.FullPrelude + "\nfunc Add(int32 a, int32 b) -> int32 { return a; }";
        var comp1 = MahoCompiler.CompileSource(code);
        Assert.False(comp1.HasErrors);

        var comp2 = MahoCompiler.CompileSource(code, cachedCompilation: comp1);
        Assert.Same(comp1, comp2);
    }

    [Fact]
    public void Samples_IncrementalCompilation_ReusesCachedCompilation()
    {
        string dir = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(dir, "Samples")) && Directory.GetParent(dir) != null)
            dir = Directory.GetParent(dir)!.FullName;
        string samplesDir = Path.Combine(dir, "Samples");
        var files = Directory.GetFiles(samplesDir, "*.mh").OrderBy(x => x).ToList();
        var comp1 = MahoCompiler.AnalyzeFiles(files, samplesDir);
        var comp2 = MahoCompiler.AnalyzeFiles(files, samplesDir, cachedCompilation: comp1);
        Assert.Same(comp1, comp2);
    }
}
