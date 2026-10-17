using System;
using System.Collections.Generic;
using System.Text;

namespace Maho;

/// <summary>One in-memory MHIL instruction.</summary>
public readonly record struct IlInstruction(string OpCode, string? Operand = null)
{
    public override string ToString() => Operand is null ? OpCode : $"{OpCode} {Operand}";
}

/// <summary>Compiled output owned by a <see cref="Compilation"/>.</summary>
public sealed class CompiledArtifact
{
    public static CompiledArtifact Empty { get; } = new([]);
    public IReadOnlyList<IlInstruction> Instructions { get; }

    public CompiledArtifact(IReadOnlyList<IlInstruction> instructions) => Instructions = instructions;

    /// <summary>Serializes the instruction stream for a binary IL consumer.</summary>
    public byte[] ToInstructions() => Encoding.UTF8.GetBytes(ToString());

    /// <summary>Produces the stable text form suitable for an IL file.</summary>
    public override string ToString() => string.Join(Environment.NewLine, Instructions);
}

/// <summary>Placeholder for future semantic-model queries.</summary>
public sealed class SemanticModel
{
    internal SemanticModel() { }
}
