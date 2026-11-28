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
    /// <summary>Immutable diagnostics emitted during lexing and parsing of this file.</summary>
    internal DiagnosticsManager SyntaxDiagnostics { get; }
    /// <summary>Resolution and macro-expansion diagnostics scoped to this compilation snapshot.</summary>
    internal IReadOnlyList<Diagnostic> ResolutionDiagnostics { get; }
    internal List<Token>? Tokens { get; }
    internal bool AllowImplicitTopLevel { get; }
    internal bool WasMacroExpanded { get; set; }
    internal bool ContainsMacroSyntax => Root.ContainsMacroSyntax;

    /// <summary>Diagnostics associated with this source file only.</summary>
    public IReadOnlyList<DiagnosticInfo> Diagnostics
    {
        get
        {
            var diagnostics = new List<DiagnosticInfo>(SyntaxDiagnostics.Diagnostics.Count + ResolutionDiagnostics.Count);
            foreach (var diagnostic in SyntaxDiagnostics.Diagnostics)
                diagnostics.Add(DiagnosticInfo.FromDiagnostic(diagnostic, SourceText, FilePath));
            foreach (var diagnostic in ResolutionDiagnostics)
                diagnostics.Add(DiagnosticInfo.FromDiagnostic(diagnostic, SourceText, FilePath));
            return DiagnosticInfo.OrderDiagnostics(diagnostics);
        }
    }

    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary> SHA256 content hash of the underlying source text. </summary>
    public string ContentHash => SourceText.ContentHash;

    /// <summary> Timestamp when the file was last written on disk, or null for in-memory text. </summary>
    public System.DateTime? LastWriteTimeUtc
    {
        get => SourceText.LastWriteTimeUtc;
        set => SourceText.LastWriteTimeUtc = value;
    }

    /// <summary> File size in bytes recorded when loaded from disk, or null for in-memory text. </summary>
    public long? FileLength
    {
        get => SourceText.FileLength;
        set => SourceText.FileLength = value;
    }

    /// <summary>
    /// Checks whether this syntax tree matches the current file on disk.
    /// Fast-checks file metadata (last modification time and file size) first without reading file content or hashing.
    /// If metadata changed, hashes the disk file and compares with <see cref="ContentHash"/>; if hashes match,
    /// updates the tree's recorded metadata and avoids re-analysis.
    /// </summary>
    /// <param name="currentDiskPath">The file path on disk to check against.</param>
    /// <returns><see langword="true"/> if the content on disk is equivalent to this syntax tree; otherwise <see langword="false"/>.</returns>
    public bool IsEquivalent(string currentDiskPath)
    {
        if (!string.Equals(FilePath, currentDiskPath, System.StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var fi = new System.IO.FileInfo(currentDiskPath);
            if (!fi.Exists)
                return false;

            // 1. Fast path: If file metadata (last modification time and size) did not change, ignore and reuse directly.
            if (LastWriteTimeUtc.HasValue && LastWriteTimeUtc.Value == fi.LastWriteTimeUtc &&
                (!FileLength.HasValue || FileLength.Value == fi.Length))
            {
                return true;
            }

            // // 2. Metadata changed: hash only if metadata changed to determine whether content actually changed.
            // string diskHash;
            // using (var stream = System.IO.File.OpenRead(currentDiskPath))
            // {
            //     var hashBytes = System.Security.Cryptography.SHA256.HashData(stream);
            //     diskHash = System.Convert.ToHexString(hashBytes);
            // }

            // if (string.Equals(ContentHash, diskHash, System.StringComparison.OrdinalIgnoreCase))
            // {
            //     // Hashes match despite metadata difference: update cached metadata so subsequent checks hit fast path.
            //     LastWriteTimeUtc = fi.LastWriteTimeUtc;
            //     FileLength = fi.Length;
            //     return true;
            // }

            // Hashes do not match: content changed, file must be analyzed.
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether this syntax tree has identical source content as another syntax tree.
    /// </summary>
    /// <param name="other">The other syntax tree to compare content hashes with.</param>
    /// <returns><see langword="true"/> if both trees have matching content hashes; otherwise <see langword="false"/>.</returns>
    public bool IsEquivalent(SyntaxTree other) =>
        other != null && string.Equals(ContentHash, other.ContentHash, System.StringComparison.OrdinalIgnoreCase);

    internal SyntaxTree(
        CompilationUnit root,
        SourceText sourceText,
        DiagnosticsManager syntaxDiagnostics,
        string filePath,
        IReadOnlyList<Diagnostic>? resolutionDiagnostics = null,
        List<Token>? tokens = null,
        bool allowImplicitTopLevel = false,
        bool wasMacroExpanded = false)
    {
        Root = root;
        SourceText = sourceText;
        SyntaxDiagnostics = syntaxDiagnostics;
        ResolutionDiagnostics = resolutionDiagnostics ?? [];
        FilePath = filePath;
        Tokens = tokens;
        AllowImplicitTopLevel = allowImplicitTopLevel;
        WasMacroExpanded = wasMacroExpanded;
    }

    internal static SyntaxTree CreateSingleRoot(CompilationUnit root, string name = "<single-file>")
    {
        var source = new SourceText(string.Empty, name);
        return new SyntaxTree(root, source, new DiagnosticsManager(source), name);
    }
}
