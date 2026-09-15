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
- report structured diagnostics for invalid syntax,
- run project-wide resolution over parsed compilation units.

The semantic layer is still growing, and code generation is not implemented yet.

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
./maho Samples/Test.mhpr
./maho --debug --lex --output output/test-lex.json Samples/Program.mh
./maho --debug --lex --parse --output - --diagnostics --json --output - Samples/Test.mhpr
cd Samples && ../maho Test.mhpr
```

Supported flags:

- `--debug (--lex|--parse)+ --output <path|->`: emit selected debug payloads to a file or `stdout`.
- `--diagnostics [--text|--json] --output <path|->`: emit diagnostics to a file or `stderr`.
- `-h`, `--help`: print usage information.

When no source path is provided, the CLI analyzes the current working directory recursively for `.mh` files.

Normal invocations proceed into the compiler pipeline. The current lowering/code-generation boundary is
deliberately unimplemented, so syntactically valid programs stop there with a compiler error. Debug
output is therefore an explicit inspection channel rather than the compiler's final product.

## Library

The core library exposes `MahoCompiler.AnalyzeFile(...)` and `MahoCompiler.AnalyzeText(...)`.

It also exposes `MahoCompiler.AnalyzeFiles(...)` for batch analysis, which keeps file-level parallelism inside the library instead of making the CLI manage it directly.

The analysis APIs return:

- requested lexer JSON,
- requested parser JSON,
- structured diagnostics with file offsets and line/column locations.

`CompileFiles(...)` and `CompileProjectFile(...)` continue beyond front-end analysis. They currently
raise `CompilerPipelineNotImplementedException` at the lowering/code-generation boundary after a
successful front end.
