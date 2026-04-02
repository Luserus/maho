# Resolution Guide

`src/Maho/Resolution` is the placeholder boundary for future semantic analysis and name/type resolution.

Right now this folder is intentionally light in code, but it still deserves deeper documentation because it defines a major architectural boundary: syntax is not meant to silently grow semantic behavior forever.

In other words, `Resolution` is where "parsed source" is expected to become "understood program".

## Files in this folder

- `Resolver.cs`: future semantic driver.
- `Scope.cs`: current shell for nested scope tracking.

## Why this folder matters before it is implemented

Even in its current skeletal state, this folder answers a useful design question: where should the next layer of compiler intelligence go?

The answer is not:

- inside syntax node types,
- inside the CLI,
- inside diagnostics rendering,
- or scattered across parser recovery code.

The answer is here, alongside symbols and downstream semantic diagnostics.

That separation matters because syntax and semantics evolve at different speeds. Syntax cares about source form; resolution will care about meaning, lookup, accessibility, and well-typed relationships.

## Current state

### `Resolver`

`Resolver` exists as an explicit subsystem marker, but it does not implement behavior yet.

That is valuable in itself: the repository already distinguishes syntax construction from later semantic passes, even if the semantic layer is still under construction.

When this type starts filling out, it will likely become the pass coordinator that:

- walks syntax trees,
- opens and closes scopes,
- constructs semantic symbols,
- performs name lookup,
- and emits semantic diagnostics.

### `Scope`

`Scope` currently stores only a `parent` reference supplied through the constructor.

That means the intended shape is already visible:

- scopes are nested,
- child scopes can walk outward,
- symbol lookup tables and scope-specific state have not been added yet.

That makes `Scope` the first concrete hint about intended lookup direction: inner scopes should be able to fail locally and then consult enclosing scopes without the syntax tree itself owning that logic.

## Relationship to neighboring folders

- `Syntax` tells you what appeared in source.
- `Symbols` tells you the semantic entities the compiler expects to model.
- `Resolution` is the layer that should connect those two worlds.
- `Diagnostics` will likely receive additional semantic error production through this stage later.

So if you think of the front-end as stages, `Resolution` is the missing bridge between parse-time structure and semantic understanding.

## What will probably land here later

The code is not there yet, but the folder is the natural home for:

- lexical and nested scope construction,
- symbol declaration/registration passes,
- type/name lookup,
- duplicate definition checks,
- unresolved identifier diagnostics,
- and eventually type-directed semantic validation.

That is documentation of intent, not a claim that those passes already exist.

## How to use this folder today

- Treat it as a roadmap boundary, not an implementation hotspot.
- If you are adding symbols, bindings, or semantic diagnostics later, this is the folder that should start absorbing that work.
- If you are just tracing today's runtime behavior, you can usually skip this folder.

## Reading order once this grows

When semantic work starts landing, the likely order to read will be:

1. `Scope.cs`
2. symbol declarations in `Symbols`
3. `Resolver.cs`
4. semantic diagnostics paths

That order reflects the probable dependency direction: lookup/state first, pass orchestration second.

## Traversal tip

Read [`../Symbols/docs/README.md`](../../Symbols/docs/README.md) before implementing anything substantial here. Resolution is where symbol abstractions will eventually become live semantic state.
