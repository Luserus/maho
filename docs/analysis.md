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
- `DiagnosticInfo.cs`: public diagnostic record.
- `DiagnosticSeverity.cs`: public severity enum.
- `TextLocation.cs`: public line/column pair.
- `TextSpanInfo.cs`: public span with both absolute offsets and line/column endpoints.

*(Note: Domain-specific `.mhpr` project loading, workspace orchestration, and multi-project dependency resolution are owned by `Miryo` in `src/Miryo/Build`, documented in [`miryo.md`](miryo.md). The core compiler operates purely on source text and files.)*

## `MahoCompiler` function guide

### `CompileSource(string source, string filePath = "source.mh", CompilationOptions? options = null, Compilation? cachedCompilation = null)`

Compiles an in-memory source string and returns an immutable `Compilation`. It is the primary API for in-memory analysis, unit tests, and editor integrations, accepting an optional `cachedCompilation` for incremental tree and symbol reuse.

### `AnalyzeFiles(IEnumerable<string> filePaths, string? rootPath = null, CompilationOptions? options = null, ...)`

Coordinates file-level parallel parsing and front-end resolution across multiple source files:
- input paths are normalized up front,
- parallel syntax phase parses compilation units under `Parallel.For`, reusing unchanged syntax trees when `cachedCompilation` is provided,
- runs the multi-pass `Resolver` across all compilation units,
- measures and records execution duration in `CompilationPhaseTimers`,
- returns an immutable `Compilation` object keeping file trees in input order.

### `CompileFiles(...)` and `CompileSource(...)`

These are the compiler entry points used for full compilation runs. They execute front-end lexing, parsing, and the complete 6-pass semantic resolution pipeline via `Resolver` (`MacroExpansionPass`, `SymbolDiscoveryPass`, `IntrinsicDiscoveryPass`, `DeclarationResolutionPass`, `ExpressionResolutionPass`, and `ControlFlowAndReturnInferencePass`). If front-end analysis succeeds with zero errors, execution reaches the lowering/codegen boundary (currently throwing `CompilerPipelineNotImplementedException` with error code `MH9000`).

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

### `SyntaxTree`

Parsed representation of one source file within a compilation.

Fields & Query Methods:

- `FilePath`: normalized path of the source file.
- `Root`: root compilation unit AST node.
- `Diagnostics`: ordered diagnostics associated with this source file.
- `HasErrors`: whether this file produced any fatal or error diagnostics.

### `Compilation`

Top-level immutable compilation object returned by all compiler APIs (`CompileFiles(...)`, `AnalyzeFiles(...)`, `CompileSource(...)`, `CreateCompilation(...)`).

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
