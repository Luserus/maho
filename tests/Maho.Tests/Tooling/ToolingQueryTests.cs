using System;
using System.IO;
using System.Linq;
using Maho.Tooling;
using Xunit;

namespace Maho.Tests;

public class ToolingQueryTests : IDisposable
{
    private readonly string testDir;
    private readonly string preludePath;

    public ToolingQueryTests()
    {
        testDir = Path.Combine(Path.GetTempPath(), "MahoToolingTests_" + Guid.NewGuid().ToString("N"));
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

    private (Compilation Compilation, SemanticModel Model, string FilePath) Compile(string code)
    {
        string srcPath = Path.Combine(testDir, "source.mh");
        File.WriteAllText(srcPath, code);

        var comp = MahoCompiler.AnalyzeFiles([preludePath, srcPath]);
        if (comp.HasErrors)
            Assert.Fail(string.Join("; ", comp.Diagnostics.Select(d => $"{d.Code} at {d.SourcePath}:{d.Span.StartLocation.Line}: {d.Message}")));
        var model = comp.GetSemanticModel(srcPath);
        return (comp, model!, srcPath);
    }

    [Fact]
    public void FindDefinition_ResolvesFunctionDeclaration()
    {
        string code = """
            func Add(int32 a, int32 b) -> int32
            {
                return a;
            }

            func Main() -> int32
            {
                return Add(1, 2);
            }
            """;

        var (comp, model, srcPath) = Compile(code);
        Assert.False(comp.HasErrors);
        Assert.NotNull(model);

        // Line 8, Col 13 is "Add(1, 2)" call
        var def = model.FindDefinition(8, 13);
        Assert.NotNull(def);
        Assert.Equal("Add", def.SymbolName);
        Assert.Equal("Function", def.SymbolKind);
        Assert.Equal(1, def.Span.StartLocation.Line);
    }

    [Fact]
    public void FindDefinition_ResolvesStructDeclaration()
    {
        string code = """
            public struct Point
            {
                public int32 X;
                public int32 Y;
            }

            func MakePoint(Point p) -> Point
            {
                return p;
            }
            """;

        var (comp, model, srcPath) = Compile(code);
        Assert.False(comp.HasErrors);
        Assert.NotNull(model);

        // Line 7, Col 28 is return type "Point"
        var def = model.FindDefinition(7, 28);
        Assert.NotNull(def);
        Assert.Equal("Point", def.SymbolName);
        Assert.Equal("Type", def.SymbolKind);
        Assert.Equal(1, def.Span.StartLocation.Line);
    }

    [Fact]
    public void FindReferences_LocatesAllCallsAcrossCompilation()
    {
        string code = """
            func Multiply(int32 x, int32 y) -> int32
            {
                return x;
            }

            func Caller1() -> int32
            {
                return Multiply(2, 3);
            }

            func Caller2() -> int32
            {
                return Multiply(4, 5);
            }
            """;

        var (comp, model, srcPath) = Compile(code);
        Assert.False(comp.HasErrors);
        Assert.NotNull(model);

        // Position on 'Multiply' definition (Line 1, Col 7)
        var refs = model.FindReferences(1, 7, includeDeclaration: true);
        Assert.True(refs.Count >= 3); // Declaration + 2 callers
        Assert.Contains(refs, r => r.IsDeclaration);
        Assert.Contains(refs, r => !r.IsDeclaration && r.Span.StartLocation.Line == 8);
        Assert.Contains(refs, r => !r.IsDeclaration && r.Span.StartLocation.Line == 13);
    }

    [Fact]
    public void GetHover_ReturnsFormattedSignatureAndType()
    {
        string code = """
            /// Calculates the square of a number.
            func Square(int32 n) -> int32
            {
                return n;
            }

            func Main() -> int32
            {
                return Square(5);
            }
            """;

        var (comp, model, _) = Compile(code);
        Assert.False(comp.HasErrors);
        Assert.NotNull(model);

        // Hover over 'Square' at call site (Line 9, Col 13)
        var hover = model.GetHover(9, 13);
        Assert.NotNull(hover);
        Assert.Contains("Square", hover.DisplayString);
        Assert.Contains("int32", hover.DisplayString);
    }

    [Fact]
    public void GetDocumentSymbols_GeneratesHierarchy()
    {
        string code = """
            public struct Vector2
            {
                public int32 X;
                public int32 Y;
                func Length() -> int32 { return 0; }
            }

            func GlobalHelper() { }
            """;

        var (comp, model, srcPath) = Compile(code);
        Assert.False(comp.HasErrors);
        Assert.NotNull(model);

        var symbols = model.GetDocumentSymbols();
        Assert.NotEmpty(symbols);

        var structSym = Assert.Single(symbols, s => s.Name == "Vector2");
        Assert.Equal("struct", structSym.Kind);
        Assert.NotNull(structSym.Children);
        Assert.Contains(structSym.Children, c => c.Name == "X" && c.Kind == "field");
        Assert.Contains(structSym.Children, c => c.Name == "Y" && c.Kind == "field");
        Assert.Contains(structSym.Children, c => c.Name == "Length" && c.Kind == "method");

        Assert.Contains(symbols, s => s.Name == "GlobalHelper" && s.Kind == "function");
    }

    [Fact]
    public void GetCompletions_ReturnsKeywordsAndScopeSymbols()
    {
        string code = """
            func Helper() -> int32 { return 42; }

            func Main()
            {
                
            }
            """;

        var (comp, model, srcPath) = Compile(code);
        Assert.NotNull(model);

        // Inside Main body
        var completions = model.GetCompletions(5, 5);
        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "Helper" && c.Kind == "function");
        Assert.Contains(completions, c => c.Label == "return" && c.Kind == "keyword");
        Assert.Contains(completions, c => c.Label == "let" && c.Kind == "keyword");
    }

    [Fact]
    public void GetSignatureHelp_ReturnsActiveParameterIndex()
    {
        string code = """
            func Process(int32 first, int32 second, int32 third) -> int32
            {
                return first;
            }

            func Main() -> int32
            {
                return Process(10, 20, 30);
            }
            """;

        var (comp, model, _) = Compile(code);
        Assert.False(comp.HasErrors);
        Assert.NotNull(model);

        // Position on second argument (after first comma, around col 25)
        var sigHelp = model.GetSignatureHelp(8, 25);
        Assert.NotNull(sigHelp);
        Assert.NotEmpty(sigHelp.Signatures);
        Assert.Contains("Process", sigHelp.Signatures[0].Label);
        Assert.True(sigHelp.ActiveParameter >= 1);
    }

    [Fact]
    public void GetSemanticTokens_ClassifiesNodes()
    {
        string code = """
            func Add(int32 a, int32 b) -> int32
            {
                return a;
            }
            """;

        var (comp, model, srcPath) = Compile(code);
        Assert.False(comp.HasErrors);
        Assert.NotNull(model);

        var tokens = model.GetSemanticTokens();
        Assert.NotEmpty(tokens);
        Assert.Contains(tokens, t => t.TokenType == "function" || t.TokenType == "type" || t.TokenType == "variable" || t.TokenType == "parameter");
    }

    [Fact]
    public void GetSemanticTokens_SplitsLiteralStringsAndEscapeSequencesWithoutOverlap()
    {
        string code = """
            [IntrinsicType("Char")] public struct char;

            func Main() -> ()
            {
                var s = "hi\n\u{1F600}!";
                var c = '\t';
            }
            """;

        var (comp, model, _) = Compile(code);
        Assert.False(comp.HasErrors, string.Join("; ", comp.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.NotNull(model);

        var tokens = model.GetSemanticTokens();
        var escapeTokens = tokens.Where(t => t.TokenType == "escapeSequence").ToList();
        Assert.Equal(3, escapeTokens.Count); // \n, \u{1F600}, \t
        Assert.Contains(escapeTokens, t => t.Length == 2); // \n or \t
        Assert.Contains(escapeTokens, t => t.Length == 9); // \u{1F600}

        // Verify literal line segments do not overlap any token on the same line
        foreach (int line in tokens.Select(t => t.Line).Distinct())
        {
            var onLine = tokens.Where(t => t.Line == line).OrderBy(t => t.Column).ToList();
            for (int i = 1; i < onLine.Count; i++)
            {
                Assert.True(onLine[i - 1].Column + onLine[i - 1].Length <= onLine[i].Column,
                    $"Overlapping tokens on line {line}: {onLine[i - 1]} and {onLine[i]}");
            }
        }
    }
}
