namespace Maho;

/// <summary>
/// Provenance information for a diagnostic that originated within macro-expanded code.
/// </summary>
/// <param name="MacroName">The name of the macro that produced the offending syntax.</param>
/// <param name="InvocationSpan">The source span of the invocation site that triggered expansion.</param>
/// <param name="InvocationFilePath">Path to the source file containing the macro invocation.</param>
/// <param name="DefinitionSpan">The source span where the macro was declared.</param>
/// <param name="DefinitionFilePath">Path to the source file containing the macro definition.</param>
/// <param name="Parent">Enclosing macro expansion provenance when macros are nested.</param>
public sealed record MacroExpansionTraceInfo(
    string MacroName,
    TextSpanInfo InvocationSpan,
    string? InvocationFilePath,
    TextSpanInfo? DefinitionSpan = null,
    string? DefinitionFilePath = null,
    MacroExpansionTraceInfo? Parent = null);
