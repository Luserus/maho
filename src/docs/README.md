# Source Tree Guide

The `src` folder holds the two real code projects in the repository.

## Projects

- [`Maho`](../Maho/docs/README.md): the reusable compiler library.
- [`Maho.Cli`](../Maho.Cli/docs/README.md): the executable front-end that turns library results into terminal and file output.

## How to traverse `src`

- Start in [`Maho.Cli`](../Maho.Cli/docs/README.md) if your question is about command-line behavior, argument parsing, output files, status updates, or terminal rendering.
- Start in [`Maho`](../Maho/docs/README.md) if your question is about source loading, diagnostics, AST structure, analysis results, or future semantic layers.

## Suggested reading order

1. [`src/Maho.Cli/docs/README.md`](../Maho.Cli/docs/README.md)
2. [`src/Maho/docs/README.md`](../Maho/docs/README.md)
3. The specific subsystem guide inside `src/Maho/*/docs`

That order matches the actual runtime direction for most CLI-driven runs: outer orchestration first, inner compiler services second.
