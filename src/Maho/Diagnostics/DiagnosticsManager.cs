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
        string? note = null,
        string? help = null,
        SourceText? source = null)
    {
        var builder = BuildError("MH2001", $"cannot apply binary operator '{op}' to types '{lhsTypeName}' and '{rhsTypeName}'", opSpan, source)
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
    /// Reports an unsupported unary operator on an operand.
    /// </summary>
    public void ReportUnaryTypeMismatch(
        TextSpan opSpan,
        string op,
        TextSpan operandSpan,
        string operandTypeName,
        string? note = null,
        string? help = null,
        SourceText? source = null)
    {
        var builder = BuildError("MH2002", $"cannot apply unary operator '{op}' to operand of type '{operandTypeName}'", opSpan, source)
            .WithPrimaryLabel(opSpan, $"cannot apply '{op}'")
            .WithSecondaryLabel(operandSpan, $"this expression has type '{operandTypeName}'");

        if (note is not null)
            builder.WithNote(note);
        if (help is not null)
            builder.WithHelp(help);

        builder.Report();
    }

    /// <summary> Reports an assignment type mismatch. </summary>
    public void ReportAssignmentTypeMismatch(TextSpan span, string rhsTypeName, string lhsTypeName, SourceText? source = null) =>
        BuildError("MH2003", $"cannot assign expression of type '{rhsTypeName}' to variable of type '{lhsTypeName}'", span, source)
            .WithPrimaryLabel(span, $"expression has type '{rhsTypeName}', but target has type '{lhsTypeName}'", source)
            .Report();

    /// <summary> Reports a function/method argument type mismatch. </summary>
    public void ReportArgumentTypeMismatch(TextSpan span, int index, string argTypeName, string paramTypeName, SourceText? source = null) =>
        BuildError("MH2004", $"argument {index}: cannot convert from '{argTypeName}' to '{paramTypeName}'", span, source)
            .WithPrimaryLabel(span, $"has type '{argTypeName}', but expected '{paramTypeName}'", source)
            .Report();

    /// <summary> Reports a call argument count mismatch. </summary>
    public void ReportArgumentCountMismatch(TextSpan span, int expected, int actual, SourceText? source = null) =>
        BuildError("MH2005", $"expected {expected} arguments, but received {actual}", span, source)
            .WithPrimaryLabel(span, $"expected {expected} arguments, but received {actual}", source)
            .Report();

    /// <summary> Reports a return type mismatch. </summary>
    public void ReportReturnTypeMismatch(TextSpan span, string exprTypeName, string returnTypeName, SourceText? source = null) =>
        BuildError("MH2006", $"cannot convert return expression of type '{exprTypeName}' to return type '{returnTypeName}'", span, source)
            .WithPrimaryLabel(span, $"expression has type '{exprTypeName}', but function returns '{returnTypeName}'", source)
            .Report();

    /// <summary> Reports a condition expression type mismatch. </summary>
    public void ReportConditionTypeMismatch(TextSpan span, string actualTypeName, SourceText? source = null) =>
        BuildError("MH2007", $"condition expression must have type 'Bool', but found '{actualTypeName}'", span, source)
            .WithPrimaryLabel(span, $"condition has type '{actualTypeName}' instead of 'Bool'", source)
            .Report();

    /// <summary> Reports an uninferrable variable type. </summary>
    public void ReportCannotInferType(TextSpan span, string name, SourceText? source = null) =>
        BuildError("MH2008", $"cannot infer type for variable '{name}' from initializer", span, source)
            .WithPrimaryLabel(span, $"variable '{name}' requires an explicit type or a typed initializer", source)
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

    /// <summary> Reports a parser recovery site where parameter syntax was required. </summary>
    public void ReportExpectedParameter(TextSpan span, DiagnosticText found, string? context = null, SourceText? source = null, TextSpan? unexpectedSpan = null) =>
        ReportExpected("MH0125", "a parameter", found, span, context, source, unexpectedSpan);

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

    /// <summary> Reports that a constructor was declared outside of a type body. </summary>
    // Possibly syntax error but for some reason inserted into semantic one.
    public void ReportConstructorOutsideType(TextSpan span, SourceText? source = null) =>
        BuildError("MH0520", "constructor must be declared inside a type body", span, source)
            .WithPrimaryLabel(span, "constructor declared outside a type", source)
            .WithHelp("move the constructor inside a class, struct, or union declaration")
            .Report();

    /// <summary> Reports that a constructor name does not match the enclosing type name. </summary>
    public void ReportConstructorNameMismatch(string constructorName, string enclosingTypeName, TextSpan span, SourceText? source = null) =>
        BuildError("MH0525", $"constructor name '{constructorName}' does not match enclosing type name '{enclosingTypeName}'", span, source)
            .WithPrimaryLabel(span, $"constructor name must match enclosing type '{enclosingTypeName}'", source)
            .WithHelp($"rename the constructor to '{enclosingTypeName}' or provide a return type if this is a method")
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
        BuildError("MH0242", $"invalid signature for operator function '{operatorName}': {reason}", span, source)
            .WithPrimaryLabel(span, reason, source)
            .Report();

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
        BuildError("MH0544", $"type '{typeName}' does not satisfy the ABI contract for intrinsic '{intrinsicName}': {reason}", span, source)
            .WithPrimaryLabel(span, reason, source)
            .Report();

    /// <summary> Reports that syntax requires an intrinsic type that was not declared or failed verification. </summary>
    public void ReportMissingIntrinsicForSyntax(TextSpan span, string syntaxDescription, string requiredIntrinsic, SourceText? source = null) =>
        BuildError("MH0240", $"{syntaxDescription} requires intrinsic type '{requiredIntrinsic}' to be declared", span, source)
            .WithPrimaryLabel(span, $"requires intrinsic '{requiredIntrinsic}'", source)
            .WithHelp($"declare a type with [IntrinsicType(\"{requiredIntrinsic}\")] or reference a library that defines it")
            .Report();

    /// <summary> Reports a non-exhaustive pattern match. </summary>
    public void ReportNonExhaustiveMatch(TextSpan span, string missingVariant, SourceText? source = null) =>
        BuildError("MH0605", $"match expression is not exhaustive; missing variant '{missingVariant}'", span, source)
            .WithPrimaryLabel(span, $"missing pattern arm for variant '{missingVariant}'", source)
            .WithHelp("add the missing variant arm or a wildcard pattern '_' to make the match exhaustive")
            .Report();

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
}