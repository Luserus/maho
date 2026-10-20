# Analysis Contract Guide

The `Analysis` folder is the public-facing bridge between the internal compiler pipeline and external consumers such as the CLI.

This folder matters because it defines what leaves the compiler:

- which debug payloads exist,
- how diagnostics are exposed,
- and how text spans are projected into line/column information.

## Files in this folder

- `MahoCompiler.cs`: public compiler entrypoint and orchestration for single-file and batch analysis.
- `Compilation.cs`: root compilation coordination object managing source units, referenced compilations, and resolution passes.
- `AnalysisSession.cs`: stateful incremental compilation session for interactive evaluation and REPL environments.
- `CompilationPhaseTimers.cs`: measures duration across compilation pipeline phases (`Syntax`, `SemanticAnalysis`, `Lowering`, `Total`).
- `AnalysisOutput.cs`: flags that decide which compiler outputs are enabled.
- `DiagnosticInfo.cs`: public diagnostic record.
- `DiagnosticSeverity.cs`: public severity enum.
- `TextLocation.cs`: public line/column pair.
- `TextSpanInfo.cs`: public span with both absolute offsets and line/column endpoints.

*(Note: Domain-specific `.mhpr` project loading, workspace orchestration, and multi-project dependency resolution are owned by `Miryo` in `src/Miryo/Build`, documented in [`miryo.md`](miryo.md). The core compiler operates purely on source text and files.)*

## `MahoCompiler` function guide

### `AnalyzeFile(string filePath, AnalysisOutput output = AnalysisOutput.None, CompilationOptions? options = null)`

Loads source from disk, validates the input path argument, and forwards to `AnalyzeCore(...)`.

Important details:
- owns path normalization,
- text loading happens through `SourceText` and `SourceFile`, not ad hoc file reads,
- executes the resolution pipeline through `DeclarationResolutionPass`.

### `AnalyzeText(string sourceText, AnalysisOutput output = AnalysisOutput.None, string sourcePath = "<memory>", CompilationOptions? options = null)`

The in-memory companion to `AnalyzeFile(...)`. It is the primary API for unit tests, editor integrations, or any caller with source text in memory. It accepts a `sourcePath` string so downstream diagnostics and spans have a stable virtual identity.

### `AnalyzeFiles(IReadOnlyList<string> filePaths, AnalysisOutput output = AnalysisOutput.None, string rootDirectory = "", CompilationOptions? options = null)`

The batch companion to `AnalyzeFile(...)`. It coordinates file-level parallel parsing and front-end work across multiple source files:
- input paths are normalized up front,
- parallel syntax phase parses compilation units under `Parallel.For`,
- parsed units are united under a single `SyntaxTree`,
- runs the multi-pass `Resolver` across all compilation units,
- measures and records execution duration in `CompilationPhaseTimers`,
- returns an immutable `Compilation` object keeping file trees in input order.

### `CompileFiles(...)` and `CompileSource(...)`

These are the compiler entry points used for full compilation runs. They execute front-end lexing, parsing, and semantic resolution passes (`SymbolDiscoveryPass` and `DeclarationResolutionPass`). If front-end analysis succeeds with zero errors, execution reaches the lowering/codegen boundary (currently throwing `CompilerPipelineNotImplementedException` with error code `MH9000`).

Both methods record phase durations in `CompilationPhaseTimers` and expose `Elapsed` representing total core compilation time.

### `AnalyzeCore(SourceText text, string sourcePath)`

This is the orchestration seam inside the library:

1. create one `DiagnosticsManager`,
2. run the lexer and parser, measuring `Syntax` duration,
3. wrap the parsed root in a `SyntaxTree`,
4. start resolution, measuring `SemanticAnalysis` duration,
5. project diagnostics,
6. record phase timers,
7. return a `Compilation`.

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

### `SyntaxTree`

Parsed representation of one source file within a compilation.

Fields & Query Methods:

- `FilePath`: normalized path of the source file.
- `Root`: root compilation unit AST node.
- `Diagnostics`: ordered diagnostics associated with this source file.
- `HasErrors`: whether this file produced any fatal or error diagnostics.

### `Compilation`

Top-level immutable compilation object returned by all compiler APIs (`CompileFiles(...)`, `AnalyzeFiles(...)`, `CompileSource(...)`, `AnalyzeText(...)`, `CreateCompilation(...)`).

Fields & Query Methods:

- `ProjectName`: friendly identity for the compiled unit or batch.
- `SyntaxTrees` / `Files`: ordered parsed syntax trees for all files.
- `Diagnostics`: aggregated, source-ordered diagnostics across all files and resolution passes.
- `GetDiagnostics(string? filePath = null)`: queries diagnostics, optionally filtered by source file.
- `GetSyntaxTree(string filePath)`: retrieves a specific file's syntax tree.
- `EntryFile`: selected explicit or unambiguous implicit entry file, when applicable.
- `PhaseTimers`: breakdown of duration across pipeline phases (`CompilationPhaseTimers`).
- `Elapsed`: total core compilation duration across all phases (`PhaseTimers.Total`).
- `HasErrors`: whether any file in the compilation reported error-severity diagnostics.

### `CompilationPhaseTimers`

Record measuring execution duration across distinct compiler phases:

- `Syntax`: combined duration of lexing and parsing across all input source files (multithreaded).
- `SemanticAnalysis`: duration of symbol discovery and declaration resolution passes.
- `Lowering`: duration of lowering and code generation.
- `Total`: combined duration across all core compilation phases (`Syntax + SemanticAnalysis + Lowering`).

### `DiagnosticInfo`

Public diagnostic record with code, message, severity, and a fully projected span.

### `DiagnosticSeverity`

Public severity enum: `Info`, `Warning`, `Error`.

### `TextLocation`

Public line/column pair. Used inside `TextSpanInfo`.

### `TextSpanInfo`

Public span type for API consumers. It keeps both raw offsets and line/column endpoints so callers do not need access to the original `SourceText` just to display a location.

## Reading tips

- Start with `MahoCompiler.cs` if you want the public API.
- Jump to [`source-text.md`](source-text.md) if span math or line/column projection looks wrong.
- Jump to [`diagnostics.md`](diagnostics.md) if the payload content is wrong before serialization even happens.
