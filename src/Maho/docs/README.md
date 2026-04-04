# Maho Core Guide

`src/Maho` is the reusable compiler library. It contains the source model, syntax model, diagnostics, public analysis result types, and placeholders for semantic layers that have not fully landed yet.

## Folder map

- [`Analysis/docs/README.md`](../Analysis/docs/README.md): public API surface and serializable result contracts.
- [`Diagnostics/docs/README.md`](../Diagnostics/docs/README.md): internal diagnostic production and message conventions.
- [`Text/docs/README.md`](../Text/docs/README.md): file loading, line parsing, and span math.
- [`Syntax/docs/README.md`](../Syntax/docs/README.md): syntax tree layout plus debug serialization hooks.
- [`Symbols/docs/README.md`](../Symbols/docs/README.md): current symbol abstractions.
- [`Resolution/docs/README.md`](../Resolution/docs/README.md): semantic-resolution placeholder boundary.

## Runtime flow inside the library

1. `MahoCompiler.AnalyzeFile(...)` or `MahoCompiler.AnalyzeText(...)` creates a `SourceText`.
2. A shared `DiagnosticsManager` is passed through analysis so lexer and parser can report into one collection.
3. The lexer and parser run.
4. Parsed compilation units are grouped into a `SyntaxTree`.
5. Resolution starts from that syntax-tree boundary rather than interleaving with parsing.
6. Diagnostics are projected into public `DiagnosticInfo` records with line/column metadata.
7. Optional lexer/parser debug JSON is emitted, depending on `AnalysisOutput`.

## Best starting points

- For public API questions: [`Analysis/docs/README.md`](../Analysis/docs/README.md)
- For "why did this span/line/column come out that way?": [`Text/docs/README.md`](../Text/docs/README.md)
- For "where did this message/code originate?": [`Diagnostics/docs/README.md`](../Diagnostics/docs/README.md)
- For "what syntax node family should I edit?": [`Syntax/docs/README.md`](../Syntax/docs/README.md)
