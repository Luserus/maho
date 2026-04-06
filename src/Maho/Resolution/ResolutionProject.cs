using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>
/// Groups the parsed syntax tree and references that participate in one coordinated resolution run.
/// The tree boundary makes resolution an explicit post-parse phase.
/// </summary>
internal sealed class ResolutionProject
{
    /// <summary>
    /// Fully parsed syntax root for this resolution run. Resolution never starts until parsing has
    /// produced this complete project boundary.
    /// </summary>
    public SyntaxTree SyntaxTree { get; }
    /// <summary> Friendly project identity forwarded from the syntax tree. </summary>
    public string Name => SyntaxTree.Name;
    /// <summary> Convenience projection of the compilation units contained in the syntax tree. </summary>
    public CompilationUnit[] Units => SyntaxTree.Roots;
    /// <summary> Externally resolved project surfaces available to later semantic passes. </summary>
    public ResolutionProjectReference[] References { get; }

    /// <summary> Creates one resolution input package for a syntax tree plus optional references. </summary>
    public ResolutionProject(SyntaxTree syntaxTree, ResolutionProjectReference[]? references = null)
    {
        SyntaxTree = syntaxTree;
        References = references ?? [];
    }

    /// <summary> Convenience helper for callers that only need single-file resolution. </summary>
    public static ResolutionProject CreateSingleUnit(CompilationUnit unit, string name = "<single-file>") =>
        new(SyntaxTree.CreateSingleRoot(unit, name));
}
