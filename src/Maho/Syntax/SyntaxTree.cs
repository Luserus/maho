using System.Collections.Generic;
using System.Linq;
using Maho.Diagnostics;
using Maho.Text;

namespace Maho.Syntax;

/// <summary>The parsed representation of one source file.</summary>
public sealed class SyntaxTree
{
    public string FilePath { get; }
    internal SourceText SourceText { get; }
    internal CompilationUnit Root { get; }
    internal DiagnosticsManager DiagnosticsManager { get; }

    /// <summary>Diagnostics associated with this source file only.</summary>
    public IReadOnlyList<DiagnosticInfo> Diagnostics
    {
        get
        {
            var diagnostics = new List<DiagnosticInfo>(DiagnosticsManager.Diagnostics.Count);
            foreach (var diagnostic in DiagnosticsManager.Diagnostics)
                diagnostics.Add(DiagnosticInfo.FromDiagnostic(diagnostic, SourceText, FilePath));
            return DiagnosticInfo.OrderDiagnostics(diagnostics);
        }
    }

    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    internal SyntaxTree(CompilationUnit root, SourceText sourceText, DiagnosticsManager diagnostics, string filePath)
    {
        Root = root;
        SourceText = sourceText;
        DiagnosticsManager = diagnostics;
        FilePath = filePath;
    }

    internal static SyntaxTree CreateSingleRoot(CompilationUnit root, string name = "<single-file>")
    {
        var source = new SourceText(string.Empty, name);
        return new SyntaxTree(root, source, new DiagnosticsManager(source), name);
    }
}
