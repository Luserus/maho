# Source Tree Guide

The `src` folder holds the two real code projects in the repository.

## Projects

- [`compiler-library.md`](compiler-library.md): the reusable compiler library.
- [`cli.md`](cli.md): the executable front-end that turns library results into terminal and file output.

## How to traverse `src`

- Start in [`cli.md`](cli.md) if your question is about command-line behavior, argument parsing, output files, status updates, or terminal rendering.
- Start in [`compiler-library.md`](compiler-library.md) if your question is about source loading, diagnostics, AST structure, analysis results, or future semantic layers.

## Suggested reading order

1. [`cli.md`](cli.md)
2. [`compiler-library.md`](compiler-library.md)
3. The relevant subsystem guide in this directory.

That order matches the actual runtime direction for most CLI-driven runs: outer orchestration first, inner compiler services second.
