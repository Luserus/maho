# CLI

`Maho.Cli` is a compiler driver. It accepts a source file, directory, or `.mhpr` project file and
asks `MahoCompiler` to compile it. A successful front end currently reaches the intentional
lowering/code-generation placeholder and exits with an error until that stage is implemented.

## Output

Syntax trees are inspection data, not normal compiler output. Request JSON debug payloads explicitly:

```bash
maho --debug --lex --output - Program.mh
maho --debug --parse --output parser.json Program.mh
maho --debug --lex --parse --output debug.json Test.mhpr
```

`-` directs debug output to `stdout`.

Diagnostics are text on `stderr` by default. To choose a format or write a report, use:

```bash
maho --diagnostics --json --output - Test.mhpr
maho --diagnostics --text --output diagnostics.txt Test.mhpr
```

For diagnostics, `-` directs output to `stderr`. This permits debug JSON on `stdout` and JSON
diagnostics on `stderr` during the same run.
