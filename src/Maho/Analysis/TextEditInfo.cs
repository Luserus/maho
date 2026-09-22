namespace Maho;

/// <summary>
/// Public projection of a single text replacement in a source file.
/// </summary>
/// <param name="Span">The span of text to replace.</param>
/// <param name="NewText">The replacement text.</param>
/// <param name="FilePath">Optional path to the source file to edit.</param>
public readonly record struct TextEditInfo(
    TextSpanInfo Span,
    string NewText,
    string? FilePath = null);
