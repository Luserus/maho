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
| Generic parameter (`Transfer` default or `: Move`) | Move in generic body; call site uses concrete arg Copy/Move |
| Generic parameter constrained with `: Copy` | Copy |
| `delegate(...) -> R` (`Std.Delegate`) | Move (with no `Destructor` cleanup); tracks captured/bound referent loans |
| Fixed-size array or slice | Follows element ownership |
| Bare `dyn Trait` | Conservatively Move |
| `dyn Trait&` / `dyn Trait*` | Behavior belongs to the reference/pointer wrapper |

`Transfer`, `Copy<T : this = this> : Transfer`, `Move<T : this = this> : Transfer`, and `Destructor : Move` form the lifecycle hierarchy. Unconstrained generic type parameters default to `Transfer`: generic bodies conservatively treat `Transfer` values as `Move`, while concrete instantiations with `Copy` types skip runtime destruction. Restricting a generic parameter with `where T : Copy` allows copying in the generic body and rejects `Move` or unconstrained `Transfer` arguments (`MH0590`); `where T : Move` accepts only `Move` types. Direct bases and C3-linearized bases are checked, so inherited lifecycle behavior is effective. `Destructor` strictly requires a `mut &this` receiver (`MH0560`) and implies `Move` semantics.

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

An alias or safe-reference result supplies its referent at an owned-value destination. Thus `int32 copy = alias;` and `int32 copy = *reference;` both perform an implicit Copy of `int32`; neither needs a source-level dereference binding. This is never an implicit Move: assigning a move-only referent through an alias/reference reports `MH1005`, preserving the access path's promise to continue referring to valid storage.

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

`Resolve` visits every non-intrinsic function/method with a body, plus resolved lambdas recorded in `ResolvedTree.ResolvedLambdas`. `AnalyzeBody` / `AnalyzeLambdaBody` run:

```text
resolved body -> OwnershipLowerer -> MoveAnalysis -> BorrowAnalysis
              -> BorrowEscapeAnalysis -> DropElaborator -> OwnershipFacts
```

Properties are deferred because their synthetic receiver/value storage roots are not modeled yet; guessing those roots would make place identity unsound.

### `OwnershipLowerer`

- `Lower` creates entry/exit blocks, opens the function scope, and lowers the body.
- `RegisterParameters` makes every parameter a storage root and retains the receiver root for `this` lookup.
- `LowerDeclaration` emits local storage effects and recognizes safe alias/reference declarations as named loans rather than value moves, while recording `OwnershipBody.ReferentMap` provenance for delegates and borrow-holding composites (`Tuple`, `ObjectInitializer`, `ConstructorCall`).
- `LowerValue` dispatches place uses, assignments, calls, value-returning `put`/`new` creation expressions (tracking `IsReturnUse` on return leaves and Phase 2 construction on `AllocDeref` targets), `for` loops (shared collection borrow across loop iteration), lambdas/bound instance method delegates, address-of, aggregates, and nested CFG expressions.
- `LowerValueForDestination` applies destination-boundary ownership: owned values Copy or Move normally (consulting the caller's concrete argument type when passing into an unsubstituted generic parameter), while an alias/reference result is always materialized as a Copy of its referent and diagnoses a move-only referent with `MH1005`.
- `LowerCall` consults resolved parameter types: a safe-reference/alias parameter creates an ephemeral borrow; value and composite delegate/borrow arguments keep ephemeral loans alive through the call statement.
- `TryGetPlace` recognizes names, fields, indices, and safe dereferences. Raw dereferences stop there deliberately.
- `LowerIf`, `LowerWhile`, `LowerFor`, and `LowerTry` build CFG edges (including catch scopes and `throw` terminators). `BeginScope`, `EndScope`, and `ExitAllScopes` add storage-death points including normal-return and throw cleanup points.

The lowerer only emits the local, type-directed `MH1005` materialization error; its structural ownership model remains usable after earlier type errors so later analyses can continue from a coherent storage model without altering `ResolvedTree`.

### `OwnershipTypeClassifier`

`Classify` answers Copy or Move and caches by `TypeRef` (using `Transfer`/`Move`/`Copy` constraints and generic parameter bounds). `RequiresDrop` is separate because Move does not imply Destructor (`Delegate` classifies as `Move` with `RequiresDrop => false`). Both use visitation sets to prevent recursive structural types from recursing forever. `HasLifecycleOperator` reads intrinsic lifecycle interfaces, then checks direct and C3-linearized bases. `TryGetAllocDerefInfo` and `IsAllocationOperatorMethod` identify `[OperatorFunction("New")]`/`[OperatorFunction("Put")]` bodies and any fields directly returned by `AllocDeref<mut &T>`.

### `MoveAnalysis`

This is a forward fixed point. A block's input is the join of visited predecessor outputs (unvisited loop back-edges are skipped on join so pre-loop initializations are preserved), and replaying the block computes its output:

```text
StorageLive / StorageDead / Drop  => Uninitialized
Assign                            => Initialized
Use(..., Move)                    => Moved
Use(..., Copy or Read)            => unchanged
```

When control-flow paths disagree, the join records `MaybeInitialized`. It represents a conditional move and lets later code generation allocate a hidden drop flag only when needed. A later use of a moved or conditionally moved root reports `MH1001`, and uninitialized reads report `MH1002`.

Whole-place definite-initialization reporting and partial moves of non-lifecycle types still advance through projection tracking. Uninitialized or partially initialized returns are rejected (`MH1000`/`MH1002`) everywhere except inside `[OperatorFunction("New")]`/`[OperatorFunction("Put")]` allocation operators when returning the allocation target type `T` itself or an `AllocDeref` wrapper whose non-`AllocDeref` fields are all initialized.

### `BorrowAnalysis`

Borrow checking uses two fixed points:

1. `ComputeLoanLiveness` runs backward over alias/reference/delegate/composite loan holders. `Use(holder)` makes it live; `Assign(holder)` and `StorageDead(holder)` kill it. The recorded live-after set is the non-lexical loan boundary.
2. The forward loan analysis joins active loans, begins named loans at `Borrow`, checks overlap, clears prior holder loans on whole-root `Assign`, verifies at `StorageDead` that no active loan references dead local storage (`MH1004`), and drops loans whose holder is no longer live.

Thus a loan ends at its final use rather than its closing brace:

```maho
&Resource view = value;
view;           // final use
Consume(value); // accepted
```

Reversing the last two statements produces `MH1003`, because the holder is live across the move. An unowned borrow used to pass a call argument ends at that call statement.

### `BorrowEscapeAnalysis`

This pass indexes named and temporary loan origins (`OwnershipBody.ReferentMap` and `temporaryLoans`) and checks `return` (when the return type contains a safe reference, alias, delegate, optional, tuple, or non-Drop struct holding borrows) and `throw` terminators. Referents pointing to automatic local storage produce `MH1004`, while escaping temporaries produce `MH1008`. Borrowing a parameter is retained for future signature/lifetime constraints.

### `DropElaborator`

After move facts are known, this scan inserts `Drop` immediately before whole-place `Assign` or `StorageDead` if the type needs destruction and may be initialized. A `MaybeInitialized` state marks the drop `conditional`. This is an IR promise: backend lowering must eventually allocate and maintain the hidden stack flag on all cleanup paths.

## Diagnostics available now

| Code | Meaning |
| --- | --- |
| `MH1001` | Used after a definite or conditional move. |
| `MH1002` | Used while definitely or possibly uninitialized. |
| `MH1003` | Move, write, or incompatible borrow overlaps a live safe loan. |
| `MH1004` | Borrow or delegate capture escapes via `return`/`throw` or outlives its local storage scope. |
| `MH1005` | An owned-value assignment attempted to implicitly copy a move-only referent through a safe alias/reference. |
| `MH1006` | Attempted partial move out of a type implementing custom `Move` or `Destructor`. |
| `MH1007` | Attempted whole move of a partially moved value. |
| `MH1008` | Safe borrow of a temporary value would outlive its statement or binding. |

## Deliberate next work

1. Field-disjoint overlap and projection-aware partial move tracking.
2. Declared lifetime parameters (`'a`) for interprocedural return and field-store borrow relationships.
3. Backend materialization of conditional drop flags and unwind cleanup generation.
4. Explicit FFI/raw-pointer contracts and owning value-form `dyn` vtable rules.
