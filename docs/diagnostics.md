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

All diagnostic codes and error templates are centralized in `DiagnosticsManager`. Semantic passes and front-end stages call domain-specific factory methods:

### Lexer Diagnostics (`MH0000` - `MH0003`, `MH0013`)
- `ReportBadToken`: illegal character in source.
- `ReportUnterminatedString`: string literal reaching EOF without a closing quote.
- `ReportUnterminatedCharacter`: character literal syntax error.
- `ReportEmptyCharacterLiteral`: empty `''` character literal.
- `ReportUnterminatedMultiLineComment` (`MH0013`): multi-line comment reaching EOF without a closing `*/`.

### Parser Diagnostics (`MH0004` - `MH0010`)
- `ReportExpectedToken`: expected punctuation/keyword.
- `ReportExpectedExpression`: expected expression in statement/assignment context.
- `ReportExpectedIdentifier`: expected identifier for name/declaration.
- `ReportExpectedType`: expected type reference syntax.
- `ReportExpectedBody`: expected method or type block body.
- `ReportExpectedParameter`: expected parameter in parameter list.
- `ReportExpectedGenericParameter`: expected generic parameter name.
- `ReportUnexpectedToken`: token encountered that violates syntax grammar.
- `ReportMissingToken`: synthetic token inserted during error recovery.

### Top-Level Statements (`MH0011` - `MH0012`)
- `ReportTopLevelPragmaRequired` (`MH0011`): top-level statements in a file without `#pragma toplevel enable`.
- `ReportMultipleTopLevelSources` (`MH0012`): multiple source files attempting to define top-level statements.

### Semantic Resolution Diagnostics (`MH1000` - `MH1006`)
- `ReportUnresolvedTypeReference` (`MH1000`): type name cannot be resolved in lexical or imported scopes.
- `ReportAmbiguousTypeReference` (`MH1001`): unqualified type reference matches multiple candidate types from different namespaces.
- `ReportDuplicateTypeDeclaration` (`MH1002`): duplicate type declaration with matching arity where at least one declaration is not marked `partial`. Includes primary label on redeclaration, secondary label on original declaration, and `help:` suggestion.
- `ReportDuplicateFunctionDeclaration` (`MH1003`):
  - Duplicate non-partial function declarations matching in parameter signatures and generic arity.
  - Partial function declaration violations: multiple declarations with implementation bodies (primary label on conflicting body, secondary label on first body).
- `ReportCyclicTypeHierarchy` (`MH1004`): base type inheritance cycle detected.
- `ReportDuplicateVariableDeclaration` (`MH1005`): duplicate global variable or member field declaration.
- `ReportDuplicatePropertyDeclaration` (`MH1006`): duplicate property declaration in the same type.

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
