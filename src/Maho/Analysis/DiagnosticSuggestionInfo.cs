using System.Collections.Generic;
using Maho.Diagnostics;

namespace Maho;

/// <summary>
/// Public projection of a suggested code fix.
/// </summary>
/// <param name="Description">Human-readable description of the fix.</param>
/// <param name="Edits">List of individual text edits required for this suggestion.</param>
/// <param name="Applicability">Confidence / safety level of the suggestion.</param>
public readonly record struct DiagnosticSuggestionInfo(
    string Description,
    IReadOnlyList<TextEditInfo> Edits,
    SuggestionApplicability Applicability);
