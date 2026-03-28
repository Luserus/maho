namespace Maho;

public sealed record DiagnosticInfo(string Code, string Message, DiagnosticSeverity Severity, TextSpanInfo Span);
