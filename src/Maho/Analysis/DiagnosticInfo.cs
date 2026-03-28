namespace Maho;

/// <summary>
/// Public diagnostic payload exposed to API consumers.
/// </summary>
/// <param name="Code">Stable diagnostic identifier intended for tooling and tests.</param>
/// <param name="Message">Human-readable description of the problem.</param>
/// <param name="Severity">Normalized severity category for the problem.</param>
/// <param name="Span">Location data expressed as both offsets and line/column endpoints.</param>
public sealed record DiagnosticInfo(string Code, string Message, DiagnosticSeverity Severity, TextSpanInfo Span);
