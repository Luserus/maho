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
./maho [options] [source-path]
```

The wrapper script forwards arguments to `dotnet run --project src/Maho.Cli/Maho.Cli.csproj -- ...`, so you can use the shorter command from the repository root.

Examples:

```bash
./maho --all Samples/Valid/Test1.mh
./maho --lex --output output/test-lex.json Samples/Valid/Test1.mh
./maho --all --progress Samples
cd Samples/Valid && ../../maho --all
```

Supported flags:

- `-l`, `--lex`: print the lexer token stream.
- `-p`, `--parse`: print the parser syntax tree.
- `-a`, `--all`: print both debug views.
- `--progress`: show per-file analysis progress on `stderr`.
- `-o`, `--output <path>`: write the requested debug views as JSON to a file.
- `-h`, `--help`: print usage information.

When no source path is provided, the CLI analyzes the current working directory recursively for `.mh` files.

Human-readable debug output is written to `stdout` when `--output` is not provided. When `--output` is present, the requested JSON is written to the file, and diagnostics, progress, and completion status messages remain on `stderr`.

## Library

The core library exposes `MahoCompiler.AnalyzeFile(...)` and `MahoCompiler.AnalyzeText(...)`.

Both APIs return:

- requested human-readable lexer output,
- requested human-readable parser output,
- requested lexer JSON,
- requested parser JSON,
- structured diagnostics with file offsets and line/column locations.
