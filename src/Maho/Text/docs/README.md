# Text System Guide

The `Text` folder is the source-loading and coordinate-calculation layer used by the rest of the compiler.

If line/column locations, spans, or file-loading behavior look wrong, start here before blaming diagnostics or rendering code.

## Files in this folder

- `SourceFile.cs`: lightweight source identity plus load policy.
- `SourceTextLoadMode.cs`: eager vs lazy-cached loading mode.
- `SourceText.cs`: decoded text access, line parsing, and lookup.
- `TextLine.cs`: one parsed line of source.
- `TextSpan.cs`: half-open text span with line/column helpers.

## Type guide

### `SourceFile`

Tiny value object that carries:

- `FilePath`
- `LoadMode`

This lets callers choose file identity and load strategy without overloading the `SourceText` constructor signature.

### `SourceTextLoadMode`

Current options:

- `Eager`: decode immediately.
- `LazyCached`: keep the memory-mapped file around and decode on first real text access.

### `SourceText`

This is the core text abstraction used by syntax and diagnostics code.

Important fields and properties:

- `cachedText`: decoded string cache
- `mmf` and `accessor`: backing storage for file-based sources
- `fileLength`: raw byte count
- `Lines`: lazily parsed line table
- `Length` and indexer: character-level access

## `SourceText` function guide

### `SourceText(SourceFile sourceFile)`

Loads a file-backed source. The interesting part is the split behavior:

- empty files immediately become an empty cached string,
- eager mode decodes right away and disposes the memory map,
- lazy-cached mode keeps the map/accessor open until text is actually needed.

### `SourceText(string text)`

In-memory constructor. Always eager because the text already exists as a managed string.

### `MatchesAt(int position, ReadOnlySpan<char> value)`

A small but useful performance helper: compare characters in place without allocating a substring.

### `ToString()` and `ToString(TextSpan span)`

Expose the whole text or a span slice.

### `EnsureText()`

Internal lazy gate for decoding file-backed text. Nearly every read path funnels through here.

### `DecodeFromAccessor()`

Reads the full byte range from the memory-mapped accessor and decodes it as UTF-8.

### `ParseLines()`, `AddLine(...)`, `GetLineBreakWidth(...)`

Build the line table used by diagnostics and span helpers.

Worth noting:

- final lines are added even without a trailing newline,
- CRLF, CR, and LF are all handled explicitly.

### `GetLineIndex(int position)`

Uses binary search over the parsed line table. This is one of the core utilities behind line/column projection throughout the repository.

### `Dispose()`

Releases the view accessor and memory-mapped file when file-backed storage was used.

## `TextLine`

Represents one parsed line and keeps:

- the owning `SourceText`,
- start offset,
- logical length,
- length including the line break.

Useful computed members:

- `End`
- `Span`
- `SpanIncludingLineBreak`

### `TextLine.ToString()`

Returns the text content of the line via the owning `SourceText`.

## `TextSpan`

Half-open span with:

- `Start`
- `Length`
- `End`

### `FromBounds(int start, int end)`

Convenience creator used when only endpoints are known.

### `GetStartLine(...)`, `GetStartColumn(...)`, `GetEndLine(...)`, `GetEndColumn(...)`

These methods convert raw offsets into line/column coordinates using `SourceText.GetLineIndex(...)` and the parsed line table.

## What is worth paying attention to here

- `SourceText` uses memory mapping, but line parsing still works on decoded text. If you are optimizing, keep both layers in mind.
- Location math depends on `TextSpan` plus `SourceText`; diagnostics and renderers are downstream consumers.
- The CLI renderer has its own lightweight `SourceBuffer` type. That is intentional duplication for rendering from serialized results, not a replacement for this folder.

## Reading order

1. `TextSpan.cs`
2. `TextLine.cs`
3. `SourceFile.cs`
4. `SourceTextLoadMode.cs`
5. `SourceText.cs`
