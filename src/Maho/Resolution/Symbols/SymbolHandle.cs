global using SymbolHandle = Maho.Resolution.SymbolHandle;

using System;

namespace Maho.Resolution;

/// <summary>
/// A uniquely identifying handle for a symbol across compilation projects and modules.
/// </summary>
internal readonly record struct SymbolHandle(SymbolKind Kind, SymbolID ID, int ProjectId = 0) : IEquatable<SymbolHandle>
{
    public static implicit operator SymbolHandle((SymbolKind Kind, SymbolID ID) tuple) => new(tuple.Kind, tuple.ID);
    public static implicit operator (SymbolKind Kind, SymbolID ID)(SymbolHandle handle) => (handle.Kind, handle.ID);

    public void Deconstruct(out SymbolKind kind, out SymbolID id) => (kind, id) = (Kind, ID);
    public void Deconstruct(out SymbolKind kind, out SymbolID id, out int projectId) => (kind, id, projectId) = (Kind, ID, ProjectId);

    public bool Equals(SymbolHandle other)
    {
        if (Kind != other.Kind || ID != other.ID)
            return false;
        if (ProjectId == 0 || other.ProjectId == 0)
            return true;
        return ProjectId == other.ProjectId;
    }

    public override int GetHashCode() => HashCode.Combine(Kind, ID);

    public override string ToString() => ProjectId == 0 ? $"({Kind}, {ID})" : $"({Kind}, {ID}, Project {ProjectId})";
}

