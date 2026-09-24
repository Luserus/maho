using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Maho;

/// <summary>
/// Source-generated JsonSerializerContext providing reflection-free, trim-safe JSON serialization.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(DebugLexerInfo))]
[JsonSerializable(typeof(DebugParserInfo))]
[JsonSerializable(typeof(IReadOnlyList<DiagnosticInfo>))]
[JsonSerializable(typeof(List<DiagnosticInfo>))]
[JsonSerializable(typeof(DiagnosticInfo))]
internal partial class MahoJsonContext : JsonSerializerContext
{
}
