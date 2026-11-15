using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Miryo.Build;

/// <summary>
/// Represents a dependency graph (DAG) of workspace projects with cycle detection and topological tier scheduling.
/// </summary>
public sealed class ProjectDependencyGraph
{
    private readonly Dictionary<string, MahoProject> projectsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MahoProject> projectsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<MahoProject, HashSet<MahoProject>> directDependencies = [];
    private readonly Dictionary<MahoProject, HashSet<MahoProject>> directDependents = [];

    /// <summary>
    /// All projects participating in this dependency graph.
    /// </summary>
    public IReadOnlyList<MahoProject> AllProjects { get; }

    /// <summary>
    /// Projects partitioned into topological execution tiers. Projects within the same tier have no inter-dependencies.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<MahoProject>> Tiers { get; }

    public ProjectDependencyGraph(IEnumerable<MahoProject> projects)
    {
        AllProjects = [.. projects];

        foreach (var project in AllProjects)
        {
            string canonicalPath = Path.GetFullPath(project.ProjectFilePath);
            projectsByPath[canonicalPath] = project;

            if (projectsByName.TryGetValue(project.ProjectName, out MahoProject? existing))
            {
                throw new InvalidOperationException(
                    $"Duplicate project name '{project.ProjectName}' found in workspace:\n" +
                    $"  - {existing.ProjectFilePath}\n" +
                    $"  - {project.ProjectFilePath}\n" +
                    $"Project names in a workspace must be unique.");
            }

            projectsByName[project.ProjectName] = project;
            directDependencies[project] = [];
            directDependents[project] = [];
        }

        // Build edges
        foreach (var project in AllProjects)
        {
            foreach (string refPath in project.ProjectsReferenced)
            {
                string resolvedPath = Path.IsPathRooted(refPath)
                    ? Path.GetFullPath(refPath)
                    : Path.GetFullPath(Path.Combine(project.ProjectDirectory, refPath));

                // If resolved path is a directory, probe for project file inside it
                if (Directory.Exists(resolvedPath))
                {
                    string? innerFile = MiryoBuildSystem.FindProjectFile(resolvedPath);
                    if (innerFile != null)
                        resolvedPath = innerFile;
                }

                if (projectsByPath.TryGetValue(resolvedPath, out var referencedProject))
                {
                    directDependencies[project].Add(referencedProject);
                    directDependents[referencedProject].Add(project);
                }
            }
        }

        DetectCycles();
        Tiers = ComputeTiers(AllProjects);
    }

    /// <summary>
    /// Finds a workspace project by its logical name or project file path.
    /// </summary>
    public MahoProject? FindProject(string nameOrPath)
    {
        if (projectsByName.TryGetValue(nameOrPath, out var proj))
            return proj;

        string fullPath = Path.GetFullPath(nameOrPath);
        if (projectsByPath.TryGetValue(fullPath, out proj))
            return proj;

        return null;
    }

    /// <summary>
    /// Returns direct workspace dependencies of the given project.
    /// </summary>
    public IReadOnlySet<MahoProject> GetDirectDependencies(MahoProject project) =>
        directDependencies.TryGetValue(project, out var deps) ? deps : new HashSet<MahoProject>();

    /// <summary>
    /// Returns all transitive workspace dependencies of the given project in dependency order.
    /// </summary>
    public IReadOnlySet<MahoProject> GetTransitiveDependencies(MahoProject project)
    {
        var visited = new HashSet<MahoProject>();
        TraverseDependencies(project, visited);
        visited.Remove(project);
        return visited;
    }

    private void TraverseDependencies(MahoProject current, HashSet<MahoProject> visited)
    {
        if (!visited.Add(current))
            return;

        if (directDependencies.TryGetValue(current, out var deps))
        {
            foreach (var dep in deps)
                TraverseDependencies(dep, visited);
        }
    }

    /// <summary>
    /// Computes a pruned topological execution plan containing only the targeted projects and their required transitive dependencies.
    /// </summary>
    /// <param name="targetProjectNames">Names or paths of the target projects to build.</param>
    /// <returns>A filtered list of topological execution tiers containing only necessary projects.</returns>
    public IReadOnlyList<IReadOnlyList<MahoProject>> GetExecutionPlan(IEnumerable<string> targetProjectNames)
    {
        var targets = new HashSet<MahoProject>();
        var missing = new List<string>();

        foreach (string name in targetProjectNames)
        {
            var p = FindProject(name);
            if (p != null)
                targets.Add(p);
            else
                missing.Add(name);
        }

        if (missing.Count > 0)
        {
            string available = string.Join(", ", projectsByName.Keys.OrderBy(k => k));
            throw new InvalidOperationException(
                $"Project{(missing.Count == 1 ? string.Empty : "s")} '{string.Join("', '", missing)}' not found in workspace.\n" +
                $"Available projects: {available}");
        }

        // Collect all targets and their transitive dependencies
        var requiredProjects = new HashSet<MahoProject>();
        foreach (var target in targets)
        {
            requiredProjects.Add(target);
            foreach (var dep in GetTransitiveDependencies(target))
                requiredProjects.Add(dep);
        }

        return ComputeTiers(requiredProjects);
    }

    private void DetectCycles()
    {
        var visited = new HashSet<MahoProject>();
        var inStack = new HashSet<MahoProject>();
        var path = new List<MahoProject>();

        foreach (var project in AllProjects)
        {
            if (!visited.Contains(project))
            {
                if (DfsCycleCheck(project, visited, inStack, path, out var cyclePath))
                {
                    string cycleTrace = string.Join(" -> ", cyclePath.Select(p => $"'{p.ProjectName}'"));
                    throw new InvalidOperationException($"Cyclic project dependency detected in workspace: {cycleTrace}");
                }
            }
        }
    }

    private bool DfsCycleCheck(
        MahoProject current,
        HashSet<MahoProject> visited,
        HashSet<MahoProject> inStack,
        List<MahoProject> path,
        out List<MahoProject> cyclePath)
    {
        visited.Add(current);
        inStack.Add(current);
        path.Add(current);

        if (directDependencies.TryGetValue(current, out var deps))
        {
            foreach (var dep in deps)
            {
                if (!visited.Contains(dep))
                {
                    if (DfsCycleCheck(dep, visited, inStack, path, out cyclePath))
                        return true;
                }
                else if (inStack.Contains(dep))
                {
                    int startIndex = path.IndexOf(dep);
                    cyclePath = path.Skip(startIndex).Concat([dep]).ToList();
                    return true;
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        inStack.Remove(current);
        cyclePath = [];
        return false;
    }

    private IReadOnlyList<IReadOnlyList<MahoProject>> ComputeTiers(IEnumerable<MahoProject> candidateProjects)
    {
        var subset = new HashSet<MahoProject>(candidateProjects);
        var inDegree = new Dictionary<MahoProject, int>();

        foreach (var p in subset)
        {
            int internalDeps = directDependencies[p].Count(dep => subset.Contains(dep));
            inDegree[p] = internalDeps;
        }

        var tiers = new List<IReadOnlyList<MahoProject>>();
        var resolved = new HashSet<MahoProject>();

        while (resolved.Count < subset.Count)
        {
            var currentTier = new List<MahoProject>();

            foreach (var p in subset)
            {
                if (!resolved.Contains(p) && inDegree[p] == 0)
                    currentTier.Add(p);
            }

            if (currentTier.Count == 0)
            {
                // Should not happen if DetectCycles succeeded, but guard against any edge cases
                var remaining = subset.Where(p => !resolved.Contains(p)).Select(p => p.ProjectName);
                throw new InvalidOperationException($"Unresolvable dependency cycle among projects: {string.Join(", ", remaining)}");
            }

            currentTier.Sort((a, b) => string.Compare(a.ProjectName, b.ProjectName, StringComparison.Ordinal));
            tiers.Add(currentTier);

            foreach (var p in currentTier)
            {
                resolved.Add(p);

                foreach (var dep in directDependents[p])
                {
                    if (subset.Contains(dep) && inDegree.ContainsKey(dep))
                        inDegree[dep]--;
                }
            }
        }

        return tiers;
    }
}
