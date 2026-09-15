# Maho Core Guide

`src/Maho` is the reusable compiler library. It contains the source model, syntax model, diagnostics, public analysis result types, and the semantic scaffolding that the CLI builds on.

## Folder map

- [`analysis.md`](analysis.md): public API surface and serializable result contracts.
- [`diagnostics.md`](diagnostics.md): internal diagnostic production and message conventions.
- [`source-text.md`](source-text.md): file loading, line parsing, and span math.
- [`syntax.md`](syntax.md): syntax tree layout plus debug serialization hooks.

## Runtime flow inside the library

1. `MahoCompiler.AnalyzeFile(...)`, `AnalyzeText(...)`, or `AnalyzeFiles(...)` begins one front-end run.
2. For single-file analysis, one `SourceText` is loaded and analyzed; for batch analysis, file loading and file-level front-end work are orchestrated inside the compiler library.
3. A shared `DiagnosticsManager` is passed through each file's front-end pipeline so lexer and parser can report into one collection per file.
4. The lexer and parser run.
5. Parsed compilation units are grouped into a `SyntaxTree`.
6. Resolution starts from that syntax-tree boundary rather than interleaving with parsing.
7. Diagnostics are projected into public `DiagnosticInfo` records with line/column metadata.
8. Optional lexer/parser debug JSON is emitted, depending on `AnalysisOutput`.

## Best starting points

- For public API questions: [`analysis.md`](analysis.md)
- For "why did this span/line/column come out that way?": [`source-text.md`](source-text.md)
- For "where did this message/code originate?": [`diagnostics.md`](diagnostics.md)
- For "what syntax node family should I edit?": [`syntax.md`](syntax.md)
