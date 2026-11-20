# Lifetime and ownership analysis

Maho separates source resolution from ownership analysis.

- `ResolvedTree` binds source syntax to symbols, members, and operators.
- `OwnershipBody` is a per-callable control-flow graph (CFG) of storage effects.
- `OwnershipFacts` records fixed-point move/initialization state per block.

The existing AST CFG still handles reachability and return-path diagnostics. The ownership CFG can contain compiler-only effects such as `StorageLive`, `StorageDead`, and `Drop`, so it is suitable as the foundation for cleanup lowering and later code generation.

## Running the examples

[`Samples/Lifetime`](../Samples/Lifetime) contains standalone projects. The valid example must succeed:

```bash
bin/miryo/miryo build --check Samples/Lifetime/valid
```

Each project in `invalid` must fail with the diagnostic written at its top:

```bash
bin/miryo/miryo build --check Samples/Lifetime/invalid/move-after-move.mhpr
bin/miryo/miryo build --check Samples/Lifetime/invalid/borrow-conflict.mhpr
bin/miryo/miryo build --check Samples/Lifetime/invalid/return-local-borrow.mhpr
```

They use a minimal local prelude. `Samples/Std.Core.mhpr` explicitly selects only `Std.Core.mh`, so intentionally failing examples are never included in the ordinary Samples build.

## Semantics

### Implicit Copy and Move

At every value use the compiler selects Copy or Move; source code does not write `move(x)`.

| Type shape | Behavior |
| --- | --- |
| Primitive or aggregate whose fields are all Copy | Copy |
| Implements `Move` or `Destructor`, or contains such a field | Move |
| Explicitly implements `Copy` | Copy |
| Safe alias, safe reference, raw pointer, function value | Copy the handle, never its referent |
| Generic parameter, array, delegate, bare `dyn` | Conservatively Move |
| `dyn Trait&` / `dyn Trait*` | Behavior belongs to the reference/pointer wrapper |

`Copy`, `Move`, and `Destructor` are lifecycle interfaces. Direct bases and C3-linearized bases are checked, so inherited lifecycle behavior is effective. `Destructor` implies Move semantics, but a move-only value need not be destructed.

`dyn Trait&` is a safe reference whose eventual representation is a fat pointer (data/address plus dispatch metadata). Lifetime analysis tracks its referent like any other safe reference, independent of representation. `dyn Trait*` is an unsafe raw handle. Bare `dyn Trait` is conservatively owning and move-only pending final layout/value-existential rules.

### Places, aliases, and escape hatches

A `Place` is storage, not syntax:

```text
root              local or parameter symbol
root.field        field projection
root[index]       index projection
*root             safe-reference dereference projection
```

Roots use `Symbol` identity rather than `SymbolHandle`, because local handles are only unique inside their callable. Overlap is deliberately conservative today: places sharing a root may overlap. That can reject a disjoint-field program, but never accepts an unsafe conflict.

Safe aliases and references create loans. Shared loans coexist; a mutable loan conflicts with every overlapping loan. Any live safe loan prevents moving or assigning its referent. For example, `&Resource view = value;` lowers to an initialized alias handle plus `Borrow(value, owner: view)`. Reborrows keep original provenance, so `&Resource second = first;` loans the original `value`, not the stack storage of `first`.

`this` is the receiver parameter's storage alias, never an implicit pointer, so `this.field` remains normal member syntax. A type member with no first `this` receiver parameter is implicitly static; spelling `static` is optional documentation. The pre-existing receiverless `Callable` operator spelling remains a compatibility exception until its standard-library declarations migrate to explicit `&this`. `T*` is the explicit unsafe escape hatch: it is Copy as a handle, but dereferencing it is intentionally outside safe borrow tracking.

An alias or safe-reference result supplies its referent at an owned-value destination. Thus `int32 copy = alias;` and `int32 copy = *reference;` both perform an implicit Copy of `int32`; neither needs a source-level dereference binding. This is never an implicit Move: assigning a move-only referent through an alias/reference reports `MH0404`, preserving the access path's promise to continue referring to valid storage.

## Ownership IR

Each `OwnershipBlock` has ordered statements, predecessor/successor lists, and a fallthrough, branch, conditional branch, return, or unreachable terminator.

| Operation | Meaning |
| --- | --- |
| `StorageLive(place)` | Enters scope, initially uninitialized. |
| `Assign(place)` | Initializes/replaces a whole place. |
| `Use(place, Copy/Move/Read)` | Evaluates a place; Move consumes it. |
| `Borrow(place, Shared/Mutable, owner?)` | Starts a safe loan; no owner means an ephemeral call loan. |
| `Drop(place, conditional)` | Cleanup before replacement/death; conditional requests a future hidden flag. |
| `StorageDead(place)` | Leaves scope. |

Parameters lower to `StorageLive` then `Assign` because callers supply initialized arguments. A safe-alias declaration assigns its handle before emitting `Borrow`; that makes the loan start after the declaration, allowing precise non-lexical liveness.

## Function-by-function walkthrough

### `LifetimeAnalysisPass`

`Resolve` visits every non-intrinsic function/method with a body. `AnalyzeBody` runs:

```text
resolved body -> OwnershipLowerer -> MoveAnalysis -> BorrowAnalysis
              -> BorrowEscapeAnalysis -> DropElaborator -> OwnershipFacts
```

Properties are deferred because their synthetic receiver/value storage roots are not modeled yet; guessing those roots would make place identity unsound.

### `OwnershipLowerer`

- `Lower` creates entry/exit blocks, opens the function scope, and lowers the body.
- `RegisterParameters` makes every parameter a storage root and retains the receiver root for `this` lookup.
- `LowerDeclaration` emits local storage effects and recognizes safe alias/reference declarations as named loans rather than value moves.
- `LowerValue` dispatches place uses, assignments, calls, address-of, aggregates, and nested CFG expressions.
- `LowerValueForDestination` applies destination-boundary ownership: owned values Copy or Move normally, while an alias/reference result is always materialized as a Copy of its referent and diagnoses a move-only referent with `MH0404`.
- `LowerCall` consults resolved parameter types: a safe-reference/alias parameter creates an ephemeral borrow; a value parameter uses implicit Copy/Move.
- `TryGetPlace` recognizes names, fields, indices, and safe dereferences. Raw dereferences stop there deliberately.
- `LowerIf` and `LowerWhile` build CFG edges. `BeginScope`, `EndScope`, and `ExitAllScopes` add storage-death points including normal-return cleanup points.

The lowerer only emits the local, type-directed `MH0404` materialization error; its structural ownership model remains usable after earlier type errors so later analyses can continue from a coherent storage model without altering `ResolvedTree`.

### `OwnershipTypeClassifier`

`Classify` answers Copy or Move and caches by `TypeRef`. `RequiresDrop` is separate because Move does not imply Destructor. Both use visitation sets to prevent recursive structural types from recursing forever. `HasLifecycleOperator` reads intrinsic lifecycle interfaces, then checks direct and C3-linearized bases.

### `MoveAnalysis`

This is a forward fixed point. A block's input is the join of predecessor outputs, and replaying the block computes its output:

```text
StorageLive / StorageDead / Drop  => Uninitialized
Assign                            => Initialized
Use(..., Move)                    => Moved
Use(..., Copy or Read)            => unchanged
```

When control-flow paths disagree, the join records `MaybeInitialized`. It represents a conditional move and lets later code generation allocate a hidden drop flag only when needed. A later use of a moved or conditionally moved root reports `MH0400`.

Whole-place definite-initialization reporting and partial moves are deferred. A write to `value.field` cannot yet be mistaken for initialization of whole `value`; the future projection-aware pass will activate `MH0401` correctly.

### `BorrowAnalysis`

Borrow checking uses two fixed points:

1. `ComputeLoanLiveness` runs backward over alias/reference holders. `Use(holder)` makes it live; `Assign(holder)` and `StorageDead(holder)` kill it. The recorded live-after set is the non-lexical loan boundary.
2. The forward loan analysis joins active loans, begins named loans at `Borrow`, checks overlap, and drops loans whose holder is no longer live.

Thus a loan ends at its final use rather than its closing brace:

```maho
&Resource view = value;
view;           // final use
Consume(value); // accepted
```

Reversing the last two statements produces `MH0402`, because the holder is live across the move. An unowned borrow used to pass a call argument ends at that call statement.

### `BorrowEscapeAnalysis`

For a safe-reference/alias return type, this pass indexes named and temporary loan origins and inspects returns. A referent that is a local variable produces `MH0403`. Borrowing a parameter is accepted for now; declared lifetimes and interprocedural constraints will validate those relations.

### `DropElaborator`

After move facts are known, this scan inserts `Drop` immediately before whole-place `Assign` or `StorageDead` if the type needs destruction and may be initialized. A `MaybeInitialized` state marks the drop `conditional`. This is an IR promise: backend lowering must eventually allocate and maintain the hidden stack flag on all cleanup paths.

## Diagnostics available now

| Code | Meaning |
| --- | --- |
| `MH0400` | Used after a definite or conditional move. |
| `MH0402` | Move, write, or incompatible borrow overlaps a live safe loan. |
| `MH0403` | Returned safe reference/alias directly targets local storage. |
| `MH0404` | An owned-value assignment attempted to implicitly copy a move-only referent through a safe alias/reference. |

`MH0401` is reserved for projection-aware definite-initialization reporting and is not emitted yet.

## Deliberate next work

1. Projection-aware initialization, field-disjoint overlap, and partial moves without Destructor.
2. Declared lifetime parameters plus return, field-store, closure-capture, and interprocedural escape constraints.
3. Backend materialization of conditional drop flags and cleanup on `goto`, unwind, and future `try` edges.
4. Explicit FFI/raw-pointer contracts and owning value-form `dyn` vtable rules.
