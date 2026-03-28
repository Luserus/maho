# CLI JSON And Output Pipeline

This file explains the part of the CLI that is easiest to forget later:

- how `System.Text.Json` is being used in this repository,
- what JSON shapes the CLI reads and writes,
- how those JSON objects become terminal text,
- and when the final result is printed versus written to a file.

If you already understand the higher-level CLI flow, pair this guide with [`README.md`](README.md).

## The short version

The core library gives the CLI two kinds of data:

- structured .NET objects such as `CompilerAnalysisResult` and `DiagnosticInfo[]`
- JSON strings such as `LexerJson`, `ParserJson`, and `DiagnosticsJson`

The CLI then does one of two things:

1. keep data as JSON and wrap it into a larger JSON document for file output
2. deserialize JSON back into DTOs and turn those DTOs into readable text for `stdout`

So the CLI is both:

- a JSON packager
- and a JSON consumer/renderer

## The `System.Text.Json` pieces used here

This project uses a small, focused part of the .NET JSON stack.

### `JsonSerializer.Serialize(...)`

Used when the code already has a normal .NET object and wants a JSON string.

Where that happens:

- core library debug serialization in `DebugJson.Serialize(...)`
- CLI envelope emission through `ToJsonString(...)` on `JsonObject`

Conceptually:

- input: normal .NET object
- output: JSON text

### `JsonSerializer.Deserialize<T>(...)`

Used when the CLI has JSON text and wants a normal .NET object again.

In this repository, the most important call is:

- `DeserializeJson<T>(...)` inside `SerializedAnalysisRenderer`

Examples:

- `DeserializeJson<SerializedLexerInfo>(lexerJson)`
- `DeserializeJson<SerializedParserInfo>(parserJson)`
- `DeserializeJson<DiagnosticInfo[]>(analysis.DiagnosticsJson)`

Conceptually:

- input: JSON text
- output: a typed .NET object graph

This is the core "JSON deserialization" step in the CLI.

### `JsonNode.Parse(...)`

Used when the CLI wants to keep incoming JSON as JSON instead of converting it into custom DTOs.

This happens in `CommandLine.BuildDebugOutput(...)` and `CommandLine.BuildDiagnosticsJsonOutput(...)`.

Why use it there?

- the CLI is not trying to interpret lexer/parser payloads in those methods
- it only wants to insert those payloads into a larger JSON document

So instead of:

- deserialize JSON into DTOs
- reserialize those DTOs again

the CLI does:

- parse JSON into a generic JSON node tree
- attach that node tree to a new `JsonObject`

### `JsonObject` and `JsonArray`

Used when the CLI is constructing a new JSON document itself.

Examples:

- one `JsonObject` for each file
- one `JsonArray` for the list of files
- one top-level `JsonObject` containing `inputPath` and `files`

You can think of them as mutable JSON containers:

- `JsonObject` = JSON object / dictionary
- `JsonArray` = JSON array / list

### `ToJsonString(...)`

Used at the end of JSON construction to turn a `JsonObject` or `JsonArray` back into text.

That is the final step for CLI-produced JSON file content.

## JSON options in this project

There are two separate serializer configurations worth remembering.

### Core library serializer

In `src/Maho/Analysis/DebugJson.cs`, the core library uses serializer options that:

- write camelCase property names
- indent output
- omit null properties

This is why optional fields like matching-keyword data may simply disappear from the JSON instead of showing up as `"matchingKind": null`.

### CLI serializer

In `src/Maho.Cli/CommandLine.cs`, the CLI uses its own `JsonOptions` when it emits the JSON envelopes it builds itself.

Those options:

- write camelCase property names
- indent output

This matters because the CLI is producing wrapper documents such as:

- top-level debug-output documents
- top-level diagnostics-output documents

## The JSON shapes the CLI reads

There are three main incoming JSON shapes.

## 1. Lexer debug JSON

Source:

- `CompilerAnalysisResult.LexerJson`

Produced by:

- `Lexer.ToJson()`
- `DebugJson.Serialize(...)`

Consumed by:

- `SerializedAnalysisRenderer.RenderDebugOutput(...)`

Deserialized into:

- `SerializedLexerInfo`
- `SerializedLexerTokenInfo`
- `SerializedSyntaxTriviaInfo`
- `SerializedTextSpanInfo`

### What the shape means

At the top level, the lexer payload is basically:

- `kind`
- `tokenCount`
- `tokens`

Each token object carries:

- `index`
- `kind`
- `text`
- `displayText`
- `matchingKind`
- `span`
- `leadingTrivia`
- `trailingTrivia`

This is the data that eventually feeds the "Token Stream" text output.

## 2. Parser debug JSON

Source:

- `CompilerAnalysisResult.ParserJson`

Produced by:

- `Parser.ToJson()`
- `DebugJson.Serialize(...)`

Consumed by:

- `SerializedAnalysisRenderer.RenderDebugOutput(...)`

Deserialized into:

- `SerializedParserInfo`
- `SerializedParserNodeInfo`
- `SerializedParserChildInfo`
- trivia/span DTOs reused from above

### What the shape means

At the top level, the parser payload is basically:

- `kind`
- `root`

The root node and each child node carry:

- `nodeType`
- `span`
- token-specific fields when the node is really a token:
  `tokenKind`, `text`, `displayText`, `matchingKind`
- optional trivia arrays on token nodes
- `children`

Each child entry also includes `propertyName`, which is what lets the CLI render lines like:

- `Members[0] -> ...`
- `EndToken -> ...`

That is why the parser tree renderer can show structural names instead of only raw nesting.

## 3. Diagnostics JSON

Source:

- `CompilerAnalysisResult.DiagnosticsJson`

Produced by:

- `DebugJson.Serialize(diagnostics)`

Consumed by:

- `SerializedAnalysisRenderer.RenderDiagnosticsOutput(...)`
- `CommandLine.BuildDebugOutput(...)`
- `CommandLine.BuildDiagnosticsJsonOutput(...)`

Two important details:

- text diagnostics rendering deserializes this into `DiagnosticInfo[]`
- JSON envelope building keeps it as JSON via `JsonNode.Parse(...)`

So diagnostics are both:

- a typed rendering input
- and a JSON subtree copied into larger JSON documents

## Why the CLI has its own DTO records

Inside `SerializedAnalysisRenderer.cs`, you will see renderer-local records such as:

- `SerializedLexerInfo`
- `SerializedParserInfo`
- `SerializedTextSpanInfo`

These are not the same types as the serializer-side records in `DebugJson.cs`.

That duplication is intentional.

The CLI is saying:

- "I know the JSON contract"
- not "I need the original compiler objects"

That keeps the boundary cleaner because the CLI only depends on:

- the serialized shape
- not the internal parser/lexer implementation types

## How deserialization actually works here

The key helper is `DeserializeJson<T>(string json)` in `SerializedAnalysisRenderer`.

Its job is:

1. call `JsonSerializer.Deserialize<T>(json, JsonOptions)`
2. check whether the result came back as `null`
3. throw if the JSON no longer matches the expected shape

That means deserialization is strict in an important way:

- if the payload cannot be turned back into the expected typed object, rendering fails fast

Then `RenderDebugOutput(...)` catches that and downgrades it into an internal-failure block instead of crashing the entire CLI process.

## How text output happens after deserialization

This is the part that is easiest to forget if you do not use JSON libraries often.

The CLI does not print raw deserialized objects directly.

The path is:

1. deserialize JSON text into DTOs
2. walk those DTOs
3. build a formatted string with `StringBuilder` or `StringWriter`
4. return the final string
5. `CommandLine.Run(...)` writes that string to `stdout`

## Debug text output path

For lexer/parser debug views:

1. `CommandLine.Run(...)` calls `SerializedAnalysisRenderer.RenderDebugOutput(...)`
2. `RenderDebugOutput(...)` deserializes `LexerJson` and/or `ParserJson`
3. `RenderLexerOutput(...)` and `RenderParserOutput(...)` build text
4. the final combined string is returned
5. `CommandLine.Run(...)` calls `Console.Out.Write(debugOutput)`

So the real printing happens in `CommandLine`, not inside the renderer.

The renderer's responsibility is:

- turning JSON into text

The command line's responsibility is:

- deciding where that text goes

## Diagnostics text output path

For text diagnostics:

1. `CommandLine.Run(...)` chooses `BuildDiagnosticsTextOutput(...)`
2. that calls `SerializedAnalysisRenderer.RenderDiagnosticsOutput(...)` per file
3. `RenderDiagnosticsOutput(...)` deserializes `DiagnosticInfo[]`
4. it reloads the source file
5. it formats summaries, source lines, carets, and tips into a string
6. the aggregated diagnostics string returns to `Run(...)`
7. `Run(...)` calls `Console.Out.Write(diagnosticsOutput)` when diagnostics are going to stdout

Again, deserialization happens inside the renderer, but final printing happens in `CommandLine`.

## How file output happens

There are two different file-output stories.

## 1. Writing JSON to a file

This happens when the CLI is asked for JSON output.

### Debug JSON file path

1. `CommandLine.Run(...)` calls `BuildDebugOutput(...)`
2. that creates a new JSON envelope using `JsonObject` and `JsonArray`
3. existing lexer/parser/diagnostics JSON is inserted with `JsonNode.Parse(...)`
4. the final `JsonObject` becomes text via `ToJsonString(JsonOptions)`
5. `TryWriteOutputFile(...)` writes it using `File.WriteAllText(...)`

Important:

- this path usually does not deserialize lexer/parser JSON
- it mostly repackages it

### Diagnostics JSON file path

1. `CommandLine.Run(...)` calls `BuildDiagnosticsJsonOutput(...)`
2. that builds a new envelope with per-file entries
3. successful diagnostics JSON is inserted with `JsonNode.Parse(...)`
4. the top-level object is serialized with `ToJsonString(...)`
5. `TryWriteOutputFile(...)` writes the text to disk

## 2. Writing text to a file

The CLI also supports writing diagnostics output to a file, and that file may contain text rather than JSON.

Path:

1. `BuildDiagnosticsTextOutput(...)` creates the text string
2. `Run(...)` passes that text to `TryWriteOutputFile(...)`
3. `TryWriteOutputFile(...)` writes the string with `File.WriteAllText(...)`

So `TryWriteOutputFile(...)` does not care whether the content is:

- JSON text
- or human-readable text

It just writes the final string it was given.

## When output goes to stdout vs stderr vs files

### `stdout`

Used for:

- human-readable debug output
- human-readable diagnostics output
- JSON diagnostics output when explicitly selected and not conflicting with debug stdout

The important `stdout` calls are:

- `Console.Out.Write(debugOutput)`
- `Console.Out.Write(diagnosticsOutput)`

### `stderr`

Used for:

- progress updates
- usage errors
- completion status messages such as "Stored JSON output at ..."

This is why the CLI can still keep primary output clean on `stdout`.

### files

Used when:

- `--output` is provided
- `--diagnostics-output` is provided

The important write path is always:

- `TryWriteOutputFile(...)`
- `File.WriteAllText(...)`

## A practical way to remember the whole thing

If you forget everything else, remember this split:

- `JsonNode.Parse(...)` means "keep this as JSON and insert it into a larger JSON document"
- `JsonSerializer.Deserialize<T>(...)` means "turn this JSON back into typed objects so we can render text"
- `Console.Out.Write(...)` means "print the final text/string now"
- `TryWriteOutputFile(...)` means "persist the final text/string to disk"

That mental model is enough to re-derive most of the CLI's JSON behavior later.
