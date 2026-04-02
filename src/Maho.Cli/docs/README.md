# CLI System Guide

`src/Maho.Cli` is the executable shell around the compiler library. It does four jobs:

1. parse command-line options,
2. resolve one file or many files,
3. call `MahoCompiler`,
4. render diagnostics and optional debug views to either the terminal or JSON files.

## Files in this folder

- `Program.cs`: minimal process entrypoint.
- `CommandLine.cs`: option parsing, file discovery, orchestration, output routing, and status messaging.
- `SerializedAnalysisRenderer.cs`: turns serialized analysis JSON back into colored, human-readable terminal output.
- [`json-and-output-pipeline.md`](json-and-output-pipeline.md): detailed explanation of the CLI's `System.Text.Json` usage, renderer DTOs, and the print-vs-write-to-file flow.

## Entry point

### `Program.Main(string[] args)`

This is intentionally tiny. All real behavior is delegated to `CommandLine.Run(args)` so the process entrypoint stays stable while CLI logic grows elsewhere.

## `CommandLine` type map

### Constants and shared state

- ANSI color constants: centralized here so every terminal-facing status message uses the same palette.
- `JsonOptions`: used for CLI-owned JSON envelopes, not for the core compiler's internal debug JSON serializer.
- `statusLock` and `pendingStatusSeparator`: the important detail in this file. Analysis can run in parallel, but status messages still need predictable spacing and no interleaving on `stderr`.

### Nested types

- `DiagnosticOutputFormat`: internal choice between human-readable diagnostics and a JSON diagnostics envelope.
- `CliOptions`: the normalized result of argument parsing. This is the boundary between "stringly" CLI input and structured execution settings.
- `FileResult`: per-file execution result. It carries the source identity, the successful analysis payload when available, the caught error text when not, and whether that failure should be shown as a user-facing error or an internal compiler failure.
- `AnalysisProgress`: tiny helper that owns the progress counter and synchronizes progress updates onto `stderr`.

### `AnalysisProgress.ReportAnalyzing(string displayPath)`

Worth noting because it increments progress under a lock and writes to `stderr`, not `stdout`. That separation lets normal output remain machine-readable when redirected, while progress stays ephemeral.

## `CommandLine` function guide

### `Run(string[] args)`

This is the canonical CLI control flow:

1. parse arguments,
2. reject incompatible combinations early,
3. resolve the input path,
4. expand directories into sorted `*.mh` files,
5. analyze files in parallel,
6. emit debug output,
7. emit diagnostics,
8. emit final completion messages,
9. choose exit code from analysis/write success.

Important details:

- It keeps analysis parallel but output ordered by storing results first and rendering later.
- It explicitly blocks the combination "JSON diagnostics to stdout" plus "debug views to stdout", because both would compete for the same stream.
- It returns `1` for either compiler errors or output write failures.

### `AnalyzeFile(string filePath, string displayPath, AnalysisOutput output, AnalysisProgress? progress)`

This is the per-file safety boundary. It reports progress, calls `MahoCompiler.AnalyzeFile`, and catches exceptions so a single bad file or filesystem error becomes a `FileResult` instead of crashing the entire batch.

The key distinction here is `IsInternalError`: expected path/IO issues are surfaced as user-facing failures, while unexpected exceptions are tagged as internal failures.

### `BuildDebugOutput(string inputPath, IReadOnlyList<FileResult> results)`

Builds the CLI-owned JSON envelope for debug data. It does not regenerate debug info; it just repackages the library's serialized lexer/parser JSON together with file identity and diagnostics.

Worth noting:

- diagnostics are included alongside debug output for successful analyses,
- lexer/parser blocks are only present when the core library produced them,
- failed files still appear in the envelope with `analysisError`.

### `BuildDiagnosticsTextOutput(IReadOnlyList<FileResult> results, bool useColor)`

This is the text diagnostics aggregator. It delegates successful analyses to `SerializedAnalysisRenderer.RenderDiagnosticsOutput(...)` and delegates failures to either `RenderInternalFailure(...)` or `RenderUserFacingFailure(...)`.

That split is important because it keeps the formatting logic centralized while still preserving the distinction between compiler bugs and normal user mistakes.

### `BuildDiagnosticsJsonOutput(string inputPath, IReadOnlyList<FileResult> results)`

Builds a diagnostics-only JSON envelope for tooling or snapshotting. Failed files still receive an entry, but with an empty diagnostics array plus an optional `analysisError`.

### `TryWriteOutputFile(...)`

Creates parent directories when needed, writes content, and normalizes errors into CLI-friendly messages. This keeps file output behavior consistent for both debug JSON and diagnostics JSON/text output.

### `TryResolveInputFiles(...)`

Handles the "single file or recursive directory" input model. The important behavior here is:

- single files are analyzed directly,
- directories are searched recursively for `*.mh`,
- directory results are sorted ordinally for deterministic output,
- empty directories are treated as an error rather than a no-op.

### `TryGetSourcePath(...)`

Normalizes the user-provided path or falls back to the current working directory when no path is passed.

### `FormatAnalysisError(...)`, `IsUserFacingError(...)`, `FormatPathOrIoError(...)`

These methods define the CLI's error taxonomy. They are worth reading together:

- `IsUserFacingError(...)` identifies expected environmental failures.
- `FormatPathOrIoError(...)` turns those failures into stable messages.
- `FormatAnalysisError(...)` decides whether an exception should expose only its message or receive path-aware formatting.

### `TryParseArguments(...)`

Consumes raw `args` and produces `CliOptions`. This is where option interactions are enforced:

- `--all` expands to both lexer and parser output,
- only one source path is allowed,
- unknown flags fail fast,
- `--output` without `--lex`, `--parse`, or `--all` is rejected because there would be nothing to write.

### `TryParseDiagnosticsFormat(...)`

Accepts `text`, `txt`, or `json`. Everything else fails early with a targeted error.

### `TryReadArgumentValue(...)`

Small helper, but it matters because it keeps missing-value errors consistent across all path-valued options.

### `GetDefaultSourcePath()`

Returns the full path to the current working directory, making the no-argument behavior explicit.

### `PrintUsage(TextWriter writer)`

The single source of truth for help text. If CLI flags change, this and `TryParseArguments(...)` should change together.

### `WriteStatus(...)` and `WriteStatusSeparatorIfNeeded()`

These two methods keep `stderr` readable when progress messages, completion messages, and normal output all happen in the same process run.

### `Colorize(...)` and `ShouldUseColor()`

CLI status color is only enabled for an interactive `stderr`, and it respects `NO_COLOR` plus `TERM=dumb`.

## `SerializedAnalysisRenderer` guide

This file is where serialized analysis artifacts become terminal-friendly text.

### Why it exists

The core library produces JSON for debug output and structured diagnostics for public consumption. The CLI wants pretty output, but it should not depend on live parser/lexer objects. This renderer sits at that boundary and works only from serialized payloads plus the source file on disk.

### Main entry points

#### `RenderDebugOutput(...)`

Reads serialized lexer and parser JSON from `CompilerAnalysisResult`, renders either or both sections, and adds optional file headers for multi-file runs.

Important behavior:

- if neither lexer nor parser JSON exists, it returns an empty string,
- rendering failures are downgraded into a synthetic internal failure block instead of throwing.

#### `RenderDiagnosticsOutput(...)`

Deserializes diagnostics, reloads the source file, and prints rich context blocks with underlines and tips.

Two things matter here:

- diagnostics are sorted by source location before printing,
- if the source file cannot be reloaded, it falls back to summary-only diagnostics instead of losing the whole report.

#### `RenderInternalFailure(...)` and `RenderUserFacingFailure(...)`

These keep failures visually distinct from normal diagnostics. Internal failures carry a synthetic code, `MHC9999`, to make the output immediately recognizable.

### Debug rendering helpers

- `RenderLexerOutput(...)`: shows token order, kind, matching keyword annotation, text, span, and trivia summaries.
- `RenderParserOutput(...)`: prints a tree view rooted at the serialized parser node.
- `AppendParserNode(...)`: handles the recursive tree formatting.
- `FormatNode(...)`: decides whether a serialized node should render as a syntax node line or a token line.

### Diagnostics rendering helpers

- `PrintDiagnostics(...)`: sorts diagnostics by line, then column, then original order.
- `PrintDiagnostic(...)`: prints summary plus contextual source excerpt.
- `PrintDiagnosticSummary(...)`: emits the header line with location, severity, code, and message.
- `PrintDiagnosticContext(...)`: prints up to three source lines, carets, and a continuation note for longer spans.
- `PrintDiagnosticTip(...)`: prints a small remediation hint derived from the diagnostic code.

### Span and underline math

These helpers are more important than they look:

- `GetUnderlineStart(...)`
- `GetUnderlineWidth(...)`
- `ExpandIndentation(...)`
- `GetExpandedWidth(...)`

They are what keep tabs and zero-length spans from producing broken caret alignment. If a diagnostic underline looks wrong, start here before assuming the span producer is wrong.

### Message and style helpers

- `GetDiagnosticTip(...)`: small hints for a few known diagnostics, with parser-specific wording mostly derived from the serialized expected text.
- `GetDiagnosticColor(...)`: maps severities to colors.
- `FormatTriviaSummary(...)` and `FormatTriviaKinds(...)`: compact trivia presentation for lexer output.
- `FormatTokenValue(...)`, `FormatSpan(...)`, `Escape(...)`: formatting helpers for token/debug text.
- `DeserializeJson<T>(...)`: single generic deserialization gate for all renderer-owned DTOs.
- `Colorize(...)` and `ShouldUseColor()`: same idea as the CLI status helpers, but tied to `stdout` because renderer output goes there.

### `SourceBuffer` and renderer-local model types

`SerializedAnalysisRenderer` deliberately carries its own tiny source-buffer and DTO types:

- `SourceLine`
- `SourceBuffer`
- `SerializedTextSpanInfo`
- `SerializedSyntaxTriviaInfo`
- `SerializedLexerTokenInfo`
- `SerializedLexerInfo`
- `SerializedParserChildInfo`
- `SerializedParserNodeInfo`
- `SerializedParserInfo`

That duplication is intentional. The CLI should be able to render from serialized payloads without depending on internal parser data structures or the exact serializer-side record types in the core library.

## Suggested reading order

1. `Program.cs`
2. `CommandLine.Run(...)`
3. `AnalyzeFile(...)`
4. output builders in `CommandLine.cs`
5. `SerializedAnalysisRenderer`
6. [`json-and-output-pipeline.md`](json-and-output-pipeline.md)
