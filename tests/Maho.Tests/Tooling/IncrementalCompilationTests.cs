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

        GC.SuppressFinalize(this);
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

    [Fact]
    public void FromFiles_MacroEdit_ReexpandsUnchangedCallerAndClearsDiagnostics()
    {
        string macrosPath = Path.Combine(testDir, "Macros.mh");
        string callerPath = Path.Combine(testDir, "Caller.mh");

        // Run 1: Macro emits a broken expression causing a snippet parse error and type mismatch in unchanged Caller.mh
        File.WriteAllText(macrosPath, """
            public macro $Value {
                () => "not_an_int";
            }
            """);
        File.WriteAllText(callerPath, "func Compute() -> int32 { return $Value(); }");

        var comp1 = MahoCompiler.AnalyzeFiles([preludePath, macrosPath, callerPath]);
        Assert.True(comp1.HasErrors);
        Assert.NotEmpty(comp1.GetSyntaxTree(callerPath)!.Diagnostics);

        // Run 2: Fix Macros.mh WITHOUT touching Caller.mh
        System.Threading.Thread.Sleep(50);
        File.WriteAllText(macrosPath, """
            public macro $Value {
                () => 42;
            }
            """);

        var comp2 = MahoCompiler.AnalyzeFiles([preludePath, macrosPath, callerPath], cachedCompilation: comp1);
        Assert.False(comp2.HasErrors, string.Join("\n", comp2.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Empty(comp2.GetSyntaxTree(callerPath)!.Diagnostics);
        Assert.Empty(comp2.GetSyntaxTree(macrosPath)!.Diagnostics);

        // Prior compilation snapshot remains unmutated
        Assert.True(comp1.HasErrors);
        Assert.NotEmpty(comp1.GetSyntaxTree(callerPath)!.Diagnostics);

        // Run 3: Break Macros.mh again with invalid syntax WITHOUT touching Caller.mh -> caller must re-expand from unexpanded AST
        System.Threading.Thread.Sleep(50);
        File.WriteAllText(macrosPath, """
            public macro $Value {
                () => 1 + ;
            }
            """);

        var comp3 = MahoCompiler.AnalyzeFiles([preludePath, macrosPath, callerPath], cachedCompilation: comp2);
        Assert.True(comp3.HasErrors);
        Assert.NotEmpty(comp3.Diagnostics);
    }

    [Fact]
    public void FromFiles_MacroAddedLater_ResolvesPreviouslyUnresolvedMacroInUnchangedCaller()
    {
        string macrosPath = Path.Combine(testDir, "Macros.mh");
        string callerPath = Path.Combine(testDir, "Caller.mh");

        File.WriteAllText(macrosPath, "");
        File.WriteAllText(callerPath, """
            func Id(int32 x) -> int32 { return x; }
            func Calc() -> int32 { return $Wrap(42); }
            """);

        var comp1 = MahoCompiler.AnalyzeFiles([preludePath, macrosPath, callerPath]);
        Assert.True(comp1.HasErrors);
        Assert.Contains(comp1.GetSyntaxTree(callerPath)!.Diagnostics, d => d.Code == "MH0602");

        // Define $Wrap in Macros.mh without touching Caller.mh
        System.Threading.Thread.Sleep(50);
        File.WriteAllText(macrosPath, """
            public macro $Wrap {
                (@x: expr) => Id(@x);
            }
            """);

        var comp2 = MahoCompiler.AnalyzeFiles([preludePath, macrosPath, callerPath], cachedCompilation: comp1);
        Assert.False(comp2.HasErrors, string.Join("\n", comp2.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Empty(comp2.GetSyntaxTree(callerPath)!.Diagnostics);

        // Earlier snapshot still retains its MH0602 error
        Assert.Contains(comp1.GetSyntaxTree(callerPath)!.Diagnostics, d => d.Code == "MH0602");
    }

    [Fact]
    public void FromFiles_UnchangedFileDiagnostics_DoNotDuplicateOrStayStaleAcrossIncrementalBuilds()
    {
        string libPath = Path.Combine(testDir, "Lib.mh");
        string mainPath = Path.Combine(testDir, "Main.mh");

        File.WriteAllText(libPath, "func NeedArg(int32 x) -> int32 { return x; }");
        File.WriteAllText(mainPath, "func Run() -> int32 { return NeedArg(); }");

        var comp1 = MahoCompiler.AnalyzeFiles([preludePath, libPath, mainPath]);
        Assert.Single(comp1.GetSyntaxTree(mainPath)!.Diagnostics);

        // Edit Lib.mh in a way that keeps the same error in unchanged Main.mh -> must NOT duplicate
        System.Threading.Thread.Sleep(50);
        File.WriteAllText(libPath, "func NeedArg(int32 y) -> int32 { return y + 1; }");

        var comp2 = MahoCompiler.AnalyzeFiles([preludePath, libPath, mainPath], cachedCompilation: comp1);
        Assert.Single(comp2.GetSyntaxTree(mainPath)!.Diagnostics);

        // Fix Lib.mh without touching Main.mh -> stale diagnostic in Main.mh must clear completely
        System.Threading.Thread.Sleep(50);
        File.WriteAllText(libPath, "func NeedArg() -> int32 { return 1; }");

        var comp3 = MahoCompiler.AnalyzeFiles([preludePath, libPath, mainPath], cachedCompilation: comp2);
        Assert.False(comp3.HasErrors, string.Join("\n", comp3.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Empty(comp3.GetSyntaxTree(mainPath)!.Diagnostics);
        Assert.Single(comp1.GetSyntaxTree(mainPath)!.Diagnostics);
        Assert.Single(comp2.GetSyntaxTree(mainPath)!.Diagnostics);
    }

    [Fact]
    public void FromFiles_BlockMacroBodyEdit_ClearsMacroDiagnosticsAcrossFilesInSameServerCache()
    {
        string macrosPath = Path.Combine(testDir, "Macros.mh");
        string mainPath = Path.Combine(testDir, "Main.mh");

        File.WriteAllText(macrosPath, """
            public macro $box_val {
                (@x: expr) => {
                    MissingType __tmp = @x;
                    __tmp
                };
            }
            """);
        File.WriteAllText(mainPath, "func Run() -> int32 { return $box_val(10); }");

        var comp1 = MahoCompiler.AnalyzeFiles([preludePath, macrosPath, mainPath]);
        Assert.True(comp1.HasErrors);
        Assert.Contains(comp1.Diagnostics, d => d.Code == "MH0500");

        // Fix MissingType -> int32 in Macros.mh without touching Main.mh
        System.Threading.Thread.Sleep(50);
        File.WriteAllText(macrosPath, """
            public macro $box_val {
                (@x: expr) => {
                    int32 __tmp = @x;
                    __tmp
                };
            }
            """);

        var comp2 = MahoCompiler.AnalyzeFiles([preludePath, macrosPath, mainPath], cachedCompilation: comp1);
        Assert.False(comp2.HasErrors, string.Join("\n", comp2.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Empty(comp2.GetSyntaxTree(macrosPath)!.Diagnostics);
        Assert.Empty(comp2.GetSyntaxTree(mainPath)!.Diagnostics);
    }
}
