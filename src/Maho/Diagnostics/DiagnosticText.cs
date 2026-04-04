using Maho.Text;

namespace Maho.Diagnostics;

/// <summary>
/// Represents deferred diagnostic text that can stay span-based during analysis and only
/// materialize into a string when diagnostics are projected for output.
/// </summary>
internal readonly struct DiagnosticText
{
    private readonly SourceText? source;
    private readonly TextSpan span;
    private readonly string? literal;

    private DiagnosticText(DiagnosticTextKind kind, SourceText? source, TextSpan span, string? literal)
    {
        Kind = kind;
        this.source = source;
        this.span = span;
        this.literal = literal;
    }

    public DiagnosticTextKind Kind { get; }

    public static DiagnosticText SourceSpan(SourceText source, TextSpan span) => new(DiagnosticTextKind.SourceSpan, source, span, literal: null);

    public static DiagnosticText Literal(string text) => new(DiagnosticTextKind.Literal, source: null, default, text);

    public static DiagnosticText EndOfFile { get; } = new(DiagnosticTextKind.EndOfFile, source: null, default, literal: null);

    public static DiagnosticText MissingToken { get; } = new(DiagnosticTextKind.MissingToken, source: null, default, literal: null);

    public string Materialize() => Kind switch
    {
        DiagnosticTextKind.SourceSpan => source!.ToString(span),
        DiagnosticTextKind.Literal => literal ?? string.Empty,
        DiagnosticTextKind.EndOfFile => "<end of file>",
        DiagnosticTextKind.MissingToken => "<missing>",
        _ => string.Empty
    };
}

internal enum DiagnosticTextKind : byte
{
    Literal,
    SourceSpan,
    EndOfFile,
    MissingToken
}
