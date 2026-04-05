# Maho Core Guide

`src/Maho` is the reusable compiler library. It contains the source model, syntax model, diagnostics, public analysis result types, and the semantic scaffolding that the CLI builds on.

## Folder map

- [`Analysis/docs/README.md`](../Analysis/docs/README.md): public API surface and serializable result contracts.
- [`Diagnostics/docs/README.md`](../Diagnostics/docs/README.md): internal diagnostic production and message conventions.
- [`Text/docs/README.md`](../Text/docs/README.md): file loading, line parsing, and span math.
- [`Syntax/docs/README.md`](../Syntax/docs/README.md): syntax tree layout plus debug serialization hooks.
- [`Symbols/docs/README.md`](../Symbols/docs/README.md): current symbol abstractions and declaration keys.
- [`Resolution/docs/README.md`](../Resolution/docs/README.md): semantic coordination layer and pass model.

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

- For public API questions: [`Analysis/docs/README.md`](../Analysis/docs/README.md)
- For "why did this span/line/column come out that way?": [`Text/docs/README.md`](../Text/docs/README.md)
- For "where did this message/code originate?": [`Diagnostics/docs/README.md`](../Diagnostics/docs/README.md)
- For "what syntax node family should I edit?": [`Syntax/docs/README.md`](../Syntax/docs/README.md)
- For "how do semantic passes and scopes fit together?": [`Resolution/docs/README.md`](../Resolution/docs/README.md)
