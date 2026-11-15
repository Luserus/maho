using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Miryo.Build;

/// <summary>
/// Represents a loaded Miryo workspace comprising member projects, sub-workspaces, and dependency graph.
/// </summary>
public sealed class MiryoWorkspace
{
    /// <summary>
    /// Logical name of the workspace.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Directory containing the workspace file.
    /// </summary>
    public string WorkspaceDirectory { get; }

    /// <summary>
    /// Full path to the <c>.mrws</c> workspace file.
    /// </summary>
    public string WorkspaceFilePath { get; }

    /// <summary>
    /// The parsed configuration object.
    /// </summary>
    public MiryoWorkspaceConfiguration Configuration { get; }

    /// <summary>
    /// All flattened member projects participating in this workspace hierarchy.
    /// </summary>
    public IReadOnlyList<MahoProject> Projects { get; }

    /// <summary>
    /// Map of project names to member projects.
    /// </summary>
    public IReadOnlyDictionary<string, MahoProject> ProjectsByName { get; }

    /// <summary>
    /// Target directory for all output artifacts produced within this workspace.
    /// </summary>
    public string TargetDirectory { get; }

    /// <summary>
    /// Dependency graph and topological build scheduler for member projects.
    /// </summary>
    public ProjectDependencyGraph DependencyGraph { get; }

    /// <summary>
    /// Nested child workspaces included by this workspace.
    /// </summary>
    public IReadOnlyList<MiryoWorkspace> ChildWorkspaces { get; }

    public MiryoWorkspace(
        string name,
        string workspaceDirectory,
        string workspaceFilePath,
        MiryoWorkspaceConfiguration configuration,
        IReadOnlyList<MahoProject> projects,
        IReadOnlyList<MiryoWorkspace> childWorkspaces,
        string? customTargetDirectory = null)
    {
        Name = name;
        WorkspaceDirectory = workspaceDirectory;
        WorkspaceFilePath = workspaceFilePath;
        Configuration = configuration;
        Projects = projects;
        ChildWorkspaces = childWorkspaces;

        var byName = new Dictionary<string, MahoProject>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in projects)
            byName[p.ProjectName] = p;
        ProjectsByName = byName;

        string target = customTargetDirectory
            ?? configuration.TargetDirectory
            ?? "target";

        TargetDirectory = Path.IsPathRooted(target)
            ? Path.GetFullPath(target)
            : Path.GetFullPath(Path.Combine(workspaceDirectory, target));

        DependencyGraph = new ProjectDependencyGraph(projects);
    }
}

