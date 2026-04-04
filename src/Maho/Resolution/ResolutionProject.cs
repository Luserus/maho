using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>
/// Groups the parsed syntax tree and references that participate in one coordinated resolution run.
/// The tree boundary makes resolution an explicit post-parse phase.
/// </summary>
internal sealed class ResolutionProject
{
    public SyntaxTree SyntaxTree { get; }
    public string Name => SyntaxTree.Name;
    public IReadOnlyList<CompilationUnit> Units => SyntaxTree.Roots;
    public IReadOnlyList<ResolutionProjectReference> References { get; }

    public ResolutionProject(SyntaxTree syntaxTree, IReadOnlyList<ResolutionProjectReference>? references = null)
    {
        SyntaxTree = syntaxTree;
        References = references ?? [];
    }

    public static ResolutionProject CreateSingleUnit(CompilationUnit unit, string name = "<single-file>") =>
        new(SyntaxTree.CreateSingleRoot(unit, name));
}
