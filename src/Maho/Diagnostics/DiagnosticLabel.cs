using Maho.Text;

namespace Maho.Diagnostics;

/// <summary>
/// Indicates the rendering style and visual priority of a diagnostic label.
/// </summary>
public enum DiagnosticLabelStyle : byte
{
    /// <summary> Rendered with carets (^^^^) using the diagnostic's severity color. </summary>
    Primary,

    /// <summary> Rendered with dashes (----) in a secondary/context color. </summary>
    Secondary,

    /// <summary> Included for context without rendering an underline. </summary>
    Context
}

/// <summary>
/// Represents an annotated source span attached to a diagnostic.
/// </summary>
internal readonly record struct DiagnosticLabel(
    TextSpan Span,
    string? Message = null,
    DiagnosticLabelStyle Style = DiagnosticLabelStyle.Primary,
    SourceText? Source = null
);
