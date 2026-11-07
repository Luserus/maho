# Syntax System Guide

The `Syntax` folder contains both the syntax tree model and the parser/lexer implementation.

This is the densest structural part of the repository today. Even if the semantic pipeline is still early, the syntax layer already defines:

- the exact tree shape the compiler builds,
- how source text is preserved through tokens and trivia,
- how grammar context is encoded into separate node families,
- how parser/lexer state is exposed to debug tooling,
- and where future semantic work will attach once syntax is no longer the only mature stage.

The docs here go deep on structure and navigation, and they aim to stay in sync with the parser, lexer, and debug serialization code as it evolves.

## Top-level files

- `SyntaxNode.cs`: common base type for syntax nodes.
- `CompilationUnit.cs`: root node.
- `PragmaDirective.cs`: file-header analysis configuration directives.
- `SyntaxTree.cs`: batch-level parse result that groups all compilation units once parsing is done.
- `TopLevel.cs`, `Member.cs`, `Local.cs`: category base types used to separate grammar layers.
- `Token.cs`: syntax token object, including trivia and matching-keyword metadata.
- `SyntaxTrivia.cs` and `SyntaxTriviaKind.cs`: whitespace/comment side-channel attached to tokens.
- `SeparatedSyntaxList.cs`: reusable list wrapper for comma-separated or otherwise separator-bearing node sequences.
- `TokenKind.cs` and `MatchingKeywordKind.cs`: token classification enums.
- `Lexer.cs` and `Parser*.cs`: core analysis internals.

## What this folder is really responsible for

The syntax subsystem is doing more than "just parsing":

- It preserves exact token/trivia boundaries so diagnostics and tooling can stay source-faithful.
- It encodes grammatical context into the type system with separate `TopLevel`, `Member`, and `Local` families.
- It provides a stable tree shape that later resolution/binding work can consume without reparsing text.

That is why this folder contains both highly concrete data types and the analysis code that populates them.

## Runtime shape inside `Syntax`

When analysis runs through syntax today, the path is effectively:

1. `SourceText` exposes characters and line boundaries.
2. `Lexer` consumes that text and produces `Token` objects plus trivia.
3. Each `Parser` consumes one token stream and produces one `CompilationUnit`.
4. Once every file has been parsed, those roots are grouped into a `SyntaxTree`.
5. Resolution starts only after that project-wide syntax boundary exists.
6. Diagnostics reported during both stages accumulate in the shared diagnostics manager.

That means syntax is both a computation layer and a long-lived data model.

## Folder map

- [`syntax-declarations.md`](syntax-declarations.md): names, types, and declaration nodes.
- [`syntax-expressions.md`](syntax-expressions.md): expression node families.
- [`syntax-fragments.md`](syntax-fragments.md): reusable pieces that sit between declarations/statements and full grammar constructs.
- [`syntax-statements.md`](syntax-statements.md): statement node families.

## Types worth knowing at the syntax-model level

### `SyntaxNode`

The common base type. The parser debug serializer walks syntax trees through this abstraction.

### `CompilationUnit`

The root node. It owns:

- `Pragmas`: file-header directives used to configure analysis for that one file
- `Pragmas`: ordered directives; consumers derive top-level mode from these entries.
- `Members`: top-level syntax items
- `EndToken`: the terminal EOF token

### `SyntaxTree`

The parsed representation of one source file. It owns:

- `FilePath`: source file path
- `Root`: the parsed `CompilationUnit` for this file
- `Diagnostics`: diagnostics reported for this source file
- `HasErrors`: whether this file produced error diagnostics

### `TopLevel`, `Member`, `Local`

These abstract bases are worth noticing because the tree is partitioned by grammatical context instead of by a single "statement/declaration/expression only" hierarchy.

That split is why you see parallel node families for top-level and local statements in the subfolders.

It also means later semantic passes can reason from the type system about where syntax appeared, instead of re-deriving that context from parent chains or parser state.

### `Token`

The important syntax primitive outside the parser itself.

Notable fields:

- `Value` is computed from `SourceText` and `Span`, not stored independently.
- `LeadingTrivia` and `TrailingTrivia` preserve non-semantic text around the token.
- `MatchingKind` captures extra keyword classification used by debug output.

### `SyntaxTrivia`

Small but important because debug output exposes trivia explicitly and lexer serialization includes it for each token.

### `SeparatedSyntaxList<T>`

Worth understanding before editing declaration/expression nodes. The list stores both nodes and separator tokens in a single sequence, then projects the element view on demand.

That design is common in syntax trees because it preserves exact source structure without forcing every consumer to care about separators all the time.

In practice, this means formatting/debug/round-tripping tools can still see commas or separators, while semantic consumers can mostly iterate the typed elements.

## Parser file split

The parser is intentionally split across multiple files:

- `Parser.cs`: central parser type and shared state.
- `Parser.Lookahead.cs`: token inspection helpers and predictive parsing support.
- `Parser.Diagnostics.cs`: parser-specific recovery and diagnostic emission helpers.
- `Parser.Declarations.cs`: declaration grammar.
- `Parser.Expressions.cs`: expression grammar.
- `Parser.Statements.cs`: statement grammar.
- `Parser.Directives.cs`: file-header pragma grammar.

This split is worth knowing before editing anything substantial. The runtime type is still one parser, but the code is organized by grammar concern.

The lexer has a similar split:

- `Lexer.cs`: tokenization logic.
- `Lexer.Diagnostics.cs`: lexer-specific diagnostic emission helpers.
## How to read the concrete syntax tree model

If you are new to the folder, do not start by opening random parser functions. The easier route is:

1. Read `CompilationUnit.cs`, `TopLevel.cs`, `Member.cs`, and `Local.cs`.
2. Read `Token.cs` and `SyntaxTrivia.cs` so you understand the leaf-level model.
3. Open the category folder that matches the grammar feature you care about.
4. Only then jump into the corresponding parser partial.

That order lets you understand what the parser is trying to build before reading how it builds it.

## Traversal by task

- If you want to understand the shape of a declaration: start in `Declarations`, then read `Fragments`.
- If you want to understand a runtime expression form: start in `Expressions`, then jump to `Parser.Expressions.cs`.
- If you want statement placement rules: read `Statements`, paying attention to the top-level/local split.
- If you want to understand body/modifier composition: read `Fragments`.

## What to avoid assuming

- Not every node stores a concrete span; spans can be computed from child boundaries when needed.
- Not every syntactic category is shared across all contexts; many are intentionally duplicated by scope level.
- The syntax tree is source-oriented, not semantic. Similar-looking syntax forms can still become very different later in resolution.

## How to traverse this folder

- Start in `CompilationUnit.cs` and the abstract base types if you need the overall tree shape.
- Jump to the category folders when you need concrete node definitions.
- Read `Token.cs`, `SyntaxTrivia.cs`, and `SeparatedSyntaxList.cs` before touching token handling or diagnostics spans.
