# Maho

An experimental programming language and compiler project inspired by C#.

## Current Status

The repository is currently split into two projects:

- `src/Maho/Maho.csproj`: the reusable core library.
- `src/Maho.Cli/Maho.Cli.csproj`: the command-line executable.
- `Maho.sln`: solution file for editor/LSP support across both projects.

Today the core can:

- load `.mh` source files,
- lex and parse them,
- return lexer and parser debug views as JSON,
- report structured diagnostics for invalid syntax.

Semantic analysis, resolution, and code generation are still in progress.

## Build

This project uses the .NET SDK and targets `net10.0`.

If you want editor features such as C# solution loading, navigation, and language server support, open the repository through `Maho.sln`.

Build the CLI entrypoint from the repository root:

```bash
dotnet build src/Maho.Cli/Maho.Cli.csproj
```

That also builds the core library through the project reference.

To build the library on its own:

```bash
dotnet build src/Maho/Maho.csproj
```

## CLI

Run the CLI with:

```bash
dotnet run --project src/Maho.Cli/Maho.Cli.csproj -- [options] [source-path]
```

Examples:

```bash
dotnet run --project src/Maho.Cli/Maho.Cli.csproj -- --all src/Maho/Test.mh
dotnet run --project src/Maho.Cli/Maho.Cli.csproj -- --lex --output artifacts/lex.json src/Maho/Test.mh
dotnet run --project src/Maho.Cli/Maho.Cli.csproj -- --all --progress src/Maho/Samples
```

Supported flags:

- `-l`, `--lex`: emit lexer JSON.
- `-p`, `--parse`: emit parser JSON.
- `-a`, `--all`: emit both lexer and parser JSON.
- `--progress`: show per-file analysis progress on `stderr`.
- `-o`, `--output <path>`: write the emitted JSON payload to a file instead of `stdout`.
- `-h`, `--help`: print usage information.

When no source path is provided, the CLI analyzes `src/Maho/Test.mh`.

JSON output is machine-readable and written to `stdout` by default. Diagnostics, progress, and completion status messages are written to `stderr`, so other tools can safely pipe or deserialize the JSON stream.

## Library

The core library exposes `MahoCompiler.AnalyzeFile(...)` and `MahoCompiler.AnalyzeText(...)`.

Both APIs return:

- requested lexer JSON,
- requested parser JSON,
- structured diagnostics with file offsets and line/column locations.
