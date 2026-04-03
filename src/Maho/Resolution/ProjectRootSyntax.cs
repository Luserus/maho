using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Synthetic syntax node used as the semantic boundary for project-wide resolution state. </summary>
internal sealed class ProjectRootSyntax : SyntaxNode
{
    public string ProjectName { get; }

    public ProjectRootSyntax(string projectName) => ProjectName = projectName;
}
