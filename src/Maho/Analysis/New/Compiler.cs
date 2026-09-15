using Maho.Text;
using Maho.Diagnostics;
using Maho.Syntax;
using Maho.Resolution;

namespace Maho;


public struct ProjectConfigs
{
    public string ProjectName;
    public string EntryFile;


    public ProjectConfigs(string projectName, string entryFile)
    {
        ProjectName = projectName;
        EntryFile = entryFile;
    }

    public static ProjectConfigs FromMemory() => FromMemory("Program");

    public static ProjectConfigs FromMemory(string name) => new ProjectConfigs("<memory>", name);
}


public class Diagnostic
{
    public int ID;
    public string SourcePath;
    public DiagnosticSeverity Severity;
    public string? Help;
    public string? Note;

    public Diagnostic(string sourcePath)
    {
        SourcePath = sourcePath;
    }
}


public class SyntaxDiagnostic : Diagnostic
{
    public string Message;
    public TextSpanInfo Span;

    public SyntaxDiagnostic(string sourcePath, string message, TextSpanInfo span) : base(sourcePath)
    {
        Message = message;
        Span = span;
    }
}

public record DebugOutput(string Path, string Output);

public struct AnalysisResult
{
    public DebugOutput[] LexerOutputs;
    public DebugOutput[] ParserOutputs;
    public Diagnostic[] Diagnostics;

    public AnalysisResult()
    {
        LexerOutputs = [];
        ParserOutputs = [];
        Diagnostics = [];
    }
}

public static class Compiler
{
    public static AnalysisResult AnalyzeText(string program, ProjectConfigs configs)
    {
        var source = new SourceText(program);

        var diagnostics = new DiagnosticsManager(source);

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.Lex();

        var parser = new Parser(source, diagnostics);
        var root = parser.Parse(tokens);

        var resolver = new Resolver();
        resolver.Resolve(SyntaxTree.CreateSingleRoot(root, "<memory>"));

        return new AnalysisResult();
    }
}