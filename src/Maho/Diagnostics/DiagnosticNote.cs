using Maho.Text;

namespace Maho.Diagnostics;

/// <summary>
/// Additional contextual information attached to a diagnostic.
/// </summary>
internal readonly record struct DiagnosticNote(
    string Message,
    SourceText? Source = null,
    TextSpan? Span = null
);

/// <summary>
/// Actionable advice or remediation hint attached to a diagnostic.
/// </summary>
internal readonly record struct DiagnosticHelp(
    string Message,
    SourceText? Source = null,
    TextSpan? Span = null
);
