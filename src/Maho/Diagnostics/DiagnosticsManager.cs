using System.Collections.Generic;
using System.Threading;
using Maho.Syntax;
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
        string? lhsLabel = null,
        string? rhsLabel = null,
        string? opName = null,
        string? note = null,
        string? help = null,
        SourceText? source = null)
    {
        var lhsDisplay = lhsLabel ?? lhsTypeName;
        var rhsDisplay = rhsLabel ?? rhsTypeName;
        var builder = BuildError("MH2001", $"cannot apply binary operator '{op}' to types '{lhsTypeName}' and '{rhsTypeName}'", opSpan, source)
            .WithPrimaryLabel(opSpan, $"cannot apply '{op}'")
            .WithSecondaryLabel(lhsSpan, lhsDisplay)
            .WithSecondaryLabel(rhsSpan, rhsDisplay);

        if (note is not null)
            builder.WithNote(note);
        if (help is not null)
            builder.WithHelp(help);
        else if (!string.IsNullOrEmpty(opName))
            builder.WithHelp($"implement an operator interface marked with [OperatorFunction(\"{opName}\")] to support '{op}' for '{lhsTypeName}' and '{rhsTypeName}'");

        builder.Report();
    }

    /// <summary>
    /// Reports an unsupported unary operator on an operand.
    /// </summary>
    public void ReportUnaryTypeMismatch(
        TextSpan opSpan,
        string op,
        TextSpan operandSpan,
        string operandTypeName,
        string? operandLabel = null,
        string? opName = null,
        string? note = null,
        string? help = null,
        SourceText? source = null)
    {
        var operandDisplay = operandLabel ?? operandTypeName;
        var builder = BuildError("MH2002", $"cannot apply unary operator '{op}' to operand of type '{operandTypeName}'", opSpan, source)
            .WithPrimaryLabel(opSpan, $"cannot apply '{op}'")
            .WithSecondaryLabel(operandSpan, operandDisplay);

        if (note is not null)
            builder.WithNote(note);
        if (help is not null)
            builder.WithHelp(help);
        else if (!string.IsNullOrEmpty(opName))
            builder.WithHelp($"implement an operator interface marked with [OperatorFunction(\"{opName}\")] to support '{op}' for '{operandTypeName}'");

        builder.Report();
    }

    /// <summary> Reports an assignment type mismatch. </summary>
    public void ReportAssignmentTypeMismatch(
        TextSpan span,
        string rhsTypeName,
        string lhsTypeName,
        string? rhsLabel = null,
        string? lhsLabel = null,
        SourceText? source = null)
    {
        var rhsDisplay = rhsLabel ?? rhsTypeName;
        BuildError("MH2003", $"cannot assign expression of type '{rhsTypeName}' to variable of type '{lhsTypeName}'", span, source)
            .WithPrimaryLabel(span, rhsDisplay, source)
            .Report();
    }

    /// <summary> Reports a function/method argument type mismatch. </summary>
    public void ReportArgumentTypeMismatch(
        TextSpan span,
        int index,
        string argTypeName,
        string paramTypeName,
        string? argLabel = null,
        string? paramLabel = null,
        SourceText? source = null)
    {
        var argDisplay = argLabel ?? argTypeName;
        BuildError("MH2004", $"argument {index}: cannot convert from '{argTypeName}' to '{paramTypeName}'", span, source)
            .WithPrimaryLabel(span, argDisplay, source)
            .Report();
    }

    /// <summary> Reports a call argument count mismatch. </summary>
    public void ReportArgumentCountMismatch(TextSpan span, int expected, int actual, SourceText? source = null) =>
        BuildError("MH2005", $"expected {expected} arguments, but received {actual}", span, source)
            .WithPrimaryLabel(span, $"expected {expected} arguments, but received {actual}", source)
            .Report();

    /// <summary> Reports a return type mismatch. </summary>
    public void ReportReturnTypeMismatch(
        TextSpan span,
        string exprTypeName,
        string returnTypeName,
        string? exprLabel = null,
        string? returnLabel = null,
        SourceText? source = null)
    {
        var exprDisplay = exprLabel ?? exprTypeName;
        BuildError("MH2006", $"cannot convert return expression of type '{exprTypeName}' to return type '{returnTypeName}'", span, source)
            .WithPrimaryLabel(span, exprDisplay, source)
            .Report();
    }

    /// <summary> Reports a condition expression type mismatch. </summary>
    public void ReportConditionTypeMismatch(
        TextSpan span,
        string actualTypeName,
        string? actualLabel = null,
        SourceText? source = null)
    {
        var actualDisplay = actualLabel ?? actualTypeName;
        BuildError("MH2007", $"condition expression must have type 'Bool', but found '{actualTypeName}'", span, source)
            .WithPrimaryLabel(span, actualDisplay, source)
            .Report();
    }

    /// <summary> Reports an uninferrable variable type. </summary>
    public void ReportCannotInferType(TextSpan span, string name, SourceText? source = null) =>
        BuildError("MH2008", $"cannot infer type for variable '{name}' from initializer", span, source)
            .WithPrimaryLabel(span, $"variable '{name}' requires an explicit type or a typed initializer", source)
            .Report();

    /// <summary> Reports a use after an implicit move consumed a value. </summary>
    public void ReportUseAfterMove(TextSpan span, string name, SourceText? source = null) =>
        BuildError("MH1001", $"use of moved value '{name}'", span, source)
            .WithPrimaryLabel(span, $"'{name}' was moved and is no longer initialized", source)
            .WithHelp("assign a new value before using it, or use a Copy type")
            .Report();

    /// <summary> Reports a read, move, or borrow that is not definitely initialized on all paths. </summary>
    public void ReportUseOfPossiblyUninitializedValue(TextSpan span, string name, SourceText? source = null) =>
        BuildError("MH1002", $"use of possibly uninitialized value '{name}'", span, source)
            .WithPrimaryLabel(span, $"'{name}' is not initialized on every control-flow path", source)
            .Report();

    /// <summary> Reports an operation that conflicts with a live safe borrow. </summary>
    public void ReportBorrowConflict(
        TextSpan useSpan,
        string operation,
        string name,
        TextSpan borrowSpan,
        SourceText? useSource = null,
        SourceText? borrowSource = null) =>
        BuildError("MH1003", $"cannot {operation} '{name}' because it is borrowed", useSpan, useSource)
            .WithPrimaryLabel(useSpan, $"cannot {operation} while borrowed", useSource)
            .WithSecondaryLabel(borrowSpan, "borrowed here", borrowSource)
            .Report();

    /// <summary>Reports a safe borrow that would outlive the local storage it references.</summary>
    public void ReportBorrowEscapesLocal(
        TextSpan returnSpan,
        string name,
        TextSpan localSpan,
        SourceText? returnSource = null,
        SourceText? localSource = null) =>
        BuildError("MH1004", $"cannot return a reference to local value '{name}'", returnSpan, returnSource)
            .WithPrimaryLabel(returnSpan, "borrow escapes this function", returnSource)
            .WithSecondaryLabel(localSpan, $"'{name}' is local to this function", localSource)
            .WithHelp("return a borrow of a parameter or static/global storage instead")
            .Report();

    /// <summary>Reports an attempted implicit copy of a move-only referent through a safe alias/reference.</summary>
    public void ReportCannotCopyMoveOnlyAlias(TextSpan span, string name, SourceText? source = null) =>
        BuildError("MH1005", $"cannot copy move-only value '{name}' through a safe alias or reference", span, source)
            .WithPrimaryLabel(span, "this assignment requires Copy semantics", source)
            .WithHelp("use a Copy type, or redesign the API to transfer ownership from an owned value")
            .Report();

    /// <summary>Reports an attempted move of a field from a type implementing custom Move or Destructor.</summary>
    public void ReportPartialMoveDisallowed(TextSpan span, string fieldName, string typeName, SourceText? source = null) =>
        BuildError("MH1006", $"cannot move field '{fieldName}' out of type '{typeName}' because it implements explicit 'Move' or 'Destructor'", span, source)
            .WithPrimaryLabel(span, "partial move not permitted for types with custom lifecycle", source)
            .WithHelp("move the entire struct instead, or borrow the field by reference")
            .Report();

    /// <summary>Reports an attempted move of a struct that has partially moved fields.</summary>
    public void ReportCannotMovePartiallyMoved(TextSpan span, string name, SourceText? source = null) =>
        BuildError("MH1007", $"cannot move partially moved value '{name}'", span, source)
            .WithPrimaryLabel(span, $"'{name}' cannot be moved because one or more fields were moved out", source)
            .WithHelp("reassign all moved fields before moving the entire value")
            .Report();

    /// <summary>Reports a safe borrow of a temporary value that would escape its statement.</summary>
    public void ReportBorrowEscapesTemporary(TextSpan span, string targetDescription, SourceText? source = null) =>
        BuildError("MH1008", $"cannot borrow temporary value which does not outlive {targetDescription}", span, source)
            .WithPrimaryLabel(span, "temporary value does not live long enough", source)
            .WithHelp("bind the value to a named local variable before taking its reference")
            .Report();

    /// <summary> Reports assignment to a non-lvalue expression. </summary>
    public void ReportCannotAssignToNonLValue(TextSpan span, SourceText? source = null) =>
        BuildError("MH2009", "the left-hand side of an assignment must be a variable, field, property or indexer", span, source)
            .WithPrimaryLabel(span, "invalid assignment target", source)
            .Report();

    /// <summary> Reports a tuple destructuring mismatch. </summary>
    public void ReportTupleDestructureMismatch(TextSpan span, string message, SourceText? source = null) =>
        BuildError("MH2010", $"tuple destructuring error: {message}", span, source)
            .WithPrimaryLabel(span, message, source)
            .Report();

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
        BuildError("MH0530", $"{symbolKind} '{name}' is already declared in this scope", redeclSpan, redeclSource)
            .WithPrimaryLabel(redeclSpan, $"'{name}' re-declared here", redeclSource)
            .WithSecondaryLabel(firstDeclSpan, $"previous declaration of '{name}' here", firstSource)
            .WithHelp($"consider renaming or removing one of the duplicate {symbolKind.ToLowerInvariant()} declarations")
            .Report();
    }

    /// <summary>
    /// Reports an error diagnostic that also preserves the expected syntax text for downstream
    /// renderers that want to produce more specific remediation hints.
    /// </summary>
    private void ReportExpected(
        string code,
        string expected,
        DiagnosticText found,
        TextSpan span,
        string? context = null,
        SourceText? source = null,
        TextSpan? unexpectedSpan = null)
    {
        var effectiveSource = source ?? defaultSource;
        IReadOnlyList<DiagnosticLabel> labels = [];
        TextSpan effectiveSpan = span;

        if (unexpectedSpan.HasValue)
        {
            if (effectiveSource is not null)
            {
                int expectedLine = effectiveSource.GetLineIndex(span.Start);
                int unexpectedLine = effectiveSource.GetLineIndex(unexpectedSpan.Value.Start);
                if (expectedLine != unexpectedLine)
                {
                    effectiveSpan = unexpectedSpan.Value;
                    labels =
                    [
                        new DiagnosticLabel(span, null, DiagnosticLabelStyle.Context, effectiveSource),
                        new DiagnosticLabel(unexpectedSpan.Value, null, DiagnosticLabelStyle.Primary, effectiveSource)
                    ];
                }
            }
            else
            {
                effectiveSpan = unexpectedSpan.Value;
                labels =
                [
                    new DiagnosticLabel(span, null, DiagnosticLabelStyle.Context, null),
                    new DiagnosticLabel(unexpectedSpan.Value, null, DiagnosticLabelStyle.Primary, null)
                ];
            }
        }

        Report(new Diagnostic(code, expected, found, effectiveSpan, DiagnosticKind.Error, context, effectiveSource)
        {
            Labels = labels
        });
    }

    /// <summary> Reports an invalid token emitted by the lexer, preserving the offending text when possible. </summary>
    public void ReportBadToken(TextSpan span, DiagnosticText tokenText) =>
        Report(new Diagnostic("MH0100", tokenText, span, DiagnosticKind.Error, source: defaultSource));

    /// <summary> Reports a string literal that could not be closed before the lexer had to recover. </summary>
    public void ReportUnterminatedString(TextSpan span, SourceText? source = null) =>
        ReportError("MH0101", "unterminated string literal", span, source);

    /// <summary> Reports a character literal that could not be closed before the lexer had to recover. </summary>
    public void ReportUnterminatedCharacter(TextSpan span, SourceText? source = null) =>
        ReportError("MH0102", "unterminated character literal", span, source);

    /// <summary> Reports a character literal whose delimiters contain no payload. </summary>
    public void ReportEmptyCharacterLiteral(TextSpan span, SourceText? source = null) =>
        ReportError("MH0103", "character literal cannot be empty", span, source);

    /// <summary> Reports a multi-line comment that was not closed before reaching the end of the source. </summary>
    public void ReportUnterminatedMultiLineComment(TextSpan span, SourceText? source = null) =>
        ReportError("MH0104", "unterminated multi-line comment", span, source);

    /// <summary> Reports a parser recovery site where a specific token kind was required. </summary>
    public void ReportExpectedToken(TextSpan span, string expected, DiagnosticText found, string? context = null, SourceText? source = null, TextSpan? unexpectedSpan = null) =>
        ReportExpected("MH0120", expected, found, span, context, source, unexpectedSpan);

    /// <summary> Reports a parser recovery site where an expression was needed to continue meaningfully. </summary>
    public void ReportExpectedExpression(TextSpan span, DiagnosticText found, string? context = null, SourceText? source = null, TextSpan? unexpectedSpan = null) =>
        ReportExpected("MH0121", "an expression", found, span, context, source, unexpectedSpan);

    /// <summary> Reports a parser recovery site where an identifier-shaped token was required. </summary>
    public void ReportExpectedIdentifier(TextSpan span, DiagnosticText found, string? context = null, SourceText? source = null, TextSpan? unexpectedSpan = null) =>
        ReportExpected("MH0122", "an identifier", found, span, context, source, unexpectedSpan);

    /// <summary> Reports a parser recovery site where type syntax was required. </summary>
    public void ReportExpectedType(TextSpan span, DiagnosticText found, string? context = null, SourceText? source = null, TextSpan? unexpectedSpan = null) =>
        ReportExpected("MH0123", "a type", found, span, context, source, unexpectedSpan);

    /// <summary> Reports a parser recovery site where a declaration or type body was required. </summary>
    public void ReportExpectedBody(TextSpan span, string expected, DiagnosticText found, string? context = null, SourceText? source = null, TextSpan? unexpectedSpan = null) =>
        ReportExpected("MH0124", expected, found, span, context, source, unexpectedSpan);

    /// <summary> Reports that an alias variable was declared without an initializer. </summary>
    public void ReportAliasMustBeInitialized(TextSpan span, string name, SourceText? source = null) =>
        BuildError("MH0125", $"alias variable '{name}' must be initialized in its declaration", span, source)
            .WithPrimaryLabel(span, "alias declaration requires an initializer", source)
            .Report();

    /// <summary> Reports a parser recovery site where generic parameter syntax was required. </summary>
    public void ReportExpectedGenericParameter(TextSpan span, DiagnosticText found, string? context = null, SourceText? source = null, TextSpan? unexpectedSpan = null) =>
        ReportExpected("MH0126", "a generic parameter", found, span, context, source, unexpectedSpan);

    public void ReportExpectedPattern(TextSpan span, DiagnosticText found, string? context = null, SourceText? source = null, TextSpan? unexpectedSpan = null) =>
        ReportExpected("MH0127", "a pattern", found, span, context, source, unexpectedSpan);

    /// <summary> Reports that top-level statements require '#pragma toplevel enable'. </summary>
    public void ReportTopLevelPragmaRequired(TextSpan span, SourceText? source = null) =>
        BuildError("MH0160", "top-level statements require '#pragma toplevel enable' in this file", span, source)
            .WithPrimaryLabel(span, "top-level statements require '#pragma toplevel enable'", source)
            .WithHelp("add '#pragma toplevel enable' to the file or use an explicit main function")
            .Report();

    /// <summary> Reports that only one source file may contain top-level statements. </summary>
    public void ReportMultipleTopLevelSources(TextSpan span, SourceText? source = null) =>
        BuildError("MH0161", "only one source file may contain top-level statements", span, source)
            .WithPrimaryLabel(span, "extra top-level statement source declared here", source)
            .WithHelp("ensure only a single file in the project has top-level statements")
            .Report();

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
    public void ReportUnresolvedTypeReference(
        TextSpan span,
        string typeName,
        SourceText? source = null,
        ExpansionOrigin? origin = null,
        string? arityNote = null)
    {
        var builder = BuildError("MH0500", $"could not resolve type '{typeName}'", span, source)
            .WithPrimaryLabel(span, $"type '{typeName}' not found", source)
            .WithExpansionOrigin(origin);

        if (arityNote is not null)
            builder.WithNote(arityNote);

        builder.WithHelp("check for a missing import or declaration")
            .Report();
    }

    /// <summary> Reports a type reference that matched more than one visible declaration. </summary>
    public void ReportAmbiguousTypeReference(TextSpan span, string typeName, SourceText? source = null, ExpansionOrigin? origin = null) =>
        BuildError("MH0501", $"type '{typeName}' is ambiguous in the current scope", span, source)
            .WithPrimaryLabel(span, $"ambiguous reference to '{typeName}'", source)
            .WithExpansionOrigin(origin)
            .WithHelp($"qualify '{typeName}' with its namespace or use an alias")
            .Report();



    /// <summary> Reports a duplicate type declaration pointing to both declaration sites. </summary>
    public void ReportDuplicateTypeDeclaration(
        string typeName,
        TextSpan redeclSpan,
        TextSpan firstDeclSpan,
        SourceText? redeclSource = null,
        SourceText? firstSource = null) =>
        ReportDuplicateDeclaration("type", typeName, redeclSpan, firstDeclSpan, redeclSource, firstSource);

    /// <summary> Reports a duplicate alias declaration pointing to both declaration sites. </summary>
    public void ReportDuplicateAliasDeclaration(
        string aliasName,
        TextSpan redeclSpan,
        TextSpan firstDeclSpan,
        SourceText? redeclSource = null,
        SourceText? firstSource = null) =>
        ReportDuplicateDeclaration("alias", aliasName, redeclSpan, firstDeclSpan, redeclSource, firstSource);

    /// <summary> Reports that partial declarations of a type have conflicting type kinds. </summary>
    public void ReportConflictingPartialTypeKinds(
        string typeName,
        string firstKind,
        string redeclKind,
        TextSpan redeclSpan,
        TextSpan firstDeclSpan,
        SourceText? redeclSource = null,
        SourceText? firstSource = null)
    {
        BuildError(
            "MH0531",
            $"partial declarations of '{typeName}' have conflicting type kinds ('{firstKind}' and '{redeclKind}')",
            redeclSpan,
            redeclSource)
            .WithPrimaryLabel(redeclSpan, $"declared as '{redeclKind}' here", redeclSource)
            .WithSecondaryLabel(firstDeclSpan, $"declared as '{firstKind}' here", firstSource)
            .Report();
    }

    /// <summary> Reports a duplicate function declaration pointing to both declaration sites. </summary>
    public void ReportDuplicateFunctionDeclaration(
        string functionName,
        TextSpan redeclSpan,
        TextSpan firstDeclSpan,
        SourceText? redeclSource = null,
        SourceText? firstSource = null)
    {
        BuildError(
            "MH0532",
            $"function '{functionName}' with the same parameter types is already declared in this scope",
            redeclSpan,
            redeclSource)
            .WithPrimaryLabel(redeclSpan, $"'{functionName}' re-declared here", redeclSource)
            .WithSecondaryLabel(firstDeclSpan, $"previous declaration of '{functionName}' here", firstSource)
            .WithHelp("consider renaming the function or changing its parameter types")
            .Report();
    }

    /// <summary> Reports that a partial function has more than one defining declaration with a body. </summary>
    public void ReportMultiplePartialFunctionBodies(
        string functionName,
        TextSpan redeclSpan,
        TextSpan firstDeclSpan,
        SourceText? redeclSource = null,
        SourceText? firstSource = null)
    {
        BuildError(
            "MH0533",
            $"partial function '{functionName}' cannot have more than one defining declaration with a body",
            redeclSpan,
            redeclSource)
            .WithPrimaryLabel(redeclSpan, $"'{functionName}' already has an implementation here", redeclSource)
            .WithSecondaryLabel(firstDeclSpan, $"previous implementation of '{functionName}' here", firstSource)
            .WithHelp("remove the extra function body or merge the implementations")
            .Report();
    }

    /// <summary> Reports that declarations of a partial function have conflicting return types. </summary>
    public void ReportConflictingPartialFunctionReturnTypes(
        string functionName,
        TextSpan redeclSpan,
        TextSpan firstDeclSpan,
        SourceText? redeclSource = null,
        SourceText? firstSource = null)
    {
        BuildError(
            "MH0534",
            $"partial function declarations of '{functionName}' have conflicting return types",
            redeclSpan,
            redeclSource)
            .WithPrimaryLabel(redeclSpan, "conflicting return type declared here", redeclSource)
            .WithSecondaryLabel(firstDeclSpan, "previous return type declared here", firstSource)
            .Report();
    }

    /// <summary> Reports that a type participates in an inheritance cycle. </summary>
    public void ReportCyclicTypeHierarchy(TextSpan span, string typeName, SourceText? source = null) =>
        BuildError("MH0538", $"type '{typeName}' participates in a cycle in the type hierarchy", span, source)
            .WithPrimaryLabel(span, $"'{typeName}' participates in a cycle here", source)
            .WithHelp("break the inheritance cycle by removing one of the base types")
            .Report();

    /// <summary> Reports an inconsistent or cyclic inheritance order. </summary>
    public void ReportInconsistentInheritanceOrder(TextSpan span, string typeName, SourceText? source = null) =>
        BuildError("MH0539", $"inconsistent or cyclic inheritance order detected for type '{typeName}'", span, source)
            .WithPrimaryLabel(span, $"cannot compute a consistent C3 linearization for '{typeName}'", source)
            .WithHelp("reorder or simplify base types to resolve conflicting inheritance orders")
            .Report();

    /// <summary> Reports that a base constructor requires explicit initialization in the most-derived constructor. </summary>
    public void ReportBaseConstructorRequiresExplicitInit(TextSpan span, string baseName, SourceText? source = null) =>
        BuildError("MH0551", $"base '{baseName}' has no default constructor; derived type must explicitly initialize it", span, source)
            .WithPrimaryLabel(span, $"base '{baseName}' must be initialized here", source)
            .WithHelp($"add ':{baseName}(...)' to the constructor's base initializer list")
            .Report();

    /// <summary> Reports that a base initializer clause was placed on an ordinary (non-constructor) function. </summary>
    public void ReportInvalidBaseInitializerOnOrdinaryFunction(TextSpan span, SourceText? source = null) =>
        BuildError("MH0552", "base initializer clause can only be specified on a constructor function", span, source)
            .WithPrimaryLabel(span, "base initializer clause is not allowed on ordinary functions", source)
            .WithHelp("remove the ': Base(...)' clause or declare this function as a constructor 'func Ctor.Construct'")
            .Report();

    /// <summary> Reports that a base initializer specifies a type that is not an inherited base of the enclosing type. </summary>
    public void ReportTypeIsNotBaseOf(TextSpan span, string targetName, string typeName, SourceText? source = null) =>
        BuildError("MH0553", $"type '{targetName}' is not a base type of '{typeName}'", span, source)
            .WithPrimaryLabel(span, $"'{targetName}' is not inherited by '{typeName}'", source)
            .WithHelp($"remove '{targetName}' from the base initializer list or add it to the inheritance list of '{typeName}'")
            .Report();

    /// <summary> Reports that a base type is initialized more than once in a constructor base initializer list. </summary>
    public void ReportDuplicateBaseInitializer(TextSpan span, string baseName, SourceText? source = null) =>
        BuildError("MH0554", $"base '{baseName}' is already initialized in this base initializer list", span, source)
            .WithPrimaryLabel(span, $"duplicate initialization of base '{baseName}'", source)
            .WithHelp($"remove the redundant ':{baseName}(...)' initializer")
            .Report();

    /// <summary> Reports an attempt to take a thin reference to a non-contiguous / split base. </summary>
    public void ReportNonContiguousBaseConversion(TextSpan span, string baseName, SourceText? source = null) =>
        BuildError("MH0555", $"cannot convert derived reference to non-contiguous base '{baseName}'; use 'dyn {baseName}&' instead", span, source)
            .WithPrimaryLabel(span, $"sub-object '{baseName}' is non-contiguous in memory", source)
            .WithHelp($"convert to dynamic existential reference 'dyn {baseName}&' instead")
            .Report();

    /// <summary> Reports an ambiguous member access across sibling base types. </summary>
    public void ReportAmbiguousMemberAccess(TextSpan span, string memberName, string typeName, SourceText? source = null) =>
        BuildError("MH0556", $"ambiguous member '{memberName}' in type '{typeName}'; disambiguate by casting with 'as'", span, source)
            .WithPrimaryLabel(span, $"'{memberName}' is declared in multiple base types", source)
            .WithHelp($"disambiguate by casting the receiver: '(expr as Base).{memberName}'")
            .Report();

    /// <summary> Reports that a concrete type does not implement a required abstract interface member. </summary>
    public void ReportUnimplementedInterfaceMember(
        TextSpan span,
        string typeName,
        string interfaceName,
        string memberName,
        SourceText? source = null,
        TextSpan? interfaceMemberSpan = null,
        SourceText? interfaceMemberSource = null,
        string? detail = null)
    {
        var msg = detail is null
            ? $"type '{typeName}' does not implement interface member '{interfaceName}.{memberName}'"
            : $"type '{typeName}' does not implement interface member '{interfaceName}.{memberName}': {detail}";

        var builder = BuildError("MH0550", msg, span, source)
            .WithPrimaryLabel(span, $"'{typeName}' does not implement interface member '{interfaceName}.{memberName}'", source);

        if (interfaceMemberSpan is { } memberSpan)
        {
            builder.WithSecondaryLabel(memberSpan, $"interface member '{interfaceName}.{memberName}' declared here", interfaceMemberSource);
        }

        builder.WithHelp($"implement '{interfaceName}.{memberName}' on '{typeName}' or provide a default implementation in '{interfaceName}'")
            .Report();
    }

    /// <summary> Reports a duplicate variable declaration pointing to both declaration sites. </summary>
    public void ReportDuplicateVariableDeclaration(
        string variableName,
        TextSpan redeclSpan,
        TextSpan firstDeclSpan,
        SourceText? redeclSource = null,
        SourceText? firstSource = null)
    {
        BuildError(
            "MH0535",
            $"variable '{variableName}' is already declared in this scope",
            redeclSpan,
            redeclSource)
            .WithPrimaryLabel(redeclSpan, $"'{variableName}' re-declared here", redeclSource)
            .WithSecondaryLabel(firstDeclSpan, $"previous declaration of '{variableName}' here", firstSource)
            .WithHelp("consider renaming or removing one of the duplicate variable declarations")
            .Report();
    }

    /// <summary> Reports a duplicate property declaration pointing to both declaration sites. </summary>
    public void ReportDuplicatePropertyDeclaration(
        string propertyName,
        TextSpan redeclSpan,
        TextSpan firstDeclSpan,
        SourceText? redeclSource = null,
        SourceText? firstSource = null)
    {
        BuildError(
            "MH0536",
            $"property '{propertyName}' is already declared in this scope",
            redeclSpan,
            redeclSource)
            .WithPrimaryLabel(redeclSpan, $"'{propertyName}' re-declared here", redeclSource)
            .WithSecondaryLabel(firstDeclSpan, $"previous declaration of '{propertyName}' here", firstSource)
            .WithHelp("consider renaming or removing one of the duplicate property declarations")
            .Report();
    }

    /// <summary> Reports that a macro definition has an invalid syntax or missing arms. </summary>
    public void ReportInvalidMacroDeclaration(TextSpan span, string message, SourceText? source = null) =>
        BuildError("MH0200", message, span, source)
            .WithPrimaryLabel(span, message, source)
            .Report();

    /// <summary> Reports an invalid parameter pattern inside a macro arm. </summary>
    public void ReportInvalidMacroPattern(TextSpan span, string message, SourceText? source = null) =>
        BuildError("MH0201", message, span, source)
            .WithPrimaryLabel(span, message, source)
            .Report();

    /// <summary> Reports an invalid token concatenation in a macro template. </summary>
    public void ReportInvalidTokenConcatenation(TextSpan span, string message, SourceText? source = null) =>
        BuildError("MH0230", message, span, source)
            .WithPrimaryLabel(span, message, source)
            .Report();

    /// <summary> Reports that macro expansion exceeded the configured recursion depth limit. </summary>
    public void ReportMacroRecursionLimitExceeded(TextSpan span, string macroName, ulong limit, SourceText? source = null) =>
        BuildError("MH0600", $"recursion limit of {limit} exceeded while expanding macro '${macroName}'", span, source)
            .WithPrimaryLabel(span, $"recursion limit reached during expansion of '${macroName}'", source)
            .WithHelp($"consider simplifying the macro or increasing 'MacroRecursionLimit' (current: {limit})")
            .Report();

    /// <summary> Reports that arguments supplied to a macro invocation matched none of the macro's arms. </summary>
    public void ReportNoMatchingMacroArm(TextSpan span, string macroName, SourceText? source = null) =>
        BuildError("MH0601", $"no matching arm found for macro '${macroName}' with the supplied arguments", span, source)
            .WithPrimaryLabel(span, $"no arm of '${macroName}' matched these arguments", source)
            .Report();

    /// <summary> Reports a macro invocation whose macro symbol could not be found in scope. </summary>
    public void ReportUnresolvedMacro(TextSpan span, string macroName, SourceText? source = null) =>
        BuildError("MH0602", $"could not resolve macro '${macroName}'", span, source)
            .WithPrimaryLabel(span, $"macro '${macroName}' not found", source)
            .WithHelp("check for a missing import or macro declaration")
            .Report();

    /// <summary> Reports a macro invocation used in an incompatible context (e.g. statement macro in expression). </summary>
    public void ReportInvalidMacroContext(TextSpan span, string macroName, string expectedContext, string actualContent, SourceText? source = null) =>
        BuildError("MH0603", $"macro '${macroName}' produces {actualContent} and cannot be used in {expectedContext} context", span, source)
            .WithPrimaryLabel(span, $"cannot use {actualContent} macro in {expectedContext} context", source)
            .Report();

    /// <summary> Reports a mutual dependency cycle or deadlock during macro expansion. </summary>
    public void ReportMacroDependencyCycle(TextSpan span, string macroName, SourceText? source = null) =>
        BuildError("MH0604", $"macro '${macroName}' participates in a circular dependency or expansion deadlock", span, source)
            .WithPrimaryLabel(span, $"circular macro expansion involves '${macroName}' here", source)
            .Report();

    /// <summary> Reports an unknown intrinsic attribute name declared with the intrinsic modifier. </summary>
    public void ReportUnknownIntrinsicAttribute(TextSpan span, string attributeName, SourceText? source = null) =>
        BuildError("MH0540", $"unknown intrinsic attribute '{attributeName}'", span, source)
            .WithPrimaryLabel(span, $"'{attributeName}' is not a recognized compiler intrinsic attribute", source)
            .WithHelp("only compiler-known intrinsic attributes (such as 'IntrinsicType') can be declared with the 'intrinsic' modifier")
            .Report();

    /// <summary> Reports an invalid signature for an intrinsic attribute declaration. </summary>
    public void ReportInvalidIntrinsicAttributeSignature(TextSpan span, string message, SourceText? source = null, string attributeName = "IntrinsicType") =>
        BuildError("MH0541", $"invalid signature for intrinsic attribute '{attributeName}': {message}", span, source)
            .WithPrimaryLabel(span, message, source)
            .Report();

    /// <summary> Reports an invalid signature for an operator function declaration. </summary>
    public void ReportInvalidOperatorFunctionSignature(TextSpan span, string operatorName, string reason, SourceText? source = null) =>
        BuildError("MH0560", $"invalid signature for operator function '{operatorName}': {reason}", span, source)
            .WithPrimaryLabel(span, reason, source)
            .Report();

    /// <summary> Reports a duplicate operator function declaration. </summary>
    public void ReportDuplicateOperatorFunction(
        string operatorName,
        TextSpan redeclSpan,
        TextSpan? firstDeclSpan = null,
        SourceText? redeclSource = null,
        SourceText? firstSource = null)
    {
        var builder = BuildError("MH0563", $"duplicate operator function declaration for operator '{operatorName}'; only one interface operator function is allowed per operator", redeclSpan, redeclSource)
            .WithPrimaryLabel(redeclSpan, $"duplicate declaration of operator function '{operatorName}' here", redeclSource);

        if (firstDeclSpan is { } firstSpan)
            builder.WithSecondaryLabel(firstSpan, $"previous declaration of operator function '{operatorName}' here", firstSource);

        builder.WithHelp($"only one interface can declare an [OperatorFunction(\"{operatorName}\")] contract")
            .Report();
    }

    /// <summary> Reports an unknown intrinsic type literal applied via IntrinsicType. </summary>
    public void ReportUnknownIntrinsicType(TextSpan span, string intrinsicLiteral, SourceText? source = null) =>
        BuildError("MH0542", $"unknown intrinsic type '{intrinsicLiteral}'", span, source)
            .WithPrimaryLabel(span, $"'{intrinsicLiteral}' is not a recognized compiler intrinsic type", source)
            .Report();

    /// <summary> Reports a duplicate intrinsic type declaration. </summary>
    public void ReportDuplicateIntrinsicType(
        string intrinsicName,
        TextSpan redeclSpan,
        TextSpan? firstDeclSpan = null,
        SourceText? redeclSource = null,
        SourceText? firstSource = null)
    {
        var builder = BuildError("MH0543", $"intrinsic type '{intrinsicName}' is already declared", redeclSpan, redeclSource)
            .WithPrimaryLabel(redeclSpan, $"duplicate declaration of intrinsic '{intrinsicName}' here", redeclSource);

        if (firstDeclSpan is { } firstSpan)
            builder.WithSecondaryLabel(firstSpan, $"previous declaration of intrinsic '{intrinsicName}' here", firstSource);

        builder.WithHelp($"intrinsic types can only be declared once across the project and its referenced libraries")
            .Report();
    }

    /// <summary> Reports that an intrinsic type does not satisfy its required ABI contract. </summary>
    public void ReportIntrinsicAbiMismatch(TextSpan span, string typeName, string intrinsicName, string reason, SourceText? source = null) =>
        BuildError("MH0020", $"type '{typeName}' does not satisfy the ABI contract for intrinsic '{intrinsicName}': {reason}", span, source)
            .WithPrimaryLabel(span, reason, source)
            .Report();

    /// <summary> Reports that syntax requires an intrinsic type that was not declared or failed verification. </summary>
    public void ReportMissingIntrinsicForSyntax(TextSpan span, string syntaxDescription, string requiredIntrinsic, SourceText? source = null) =>
        BuildError("MH0021", $"{syntaxDescription} requires intrinsic type '{requiredIntrinsic}' to be declared", span, source)
            .WithPrimaryLabel(span, $"requires intrinsic '{requiredIntrinsic}'", source)
            .WithHelp($"declare a type with [IntrinsicType(\"{requiredIntrinsic}\")] or reference a library that defines it")
            .Report();

    /// <summary> Reports a non-exhaustive pattern match. </summary>
    public void ReportNonExhaustiveMatch(TextSpan span, string missingVariant, SourceText? source = null) =>
        BuildError("MH0605", $"match expression is not exhaustive; missing variant '{missingVariant}'", span, source)
            .WithPrimaryLabel(span, $"missing pattern arm for variant '{missingVariant}'", source)
            .WithHelp("add the missing variant arm or a wildcard pattern '_' to make the match exhaustive")
            .Report();

    /// <summary> Reports that a type implements both Destructor and Copy. </summary>
    public void ReportDestructorNandCopy(TextSpan span, string typeName, SourceText? source = null) =>
        BuildError("MH0561", $"type '{typeName}' cannot implement both 'Destructor' and 'Copy'; types with destructors must use move semantics", span, source)
            .WithPrimaryLabel(span, "cannot implement both 'Destructor' and 'Copy'", source)
            .Report();

    /// <summary> Reports that a type implements both Copy and Move. </summary>
    public void ReportCopyNandMove(TextSpan span, string typeName, SourceText? source = null) =>
        BuildError("MH0562", $"type '{typeName}' cannot implement both 'Copy' and 'Move'; a type must be exclusively Copy or exclusively Move", span, source)
            .WithPrimaryLabel(span, "cannot implement both 'Copy' and 'Move'", source)
            .Report();

    /// <summary> Reports incompatible branch types in conditional expression. </summary>
    public void ReportAmbiguousConditionalBranches(
        TextSpan span,
        string thenType,
        string elseType,
        string? thenLabel = null,
        string? elseLabel = null,
        SourceText? source = null)
    {
        var thenDisplay = thenLabel ?? thenType;
        var elseDisplay = elseLabel ?? elseType;
        BuildError("MH2011", $"conditional branches evaluate to different types '{thenType}' and '{elseType}'; explicit cast to a common base type is required", span, source)
            .WithPrimaryLabel(span, $"branches evaluate to '{thenDisplay}' and '{elseDisplay}'", source)
            .WithHelp("cast branches explicitly to a common base type or interface")
            .Report();
    }

    /// <summary> Reports conflicting return types in inferred function. </summary>
    public void ReportConflictingReturnType(TextSpan span, string actualType, string previousType, SourceText? source = null) =>
        BuildError("MH2012", $"return statement returns type '{actualType}', which conflicts with previous return type '{previousType}'; all return branches must return the exact same type or require explicit return type declaration", span, source)
            .WithPrimaryLabel(span, $"returns '{actualType}' but previous return yielded '{previousType}'", source)
            .Report();

    /// <summary> Reports ambiguous literal conversion when multiple literal overloads exist. </summary>
    public void ReportAmbiguousLiteralConversion(
        TextSpan span,
        string typeName,
        string overloads,
        string? labelTypeName = null,
        SourceText? source = null)
    {
        var labelType = labelTypeName ?? typeName;
        BuildError("MH2013", $"ambiguous literal conversion for type '{typeName}'; multiple literal overloads exist ({overloads}); explicit suffix required", span, source)
            .WithPrimaryLabel(span, $"ambiguous literal overloads: {overloads}", source)
            .WithHelp("provide an explicit literal suffix to select the intended overload")
            .Report();
    }

    /// <summary> Reports that a member was not found on a type. </summary>
    public void ReportMemberNotFound(
        TextSpan span,
        string memberName,
        string typeName,
        string? labelTypeName = null,
        SourceText? source = null)
    {
        var labelType = labelTypeName ?? typeName;
        BuildError("MH2014", $"type '{typeName}' does not contain a definition for '{memberName}'", span, source)
            .WithPrimaryLabel(span, $"'{memberName}' not found on '{labelType}'", source)
            .Report();
    }

    /// <summary> Reports that an expression is not callable or has no matching Call operator overload. </summary>
    public void ReportTypeNotCallable(
        TextSpan span,
        string typeName,
        string? labelTypeName = null,
        SourceText? source = null)
    {
        var labelType = labelTypeName ?? typeName;
        BuildError("MH0611", $"type '{typeName}' is not callable or has no matching overload for 'Call'", span, source)
            .WithPrimaryLabel(span, $"'{labelType}' is not callable", source)
            .WithHelp($"implement 'Callable<TReturn, ...>' to make instances of '{typeName}' invocable")
            .Report();
    }

    /// <summary> Reports an attempt to match on an untagged sum type without discriminant. </summary>
    public void ReportUntaggedMatchDisallowed(TextSpan span, string sumTypeName, SourceText? source = null) =>
        BuildError("MH0606", $"cannot match on untagged sum type '{sumTypeName}' without discriminant", span, source)
            .WithPrimaryLabel(span, $"cannot match on '{sumTypeName}' because it is untagged", source)
            .WithHelp("untagged sum types have no runtime discriminant; access them via untagged methods")
            .Report();

    /// <summary> Reports an invalid pattern that does not match any variant or constituent of the sum type. </summary>
    public void ReportInvalidPatternForSumType(TextSpan span, string patternName, string sumTypeName, SourceText? source = null) =>
        BuildError("MH0607", $"pattern '{patternName}' is not a valid variant or constituent of sum type '{sumTypeName}'", span, source)
            .WithPrimaryLabel(span, $"'{patternName}' does not belong to sum type '{sumTypeName}'", source)
            .Report();

    /// <summary> Reports invocation of an ordinary method on an untagged sum type receiver. </summary>
    public void ReportUntaggedMethodMismatch(TextSpan span, string methodName, string sumTypeName, SourceText? source = null) =>
        BuildError("MH0608", $"cannot invoke ordinary instance method '{methodName}' on untagged sum type '{sumTypeName}'", span, source)
            .WithPrimaryLabel(span, $"cannot invoke non-untagged method '{methodName}' on untagged receiver", source)
            .WithHelp("untagged sum type instances can only invoke methods declared with the 'untagged' modifier")
            .Report();

    /// <summary> Reports a direct invocation of a variant-level method on a sum type instance. </summary>
    public void ReportDirectVariantMethodInvocation(TextSpan span, string methodName, string sumTypeName, SourceText? source = null) =>
        BuildError("MH0609", $"'{methodName}' is a variant of sum type '{sumTypeName}'; invoke methods via pattern matching", span, source)
            .WithPrimaryLabel(span, $"cannot call variant method '{methodName}' directly on sum type '{sumTypeName}'", source)
            .WithHelp("match on the sum type instance to unwrap the variant before invoking variant methods")
            .Report();

    /// <summary> Reports usage of an untagged-defined type without the untagged modifier. </summary>
    public void ReportUntaggedDefinitionTaggedUsage(TextSpan span, string typeName, SourceText? source = null) =>
        BuildError("MH0610", $"'untagged' type '{typeName}' must be declared with 'untagged' modifier", span, source)
            .WithPrimaryLabel(span, $"'{typeName}' was defined as untagged and must be declared as 'untagged {typeName}'", source)
            .Report();

    /// <summary> Reports an uninferrable lambda parameter type. </summary>
    public void ReportCannotInferLambdaParameterType(TextSpan span, string name, SourceText? source = null) =>
        BuildError("MH2015", $"cannot infer type for lambda parameter '{name}'; an explicit type annotation is required", span, source)
            .WithPrimaryLabel(span, $"lambda parameter '{name}' requires an explicit type", source)
            .WithHelp("specify the parameter type explicitly (e.g. (int x) => ...)")
            .Report();

    /// <summary> Reports a trailing expression in a lambda block body. </summary>
    public void ReportLambdaBlockBodyTrailingExpression(TextSpan span, SourceText? source = null) =>
        BuildError("MH2016", "lambda block body must end with an explicit return statement; trailing expressions are not permitted", span, source)
            .WithPrimaryLabel(span, "trailing expression not permitted in lambda block body", source)
            .WithHelp("add an explicit 'return' statement")
            .Report();

    /// <summary> Reports that not all reachable code paths return a value in a non-Unit function. </summary>
    public void ReportMissingReturn(TextSpan span, string functionName, SourceText? source = null) =>
        BuildError("MH1010", $"not all code paths return a value in function '{functionName}'", span, source)
            .WithPrimaryLabel(span, $"missing return statement on at least one reachable code path in '{functionName}'", source)
            .WithHelp("ensure all reachable code paths end with a 'return' statement or return type 'Never'")
            .Report();

    /// <summary> Reports that all code paths in a function diverge, so it must be declared with return type 'Never'. </summary>
    public void ReportFunctionMustReturnNever(TextSpan span, string functionName, string declaredReturnType, SourceText? source = null) =>
        BuildError("MH1011", $"function '{functionName}' never returns and must be declared with return type 'Never'", span, source)
            .WithPrimaryLabel(span, $"function declared with return type '{declaredReturnType}', but never returns", source)
            .WithHelp($"all execution paths diverge; declare the return type as 'Never' (e.g. func {functionName}(...) -> Never)")
            .Report();

    /// <summary> Reports an attempt to access an instance member on a type without an instance reference. </summary>
    public void ReportCannotAccessInstanceMemberOnType(TextSpan span, string memberName, string typeName, SourceText? source = null) =>
        BuildError("MH2019", $"an object reference is required for the non-static field, method, or property '{memberName}' on type '{typeName}'", span, source)
            .WithPrimaryLabel(span, $"non-static member '{memberName}' cannot be accessed through type '{typeName}'", source)
            .WithHelp("create or provide an instance of the type to access this member")
            .Report();

    /// <summary> Reports an attempt to access a static member through an instance reference. </summary>
    public void ReportCannotAccessStaticMemberOnInstance(TextSpan span, string memberName, string typeName, SourceText? source = null) =>
        BuildError("MH2020", $"static member '{memberName}' cannot be accessed with an instance reference on type '{typeName}'", span, source)
            .WithPrimaryLabel(span, $"static member '{memberName}' cannot be accessed via instance", source)
            .WithHelp($"qualify it with the type name instead: '{typeName}.{memberName}'")
            .Report();

    /// <summary> Reports an invalid modifier for a declaration kind. </summary>
    public void ReportInvalidModifier(TextSpan span, string modifier, string itemKind, string name, SourceText? source = null) =>
        BuildError("MH0128", $"modifier '{modifier}' is not valid for {itemKind} declaration '{name}'", span, source)
            .WithPrimaryLabel(span, $"modifier '{modifier}' not allowed on {itemKind}", source)
            .Report();

    /// <summary> Reports that a global variable declaration is missing the required static modifier. </summary>
    public void ReportGlobalMissingStatic(TextSpan span, string name, SourceText? source = null) =>
        BuildError("MH0128", $"global variable '{name}' must be declared with 'static' modifier", span, source)
            .WithPrimaryLabel(span, "missing 'static' modifier", source)
            .WithHelp("add the 'static' modifier: e.g. 'static ...'")
            .Report();

    /// <summary> Reports that a modifier is out of canonical order. </summary>
    public void ReportModifierOutOfOrder(TextSpan span, string modifier, string name, string canonicalOrder, SourceText? source = null) =>
        BuildError("MH0129", $"modifier '{modifier}' is out of order on declaration '{name}'; expected order: {canonicalOrder}", span, source)
            .WithPrimaryLabel(span, $"out of order modifier '{modifier}'", source)
            .WithHelp($"reorder modifiers to follow canonical order: {canonicalOrder}")
            .Report();

    /// <summary> Reports a duplicate modifier on a declaration. </summary>
    public void ReportDuplicateModifier(TextSpan span, string modifier, string name, SourceText? source = null) =>
        BuildError("MH0130", $"duplicate modifier '{modifier}' on declaration '{name}'", span, source)
            .WithPrimaryLabel(span, $"duplicate modifier '{modifier}'", source)
            .Report();

    /// <summary> Reports conflicting modifiers on a declaration. </summary>
    public void ReportConflictingModifiers(TextSpan span, string modifier1, string modifier2, string name, SourceText? source = null) =>
        BuildError("MH0131", $"conflicting modifiers '{modifier1}' and '{modifier2}' on declaration '{name}'", span, source)
            .WithPrimaryLabel(span, $"cannot combine '{modifier1}' with '{modifier2}'", source)
            .Report();

    /// <summary> Reports an attempt to access a private member from outside its declaring type. </summary>
    public void ReportCannotAccessPrivateMember(TextSpan span, string memberName, string declaringTypeName, SourceText? source = null) =>
        BuildError("MH0520", $"cannot access private member '{memberName}' of type '{declaringTypeName}'", span, source)
            .WithPrimaryLabel(span, $"'{memberName}' is private to '{declaringTypeName}'", source)
            .Report();

    /// <summary> Reports an attempt to access a protected member from an unrelated type. </summary>
    public void ReportCannotAccessProtectedMember(TextSpan span, string memberName, string declaringTypeName, string currentTypeName, SourceText? source = null) =>
        BuildError("MH0521", $"cannot access protected member '{memberName}' of type '{declaringTypeName}' from type '{currentTypeName}'", span, source)
            .WithPrimaryLabel(span, $"'{memberName}' is protected in '{declaringTypeName}'", source)
            .WithHelp($"type '{currentTypeName}' must inherit from '{declaringTypeName}' to access protected member '{memberName}'")
            .Report();

    /// <summary> Reports an attempt to access an internal member from an external project. </summary>
    public void ReportCannotAccessInternalMember(TextSpan span, string memberName, string declaringTypeName, SourceText? source = null) =>
        BuildError("MH0522", $"cannot access internal member '{memberName}' of type '{declaringTypeName}' from external project", span, source)
            .WithPrimaryLabel(span, $"'{memberName}' is internal to its project", source)
            .Report();

    /// <summary> Reports an attempt to access a type that is not accessible due to its protection level. </summary>
    public void ReportCannotAccessInaccessibleType(TextSpan span, string typeName, string accessibility, SourceText? source = null) =>
        BuildError("MH0523", $"cannot access {accessibility} type '{typeName}' due to its protection level", span, source)
            .WithPrimaryLabel(span, $"type '{typeName}' is {accessibility}", source)
            .Report();

    /// <summary> Reports a compile-time numeric literal overflow warning. </summary>
    public void ReportLiteralOverflowWarning(TextSpan span, string literalValue, string targetTypeName, string min, string max, SourceText? source = null) =>
        BuildWarning("MH3001", $"literal value '{literalValue}' overflows type '{targetTypeName}' (valid range is [{min}, {max}])", span, source)
            .WithPrimaryLabel(span, $"literal overflows '{targetTypeName}' at runtime", source)
            .WithHelp($"ensure the literal value fits within the range [{min}, {max}] to avoid unexpected runtime overflow")
            .Report();

    /// <summary> Reports an attempted division or remainder by zero in a constant expression or const declaration. </summary>
    public void ReportConstDivisionByZero(TextSpan span, SourceText? source = null) =>
        BuildError("MH1500", "attempted to divide by zero in a constant expression", span, source)
            .WithPrimaryLabel(span, "division by zero in constant", source)
            .WithHelp("constant expressions must evaluate to valid non-zero values at compile time")
            .Report();

    /// <summary> Reports that an expression in a const declaration cannot be evaluated at compile time. </summary>
    public void ReportInvalidConstExpression(TextSpan span, string message, SourceText? source = null) =>
        BuildError("MH1501", $"expression cannot be evaluated at compile time: {message}", span, source)
            .WithPrimaryLabel(span, "not a compile-time constant", source)
            .Report();

    /// <summary> Reports an attempt to invoke a method or operator expecting &amp;this on an r-value. </summary>
    public void ReportInvalidReceiverForByRefThis(TextSpan span, string calleeDesc, string receiverKind, SourceText? source = null) =>
        BuildError("MH2027", $"receiver expression for '{calleeDesc}' must be an addressable l-value when receiver is passed by reference ('{receiverKind}')", span, source)
            .WithPrimaryLabel(span, $"cannot pass r-value or temporary to '{receiverKind}' receiver", source)
            .WithHelp("assign the expression to a local variable or use a value receiver 'this'")
            .Report();

    /// <summary> Reports a warning when division by zero is detected in a runtime expression. </summary>
    public void ReportDivisionByZeroWarning(TextSpan span, SourceText? source = null) =>
        BuildWarning("MH3002", "division by zero detected; evaluation deferred to runtime", span, source)
            .WithPrimaryLabel(span, "division by zero will cause a runtime panic", source)
            .Report();

    /// <summary> Reports inconsistent term counts across elements in a collection expression. </summary>
    public void ReportInconsistentCollectionTermCount(TextSpan span, int actual, int expected, SourceText? source = null) =>
        BuildError("MH0568", $"inconsistent term count in collection expression: element has {actual} terms, expected {expected}", span, source)
            .WithPrimaryLabel(span, $"element has {actual} terms, expected {expected}", source)
            .Report();

    /// <summary> Reports that a collection type does not support with(...) trailer initialization. </summary>
    public void ReportCollectionMissingWithContract(TextSpan span, string typeName, SourceText? source = null) =>
        BuildError("MH0569", $"collection type '{typeName}' does not support 'with' initialization; missing 'CollectionWith' contract", span, source)
            .WithPrimaryLabel(span, "missing 'CollectionWith' operator implementation", source)
            .WithHelp($"implement the 'CollectionWith<TArgs...>' operator contract on '{typeName}'")
            .Report();

    /// <summary> Reports that a target type does not implement the Collection contract for the given term count. </summary>
    public void ReportCollectionMissingContract(TextSpan span, string typeName, int termCount, SourceText? source = null) =>
        BuildError("MH0570", $"type '{typeName}' does not implement 'Collection' contract with {termCount} terms", span, source)
            .WithPrimaryLabel(span, $"does not implement Collection with {termCount} terms", source)
            .Report();

    /// <summary> Reports that an expression in a for-in loop is not iterable. </summary>
    public void ReportTypeNotIterable(TextSpan span, string typeName, SourceText? source = null) =>
        BuildError("MH0571", $"type '{typeName}' is not iterable; missing 'Iterable' contract", span, source)
            .WithPrimaryLabel(span, $"type '{typeName}' is not iterable", source)
            .WithHelp($"implement the 'Iterable<TItem, TIterator>' operator contract on '{typeName}'")
            .Report();

    /// <summary> Reports that yield was used outside a generator function returning an Iterator or Iterable. </summary>
    public void ReportInvalidYieldUsage(TextSpan span, SourceText? source = null) =>
        BuildError("MH0572", "'yield' can only be used in a function returning an Iterator or Iterable type", span, source)
            .WithPrimaryLabel(span, "invalid 'yield' usage", source)
            .WithHelp("ensure the enclosing function returns a type implementing 'Iterator<T>' or 'Iterable<T, ...>'")
            .Report();

    /// <summary> Reports a mismatch between yielded expression type and generator iterator item type. </summary>
    public void ReportYieldTypeMismatch(TextSpan span, string yieldType, string itemType, SourceText? source = null) =>
        BuildError("MH0573", $"yield expression of type '{yieldType}' cannot be assigned to iterator item type '{itemType}'", span, source)
            .WithPrimaryLabel(span, $"expected '{itemType}', found '{yieldType}'", source)
            .Report();
}
