# Analysis Contract Guide

The `Analysis` folder is the public-facing bridge between the internal compiler pipeline and external consumers such as the CLI.

This folder matters because it defines what leaves the compiler:

- which debug payloads exist,
- how diagnostics are exposed,
- and how text spans are projected into line/column information.

## Files in this folder

- `CompilerAnalysis.cs`: the public static entrypoint on `MahoCompiler`.
- `CompilerAnalysisResult.cs`: immutable result payload.
- `AnalysisOutput.cs`: flags that decide which debug payloads are included.
- `DiagnosticInfo.cs`: public diagnostic record.
- `DiagnosticSeverity.cs`: public severity enum.
- `TextLocation.cs`: public line/column pair.
- `TextSpanInfo.cs`: public span with both absolute offsets and line/column endpoints.
- `DebugJson.cs`: internal serializer helpers plus debug DTOs for lexer/parser output.

## `MahoCompiler` function guide

### `AnalyzeFile(string filePath, AnalysisOutput output = AnalysisOutput.None)`

Loads source from disk, validates the input path argument, and forwards to `AnalyzeCore(...)`.

Important detail:

- this method owns path normalization,
- text loading happens through `SourceText` and `SourceFile`, not ad hoc file reads.

### `AnalyzeText(string sourceText, AnalysisOutput output = AnalysisOutput.None, string sourcePath = "<memory>")`

The in-memory companion to `AnalyzeFile(...)`. It is the API to use for tests, editor integrations, or any caller that already has source text in memory.

The notable behavior is that it still requires a `sourcePath` string so the downstream result has a stable identity, even for virtual documents.

### `AnalyzeCore(SourceText text, string sourcePath, AnalysisOutput output)`

This is the orchestration seam inside the library:

1. create one `DiagnosticsManager`,
2. run the lexer,
3. run the parser,
4. wrap the parsed root in a `SyntaxTree`,
5. start resolution only after that syntax-tree boundary exists,
6. project diagnostics,
7. optionally serialize lexer/parser debug views,
8. return a `CompilerAnalysisResult`.

The lexer/parser internals are intentionally out of scope for these docs, but this method is still important because it defines the analysis contract boundary.

### `CreateDiagnostics(DiagnosticsManager diagnosticsManager, SourceText text)`

Projects internal `Diagnostic` objects into public `DiagnosticInfo` records. This is where the internal diagnostics model stops and the external, serializable model begins.

### `MapSeverity(DiagnosticKind kind)`

Converts internal diagnostic kinds into the public `DiagnosticSeverity` enum.

### `CreateSpanInfo(TextSpan span, SourceText text)`

Builds a `TextSpanInfo` that contains:

- zero-based absolute positions (`Start`, `End`, `Length`),
- one-based user-facing line/column pairs (`StartLocation`, `EndLocation`).

That mixed representation is intentional: offsets stay machine-friendly, locations stay human-friendly.

## Public result types

### `AnalysisOutput`

A `[Flags]` enum with `None`, `Lexer`, and `Parser`.

Worth noting:

- the flags control optional debug payloads only,
- diagnostics are always produced.

### `CompilerAnalysisResult`

The top-level immutable result returned by `MahoCompiler`.

Fields:

- `SourcePath`: source identity used by the caller and renderer.
- `LexerJson`: optional debug JSON for the lexer.
- `ParserJson`: optional debug JSON for the parser.
- `Diagnostics`: structured diagnostics for normal API use.
- `DiagnosticsJson`: serialized diagnostics payload for consumers that prefer JSON transport.

#### `HasErrors`

Scans the diagnostics list for any `Error` severity. It is intentionally derived rather than stored so the result cannot drift out of sync with the diagnostics collection.

### `DiagnosticInfo`

Public diagnostic record with code, message, severity, and a fully projected span.

### `DiagnosticSeverity`

Public severity enum: `Info`, `Warning`, `Error`.

### `TextLocation`

Public line/column pair. Used inside `TextSpanInfo`.

### `TextSpanInfo`

Public span type for API consumers. It keeps both raw offsets and line/column endpoints so callers do not need access to the original `SourceText` just to display a location.

## `DebugJson` guide

`DebugJson` is the internal serialization utility for lexer and parser debug payloads.

### `Serialize<T>(T value)`

Serializes with camel-case property names, indentation, and null omission. This is the single serializer configuration for compiler-owned debug payloads.

### `CreateSpan(SourceText text, TextSpan span)`

Projects an internal `TextSpan` into a `DebugTextSpanInfo`. It reuses `MahoCompiler.CreateSpanInfo(...)` so debug payload spans and public diagnostic spans stay consistent.

### `CreateTrivia(SourceText text, IReadOnlyList<SyntaxTrivia> trivias)`

Builds debug trivia items with kind, captured text, and span metadata.

### `GetMatchingKind(MatchingKeywordKind kind)`

Converts `MatchingKeywordKind.None` into `null` instead of the literal string `"None"`, which keeps the serialized payload cleaner.

### `GetDisplayText(Token token)`

Normalizes sentinel token text for debug views:

- `EndToken` becomes `<eof>`
- `MissingToken` becomes `<missing>`

That makes downstream renderers simpler because they do not need to guess from empty spans or synthetic token kinds.

## Debug DTOs

The rest of `DebugJson.cs` is a serializer-facing schema:

- `DebugTextSpanInfo`
- `DebugSyntaxTriviaInfo`
- `DebugLexerTokenInfo`
- `DebugLexerInfo`
- `DebugParserChildInfo`
- `DebugParserNodeInfo`
- `DebugParserInfo`

These are not compiler-domain nodes. They are transport types designed for inspection, logging, and CLI rendering.

## Reading tips

- Start with `CompilerAnalysis.cs` if you want the public API.
- Start with `DebugJson.cs` if you are changing emitted lexer/parser JSON.
- Jump to [`../Text/docs/README.md`](../../Text/docs/README.md) if span math or line/column projection looks wrong.
- Jump to [`../Diagnostics/docs/README.md`](../../Diagnostics/docs/README.md) if the payload content is wrong before serialization even happens.
