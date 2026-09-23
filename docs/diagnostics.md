# Diagnostics System Guide

The `Diagnostics` folder owns the compiler's internal diagnostic model and reporting engine.

This is where lexer, parser, and semantic resolution passes report problems before those problems are projected into public `DiagnosticInfo` records for outside consumers and renderers.

---

## 1. Files in this Folder

- `DiagnosticKind.cs`: internal severity enum (`Info`, `Warning`, `Error`).
- `Diagnostic.cs`: rich internal diagnostic payload with labels, notes, help hints, and suggestions.
- `DiagnosticBuilder.cs`: fluent builder for constructing rich diagnostics with multi-span labels and remediation advice.
- `DiagnosticLabel.cs`: primary and secondary labels attached to source text spans.
- `DiagnosticSuggestion.cs`: automated code-fix replacement suggestions.
- `DiagnosticText.cs`: deferred source-backed or synthetic text used while formatting diagnostics.
- `DiagnosticsManager.cs`: thread-safe collector and centralized diagnostic factory.

---

## 2. Rich Diagnostic Architecture

Maho diagnostics support modern, Rustc-style terminal reporting with multi-span context:

### Primary and Secondary Labels
- **Primary Label (`^^^^`)**: Points directly to the offending construct or redeclaration site.
- **Secondary Label (`----`)**: Highlights related context, such as the original declaration site, previous method signature, or base class definition.

### Remediation Notes & Help
- **Notes (`note:`)**: Explanatory context about compiler rules, arity mismatches, or resolution details.
- **Help (`help:`)**: Direct, actionable instructions to remediate the error (e.g. suggesting adding `partial` or removing extra bodies).

### Automated Suggestions
- Machine-applicable text replacements specifying target span, replacement text, and human-readable intent.

---

## 3. Centralized Diagnostic Methods on `DiagnosticsManager`

All diagnostic codes and error templates are centralized in `DiagnosticsManager` and formally cataloged in [docs/diagnostic-codes.md](file:///home/luserus/SoftwareDev/Systems/Compiler/maho/docs/diagnostic-codes.md). Semantic passes and front-end stages call domain-specific factory methods:

### Lexer Diagnostics (`MH0100` - `MH0104`)
- `ReportBadToken` (`MH0100`): illegal character in source.
- `ReportUnterminatedString` (`MH0101`): string literal reaching EOF without a closing quote.
- `ReportUnterminatedCharacter` (`MH0102`): character literal syntax error.
- `ReportEmptyCharacterLiteral` (`MH0103`): empty `''` character literal.
- `ReportUnterminatedMultiLineComment` (`MH0104`): multi-line comment reaching EOF without a closing `*/`.

### Parser Diagnostics (`MH0120` - `MH0128`)
- `ReportExpectedToken` (`MH0120`): expected punctuation/keyword.
- `ReportExpectedExpression` (`MH0121`): expected expression in statement/assignment context.
- `ReportExpectedIdentifier` (`MH0122`): expected identifier for name/declaration.
- `ReportExpectedType` (`MH0123`): expected type reference syntax.
- `ReportExpectedBody` (`MH0124`): expected method or type block body.
- `ReportExpectedParameter` (`MH0125`): expected parameter in parameter list.
- `ReportExpectedGenericParameter` (`MH0126`): expected generic parameter name.
- `ReportUnexpectedToken` (`MH0120`): token encountered that violates syntax grammar.
- `ReportMissingToken` (`MH0120`): synthetic token inserted during error recovery.

### Top-Level Statements (`MH0160` - `MH0161`)
- `ReportTopLevelPragmaRequired` (`MH0160`): top-level statements in a file without `#pragma toplevel enable`.
- `ReportMultipleTopLevelSources` (`MH0161`): multiple source files attempting to define top-level statements.

### Macro Syntax (`MH0200` - `MH0201`)
- `ReportInvalidMacroDeclaration` (`MH0200`): malformed macro syntax.
- `ReportInvalidMacroPattern` (`MH0201`): malformed parameter pattern.

### Semantic Resolution Diagnostics (`MH0500` - `MH0538`)
- `ReportUnresolvedTypeReference` (`MH0500`): type name cannot be resolved in lexical or imported scopes.
- `ReportAmbiguousTypeReference` (`MH0501`): unqualified type reference matches multiple candidate types from different namespaces.
- `ReportDuplicateTypeDeclaration` (`MH0530`): duplicate type declaration.
- `ReportConflictingPartialTypeKinds` (`MH0531`): conflicting kinds on partial type.
- `ReportDuplicateFunctionDeclaration` (`MH0532`): duplicate function declaration.
- `ReportMultiplePartialFunctionBodies` (`MH0533`): multiple partial bodies.
- `ReportConflictingPartialFunctionReturnTypes` (`MH0534`): conflicting partial return types.
- `ReportDuplicateVariableDeclaration` (`MH0535`): duplicate variable declaration.
- `ReportDuplicatePropertyDeclaration` (`MH0536`): duplicate property declaration.
- `ReportCyclicTypeHierarchy` (`MH0538`): base type inheritance cycle detected.

### Macro Expansion & Metaprogramming (`MH0600` - `MH0604`)
- `ReportMacroRecursionLimitExceeded` (`MH0600`): macro recursion limit exceeded.
- `ReportNoMatchingMacroArm` (`MH0601`): no matching pattern arm.
- `ReportUnresolvedMacro` (`MH0602`): macro not found in scope.
- `ReportInvalidMacroContext` (`MH0603`): macro invoked in invalid context.
- `ReportMacroDependencyCycle` (`MH0604`): circular macro dependency.

---

## 4. Fluent Diagnostic Construction

For custom or emerging semantic diagnostics, `DiagnosticsManager` exposes a fluent `DiagnosticBuilder`:

```csharp
diagnostics.BuildError("MH1002", $"Type '{name}' is already declared.", redeclSpan, redeclSource)
    .WithPrimaryLabel(redeclSpan, $"redeclared here as non-partial")
    .WithSecondaryLabel(firstSpan, $"previous declaration is here")
    .WithHelp($"Add the 'partial' modifier to all declarations if this type is split across multiple files.")
    .Report();
```
