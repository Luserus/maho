using System.Collections.Generic;
using System.Threading;
using Maho.Text;

namespace Maho.Diagnostics;

/// <summary>
/// Aggregates diagnostics produced during analysis and centralizes the message shapes used by
/// lexer and parser code. This keeps stage logic focused on detecting problems rather than on
/// formatting user-visible text.
/// </summary>
internal sealed class DiagnosticsManager
{
    private readonly List<Diagnostic> diagnostics = [];
    private readonly Lock gate = new();
    /// <summary>
    /// Default source associated with diagnostics reported through this manager. File-local lexer
    /// and parser runs set this once so fixed-message diagnostics do not have to thread the same
    /// <see cref="SourceText"/> through every report call.
    /// </summary>
    private readonly SourceText? defaultSource;

    /// <summary>
    /// Creates a diagnostics manager optionally pre-bound to one source buffer. Project-wide
    /// resolution can leave this unset and supply source identities only for diagnostics that need
    /// to be routed back to a particular file.
    /// </summary>
    public DiagnosticsManager(SourceText? defaultSource = null) => this.defaultSource = defaultSource;

    /// <summary>
    /// Gets diagnostics in report order so downstream projection can preserve stable ordering when
    /// two diagnostics share the same source location.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics => diagnostics;

    /// <summary> Indicates whether any reported diagnostic should be treated as a failing analysis condition </summary>
    public bool HasErrors
    {
        get
        {
            lock (gate)
                return diagnostics.Exists(static diagnostic => diagnostic.Kind is DiagnosticKind.Error);
        }
    }

    /// <summary> Appends an already-constructed diagnostic to the shared collection. </summary>
    public void Report(Diagnostic diagnostic)
    {
        lock (gate)
            diagnostics.Add(diagnostic);
    }

    /// <summary> Reports a non-failing informational diagnostic using the shared internal model. </summary>
    public void ReportInfo(string code, string message, TextSpan span, SourceText? source = null) =>
        Report(new Diagnostic(code, message, span, DiagnosticKind.Info, source: source ?? defaultSource));

    /// <summary> Reports a warning diagnostic using the shared internal model. </summary>
    public void ReportWarning(string code, string message, TextSpan span, SourceText? source = null) =>
        Report(new Diagnostic(code, message, span, DiagnosticKind.Warning, source: source ?? defaultSource));

    /// <summary> Reports an error diagnostic using the shared internal model. </summary>
    public void ReportError(string code, string message, TextSpan span, SourceText? source = null) =>
        Report(new Diagnostic(code, message, span, DiagnosticKind.Error, source: source ?? defaultSource));

    /// <summary> Creates a fluent <see cref="DiagnosticBuilder"/> for constructing a rich error diagnostic. </summary>
    public DiagnosticBuilder BuildError(string code, string message, TextSpan primarySpan, SourceText? source = null) =>
        new(this, code, message, DiagnosticKind.Error, primarySpan, source ?? defaultSource);

    /// <summary> Creates a fluent <see cref="DiagnosticBuilder"/> for constructing a rich warning diagnostic. </summary>
    public DiagnosticBuilder BuildWarning(string code, string message, TextSpan primarySpan, SourceText? source = null) =>
        new(this, code, message, DiagnosticKind.Warning, primarySpan, source ?? defaultSource);

    /// <summary> Creates a fluent <see cref="DiagnosticBuilder"/> for constructing a rich info diagnostic. </summary>
    public DiagnosticBuilder BuildInfo(string code, string message, TextSpan primarySpan, SourceText? source = null) =>
        new(this, code, message, DiagnosticKind.Info, primarySpan, source ?? defaultSource);

    /// <summary>
    /// Reports an unsupported binary operator or type mismatch between left and right operands,
    /// annotating the operator with a primary caret and the operands with secondary underlines.
    /// </summary>
    public void ReportTypeMismatch(
        TextSpan opSpan,
        string op,
        TextSpan lhsSpan,
        string lhsTypeName,
        TextSpan rhsSpan,
        string rhsTypeName,
        string? note = null,
        string? help = null,
        SourceText? source = null)
    {
        var builder = BuildError("MH2001", $"Cannot apply binary operator '{op}' to types '{lhsTypeName}' and '{rhsTypeName}'.", opSpan, source)
            .WithPrimaryLabel(opSpan, $"cannot apply '{op}'")
            .WithSecondaryLabel(lhsSpan, $"this expression has type '{lhsTypeName}'")
            .WithSecondaryLabel(rhsSpan, $"this expression has type '{rhsTypeName}'");

        if (note is not null)
            builder.WithNote(note);
        if (help is not null)
            builder.WithHelp(help);

        builder.Report();
    }

    /// <summary>
    /// Reports a re-declared symbol in the same scope, pointing to both the re-declaration site
    /// (primary label) and the previous declaration site (secondary label).
    /// </summary>
    public void ReportDuplicateDeclaration(
        string symbolKind,
        string name,
        TextSpan redeclSpan,
        TextSpan firstDeclSpan,
        SourceText? redeclSource = null,
        SourceText? firstSource = null)
    {
        BuildError("MH1002", $"{symbolKind} '{name}' is already declared in this scope.", redeclSpan, redeclSource)
            .WithPrimaryLabel(redeclSpan, $"'{name}' re-declared here", redeclSource)
            .WithSecondaryLabel(firstDeclSpan, $"previous declaration of '{name}' here", firstSource)
            .WithHelp($"consider renaming or removing one of the duplicate {symbolKind.ToLowerInvariant()} declarations.")
            .Report();
    }

    /// <summary>
    /// Reports an error diagnostic that also preserves the expected syntax text for downstream
    /// renderers that want to produce more specific remediation hints.
    /// </summary>
    private void ReportExpected(string code, string expected, DiagnosticText found, TextSpan span, string? context = null, SourceText? source = null) =>
        Report(new Diagnostic(code, expected, found, span, DiagnosticKind.Error, context, source ?? defaultSource));


    /// <summary> Reports an invalid token emitted by the lexer, preserving the offending text when possible. </summary>
    public void ReportBadToken(TextSpan span, DiagnosticText tokenText) =>
        Report(new Diagnostic("MH0000", tokenText, span, DiagnosticKind.Error, source: defaultSource));

    /// <summary> Reports a string literal that could not be closed before the lexer had to recover. </summary>
    public void ReportUnterminatedString(TextSpan span, SourceText? source = null) =>
        ReportError("MH0001", "Unterminated string literal.", span, source);

    /// <summary> Reports a character literal that could not be closed before the lexer had to recover. </summary>
    public void ReportUnterminatedCharacter(TextSpan span, SourceText? source = null) =>
        ReportError("MH0002", "Unterminated character literal.", span, source);

    /// <summary> Reports a character literal whose delimiters contain no payload. </summary>
    public void ReportEmptyCharacterLiteral(TextSpan span, SourceText? source = null) =>
        ReportError("MH0003", "Character literal cannot be empty.", span, source);

    /// <summary> Reports a parser recovery site where a specific token kind was required. </summary>
    public void ReportExpectedToken(TextSpan span, string expected, DiagnosticText found, string? context = null, SourceText? source = null) =>
        ReportExpected("MH0004", expected, found, span, context, source);

    /// <summary> Reports a parser recovery site where an expression was needed to continue meaningfully. </summary>
    public void ReportExpectedExpression(TextSpan span, DiagnosticText found, string? context = null, SourceText? source = null) =>
        ReportExpected("MH0005", "an expression", found, span, context, source);

    /// <summary> Reports a parser recovery site where an identifier-shaped token was required. </summary>
    public void ReportExpectedIdentifier(TextSpan span, DiagnosticText found, string? context = null, SourceText? source = null) =>
        ReportExpected("MH0006", "an identifier", found, span, context, source);

    /// <summary> Reports a parser recovery site where type syntax was required. </summary>
    public void ReportExpectedType(TextSpan span, DiagnosticText found, string? context = null, SourceText? source = null) =>
        ReportExpected("MH0007", "a type", found, span, context, source);

    /// <summary> Reports a parser recovery site where a declaration or type body was required. </summary>
    public void ReportExpectedBody(TextSpan span, string expected, DiagnosticText found, string? context = null, SourceText? source = null) =>
        ReportExpected("MH0008", expected, found, span, context, source);

    /// <summary> Reports a parser recovery site where parameter syntax was required. </summary>
    public void ReportExpectedParameter(TextSpan span, DiagnosticText found, string? context = null, SourceText? source = null) =>
        ReportExpected("MH0009", "a parameter", found, span, context, source);

    /// <summary> Reports a parser recovery site where generic parameter syntax was required. </summary>
    public void ReportExpectedGenericParameter(TextSpan span, DiagnosticText found, string? context = null, SourceText? source = null) =>
        ReportExpected("MH0010", "a generic parameter", found, span, context, source);

    /// <summary> Reports a generic parser mismatch when no narrower expectation is available. </summary>
    public void ReportUnexpectedToken(TextSpan span, DiagnosticText found, SourceText? source = null) =>
        ReportExpectedToken(span, "valid syntax", found, source: source);

    /// <summary>
    /// Reports a parser-synthesized missing token using a standardized found-text placeholder so
    /// renderers and tests can recognize recovery artifacts consistently.
    /// </summary>
    public void ReportMissingToken(TextSpan span, string expected, SourceText? source = null) =>
        ReportExpectedToken(span, expected, DiagnosticText.MissingToken, source: source);

    /// <summary>
    /// Reports a type reference that could not be matched to any visible declaration.
    /// </summary>
    public void ReportUnresolvedTypeReference(TextSpan span, string typeName, SourceText? source = null) =>
        ReportError("MH1000", $"Could not resolve type '{typeName}'.", span, source);

    /// <summary> Reports a type reference that matched more than one visible declaration. </summary>
    public void ReportAmbiguousTypeReference(TextSpan span, string typeName, SourceText? source = null) =>
        ReportError("MH1001", $"Type '{typeName}' is ambiguous in the current scope.", span, source);

    /// <summary> Reports a duplicate type declaration in one lexical scope. </summary>
    public void ReportDuplicateTypeDeclaration(TextSpan span, string typeName, int arity, SourceText? source = null) =>
        ReportError(
            "MH1002",
            arity == 0
                ? $"Type '{typeName}' is already declared in this scope."
                : $"Type '{typeName}' with arity {arity} is already declared in this scope.",
            span,
            source);

    /// <summary> Reports a duplicate function declaration with the same generic arity and parameter shape. </summary>
    public void ReportDuplicateFunctionDeclaration(TextSpan span, string functionName, int arity, SourceText? source = null) =>
        ReportError(
            "MH1003",
            arity == 0
                ? $"Function '{functionName}' with the same parameter types is already declared in this scope."
                : $"Function '{functionName}' with arity {arity} and the same parameter types is already declared in this scope.",
            span,
            source);

    /// <summary> Reports that a type participates in an inheritance cycle. </summary>
    public void ReportCyclicTypeHierarchy(TextSpan span, string typeName, SourceText? source = null) =>
        ReportError("MH1004", $"Type '{typeName}' participates in a cycle in the type hierarchy.", span, source);

    /// <summary> Reports a duplicate variable declaration in one lexical scope. </summary>
    public void ReportDuplicateVariableDeclaration(TextSpan span, string variableName, SourceText? source = null) =>
        ReportError("MH1005", $"Variable '{variableName}' is already declared in this scope.", span, source);

    /// <summary> Reports a duplicate property declaration in one lexical scope. </summary>
    public void ReportDuplicatePropertyDeclaration(TextSpan span, string propertyName, SourceText? source = null) =>
        ReportError("MH1006", $"Property '{propertyName}' is already declared in this scope.", span, source);

    /// <summary> Reports an intrinsic attribute declaration whose simple compiler-known name is not recognized. </summary>
    public void ReportUnrecognizedIntrinsicAttribute(TextSpan span, string attributeName, SourceText? source = null) =>
        ReportError("MH1007", $"Intrinsic attribute '{attributeName}' is not recognized by the compiler.", span, source);

    /// <summary> Reports that a supported intrinsic attribute declaration was not found anywhere in the project. </summary>
    public void ReportUndeclaredIntrinsicAttribute(TextSpan span, string attributeName, SourceText? source = null) =>
        ReportError("MH1008", $"Intrinsic attribute '{attributeName}' was not declared in the project.", span, source);

    /// <summary> Reports that resolution state became inconsistent without crashing the analysis pipeline. </summary>
    public void ReportResolutionStateError(TextSpan span, string subject, SourceText? source = null) =>
        ReportError("MH0039", $"Resolution state became inconsistent while resolving {subject}.", span, source);

}