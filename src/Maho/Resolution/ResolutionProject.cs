using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Groups the compilation units and references that participate in one coordinated resolution run. </summary>
internal sealed class ResolutionProject
{
    public string Name { get; }
    public IReadOnlyList<CompilationUnit> Units { get; }
    public IReadOnlyList<ResolutionProjectReference> References { get; }

    public ResolutionProject(string name, IReadOnlyList<CompilationUnit> units, IReadOnlyList<ResolutionProjectReference>? references = null)
    {
        Name = name;
        Units = units;
        References = references ?? [];
    }

    public static ResolutionProject CreateSingleUnit(CompilationUnit unit, string name = "<single-file>") =>
        new(name, [unit]);
}
