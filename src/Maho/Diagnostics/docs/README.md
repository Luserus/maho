# Diagnostics System Guide

The `Diagnostics` folder owns the compiler's internal diagnostic model.

This is where lexer/parser code reports problems before those problems are projected into public `DiagnosticInfo` records for the outside world.

## Files in this folder

- `DiagnosticKind.cs`: severity category used internally.
- `Diagnostic.cs`: raw internal diagnostic object.
- `DiagnosticsManager.cs`: collector and message factory.

## Type guide

### `DiagnosticKind`

Internal enum with `Info`, `Warning`, and `Error`.

It mirrors the public `DiagnosticSeverity`, but keeping an internal enum gives the compiler freedom to evolve internal reporting without immediately exposing every change to consumers.

### `Diagnostic`

Internal immutable object with:

- `DiagnosticCode`
- `Message`
- `Span`
- `Kind`

This is the compiler's native diagnostic unit before projection into the public analysis contract.

### `DiagnosticsManager`

The central diagnostic collection and reporting helper.

Important members:

- `Diagnostics`: exposes the accumulated list as `IReadOnlyList<Diagnostic>`.
- `HasErrors`: quick check for any `Error` diagnostic.

## `DiagnosticsManager` function guide

### `Report(Diagnostic diagnostic)`

The lowest-level append operation. Everything else eventually flows through here.

### `ReportInfo(...)`, `ReportWarning(...)`, `ReportError(...)`

Severity-specific convenience wrappers that create a `Diagnostic` and push it through `Report(...)`.

### Lexer-focused helpers

- `ReportBadToken(...)`
- `ReportUnterminatedString(...)`
- `ReportUnterminatedCharacter(...)`
- `ReportEmptyCharacterLiteral(...)`

These methods matter because they define stable diagnostic codes and text for lexical failures. If code or wording changes here, every consumer sees that change.

### Parser-focused helpers

- `ReportExpectedToken(...)`
- `ReportExpectedExpression(...)`
- `ReportExpectedIdentifier(...)`
- `ReportExpectedType(...)`
- `ReportExpectedSemicolon(...)`
- `ReportExpectedClosingToken(...)`
- `ReportExpectedBody(...)`
- `ReportUnexpectedToken(...)`
- `ReportMissingToken(...)`

The notable design choice is that parser diagnostics still share one message shape, "expected X, found Y", but the most common recovery sites now get dedicated codes. That keeps messages consistent while giving the CLI room to show better tips for missing semicolons, closing delimiters, and bodies.

### Private helpers

#### `CreateExpectedMessage(...)`

Builds parser error messages with an optional context fragment. This prevents every parser site from hand-rolling slightly different wording.

#### `FormatTokenText(...)`

Normalizes how found tokens are printed inside messages.

Worth noting:

- empty text becomes `<end of file>`,
- sentinel strings like `<missing>` are preserved as-is,
- normal tokens are quoted.

That small normalization step is why the same diagnostic helper can produce readable messages for both real and synthetic parser tokens.

## What is worth paying attention to here

- Diagnostic codes are effectively part of the external contract once renderers and tests start relying on them.
- `DiagnosticsManager` is intentionally stateful and centralized; it keeps message formatting out of lexer/parser control flow.
- The folder does not know about JSON, colors, or pretty terminal output. Those responsibilities belong downstream in `Analysis` and the CLI renderer.

## Reading order

1. `DiagnosticKind.cs`
2. `Diagnostic.cs`
3. `DiagnosticsManager.cs`
4. [`../../Analysis/docs/README.md`](../../Analysis/docs/README.md)
5. [`../../../Maho.Cli/docs/README.md`](../../../Maho.Cli/docs/README.md)
