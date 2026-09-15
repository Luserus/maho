# Maho Language Theory: Current Front End

This note specifies the source language accepted by the current Maho front end and the semantic facts it resolves today. It describes the implementation as it exists now, rather than a proposal for the eventual language.

The front end has three relevant stages:

1. The lexer turns source characters into tokens and preserves whitespace as trivia.
2. The parser produces a source-oriented syntax tree, recovering with diagnostics where it can.
3. Resolution discovers symbols and records unambiguous references in `ResolvedTree`.

Later stages such as compiler-recognized primitive types, generic-value evaluation, type checking, overload selection, and code generation are intentionally outside this document's guaranteed semantics.

## Notation

The grammar uses an EBNF-style notation:

```text
A ::= B C        A consists of B followed by C
A | B            alternative forms
[ A ]            optional A
{ A }            zero or more A
A { "," A } [","]
                 a comma-separated list with an optional trailing comma
"token"          an exact source spelling
identifier        a lexical token class
```

`ε` denotes the empty sequence. Productions are descriptive: the parser also has error recovery, and a recovered tree can contain missing tokens or expressions that the ideal grammar does not show.

## 1. Lexical grammar

### 1.1 Trivia

Whitespace is not discarded. It is attached to the neighbouring tokens as leading or trailing trivia.

```text
space       ::= " " { " " }
tab         ::= "\t" { "\t" }
line-break  ::= "\n"
trivia      ::= space | tab | line-break
```

There is currently no comment lexical production. A slash is tokenized as `/`, not as the beginning of a line or block comment.

### 1.2 Identifiers and literals

The regular-expression notation below is an implementation-level approximation of the .NET character predicates used by the lexer.

```text
identifier  ::= [\p{L}_][\p{L}\p{Nd}_]*
integer     ::= [0-9]+
float       ::= [0-9]+\.[0-9]+ | \.[0-9]+
char        ::= ' ( escaped-char | non-quote-non-newline )* '
string      ::= " ( escaped-char | non-quote-non-newline )* "
escaped-char ::= "\\" any-non-newline-character
```

Notes:

- Identifiers may begin with a Unicode letter or `_`; later characters may also be Unicode digits.
- Integer and float literals currently have no sign, exponent, radix prefix, separator, or suffix syntax. A sign is parsed as an operator.
- Strings and characters cannot cross a line break. Escapes are scanned as two source characters; escape interpretation is deferred.
- A character literal is diagnosed unless it contains exactly one logical character, where an escape sequence counts as one logical character.
- An unrecognized character produces a lexer diagnostic and a `BadToken`; the parser filters bad tokens before parsing.

### 1.3 Contextual words

All words lex as `identifier` tokens. The lexer additionally classifies the following exact spellings, and the parser consults that classification only where the grammar gives a word special meaning.

```text
control        if else while return goto
declarations   using namespace attribute struct class enum union interface
accessors      get set
construction   new put with
modifiers      public private internal protected sealed virtual extern
               static const partial unsafe intrinsic global
generic        where int float
other          for var dyn
```

Consequently, these are contextual keywords rather than a separate lexical token class. For example, `int` is still an identifier token and is special only in a generic-parameter value-kind position.

### 1.4 Punctuation and operators

The lexer emits individual punctuation tokens for the following source characters:

```text
!  "  #  %  &  '  (  )  *  +  ,  -  .  /  :  ;  <  =  >  ?
@  [  \  ]  ^  `  {  }  ~
```

The parser's operator trie recognizes these adjacent token sequences:

```text
==  !=  <<  >>  <<<  <=  >=  &&  ||
+  -  *  /  %  &  <  >  ?  =
```

Only entries in the Pratt precedence table have expression semantics. The accepted expression operators are stated precisely in [section 4](#4-expressions-and-precedence).

## 2. Shared list and name productions

Except for variable declarators, every comma-separated production below permits a trailing comma. The syntax tree preserves every comma through `SeparatedSyntaxList<T>`.

```text
name                    ::= identifier | qualified-name | generic-name
simple-name             ::= identifier
qualified-name          ::= name-part { "." name-part }
name-part               ::= identifier | generic-name

attribute-lists         ::= { attribute-list }
attribute-list          ::= "[" attribute { "," attribute } [","] "]"
attribute               ::= qualified-name [ "(" argument-list ")" ]

modifier                ::= "public" | "private" | "internal" | "protected"
                          | "sealed" | "virtual" | "extern" | "static"
                          | "const" | "partial" | "unsafe" | "global"
                          | "intrinsic"
modifiers               ::= { modifier }
```

The parser records modifier tokens but does not yet validate whether every modifier is legal on every declaration kind. `intrinsic` is recognized as a modifier only when its surrounding tokens make it an attribute-declaration modifier.

## 3. Declarations and types

### 3.1 Compilation units and directives

```text
compilation-unit        ::= { pragma-directive } { top-level } EOF
pragma-directive        ::= "#" "pragma" "toplevel" ( "enable" | "disable" )
```

The parser recognizes a more permissive four-identifier directive shape and diagnoses noncanonical spellings. `#pragma toplevel enable` enables top-level statements for that compilation unit; `disable` turns that mode off again.

### 3.2 Namespaces, aliases, and top-level members

```text
top-level               ::= alias-declaration
                          | namespace-declaration
                          | top-level-declaration
                          | top-level-statement

namespace-declaration   ::= "namespace" qualified-name ( ";" | "{" { top-level } "}" )

alias-declaration       ::= "using" declaration-name { type-constraint-clause }
                          "=" type ";"

top-level-declaration   ::= attribute-lists modifiers
                          ( type-declaration | function-declaration | variable-declaration ";" )
                          | attribute-lists modifiers top-level-block

top-level-block         ::= "{" { top-level } "}"
```

### 3.3 Generic declarations

```text
declaration-name        ::= identifier [ generic-parameter-clause ]
                          | qualified declaration-name

generic-parameter-clause ::= "<" generic-parameter
                             { "," generic-parameter } [","] ">"

generic-parameter       ::= identifier [ "..." ] [ ":" generic-value-kind ]
generic-value-kind      ::= "int" | "float" | "const"

type-constraint-clause  ::= "where" identifier ":" type
                             { "," type } [","]
```

The generic parameter forms mean:

| Form | Current syntax meaning |
| --- | --- |
| `T` | A type-valued generic parameter. |
| `N: int` | A compile-time integer-valued parameter. |
| `F: float` | A compile-time floating-point-valued parameter. |
| `C: const` | A compile-time value of any literal category. |
| `Rest...` | A variadic generic parameter. |

`where N : Std.Int32` is syntactically legal even when `N` is non-type. The current resolver records the reference to `N` and resolves `Std.Int32`; it does not yet interpret the constraint as a compile-time value-type check.

### 3.4 Type syntax and generic application

```text
type                    ::= primary-type { type-modifier } [ "." type ]
primary-type            ::= identifier [ generic-argument-clause ]
type-modifier           ::= array-modifier | "?" | "*" | "&"
array-modifier          ::= "[" [ expression ] "]"

generic-argument-clause ::= "<" generic-argument
                             { "," generic-argument } [","] ">"
generic-argument        ::= type | literal | identifier
literal                 ::= integer | float | char | string
```

The final `identifier` alternative is deliberately represented as a deferred named-expression generic argument, rather than immediately as a type. Once the generic target is known, the resolver treats it as:

- a type reference when the corresponding parameter is type-valued;
- a named expression when the parameter is non-type;
- unresolved/deferred when the target or parameter position is not known.

This permits both `MyType<Int32, 100>` and `MyType<Int32, count>` without prematurely imposing compile-time-value rules.

### 3.5 Types, functions, variables, and properties

```text
type-declaration        ::= type-keyword declaration-name [ type-base-clause ]
                          { type-constraint-clause } type-body
type-keyword            ::= "attribute" | "class" | "struct" | "interface"
                          | "enum" | "union"
type-base-clause        ::= ":" type { "," type } [","]
type-body               ::= ";" | "{" { member } "}"

function-declaration    ::= type declaration-name "(" parameter-list ")"
                          { type-constraint-clause } function-body
parameter-list          ::= [ parameter { "," parameter } [","] ]
parameter               ::= modifiers type declaration-name [ "=" expression ]
function-body           ::= ";" | "{" { local } "}"

variable-declaration    ::= type variable-declarator
                          { "," variable-declarator }
variable-declarator     ::= declaration-name [ "=" expression ]
```

The final comma is intentionally **not** legal in `variable-declaration`; `Int32 a,;` is diagnosed. It is legal in the other separated lists above, for example `void F(Int32 a, Int32 b,)`.

Within a type body, the parser distinguishes fields, functions, nested types, modifier-bearing member blocks, and properties:

```text
member                  ::= attribute-lists modifiers
                          ( type-declaration | function-declaration
                          | variable-declaration ";" | property-declaration
                          | member-block )
member-block            ::= "{" { member } "}"
property-declaration    ::= type declaration-name property-accessor-list
property-accessor-list  ::= "{" { property-accessor } "}"
property-accessor       ::= attribute-lists modifiers ( "get" | "set" ) function-body
```

The same broad declaration shapes can appear locally, where they create a local type, local function, or local variable declaration.

## 4. Expressions and precedence

Expressions are parsed with a Pratt parser. The concrete binding powers, from highest to lowest, are:

| Binding power | Operators | Associativity / role |
| ---: | --- | --- |
| 70 | unary `+`, unary `-`, binary `+`, binary `-` | prefix and infix |
| 60 | `*`, `/`, `%` | infix |
| 40 | `<`, `<=`, `>`, `>=` | infix |
| 35 | `==`, `!=` | infix |
| 25 | `&&` | infix |
| 20 | `||` | infix |
| 9/10 | `=` | assignment node; intended right-associative entry |

Calls, indexing, and member access are parsed as continuations after a primary expression and can chain.

```text
expression              ::= prefix-expression { continuation | infix-operator expression }
prefix-expression       ::= ( "+" | "-" ) expression | primary-expression
continuation            ::= "(" argument-list ")"
                          | "[" expression "]"
                          | "." identifier

primary-expression      ::= literal
                          | named-expression
                          | "(" expression ")"
                          | "(" type ")" expression
                          | if-expression
                          | block-expression
                          | collection-expression
                          | creation-expression

named-expression        ::= identifier [ generic-argument-clause ]
if-expression           ::= "if" "(" expression ")" expression
                          [ "else" expression ]
block-expression        ::= "{" { local } [ expression ] "}"
collection-expression   ::= "[" expression-list "]" [ "with" "(" argument-list ")" ]
creation-expression     ::= ( "new" | "put" ) type "(" argument-list ")" [ object-with-clause ]
                          | ( "new" | "put" ) type array-modifier
                            [ collection-initializer ] [ object-with-clause ]
object-with-clause      ::= "with" collection-initializer
collection-initializer  ::= "{" expression-list "}"
expression-list         ::= [ expression { "," expression } [","] ]
argument-list           ::= [ argument { "," argument } [","] ]
argument                ::= expression | identifier ":" expression
```

The cast-versus-parenthesized-expression ambiguity is preserved in a dedicated syntax node when both readings remain plausible. `[` starts a collection expression in expression context, but a local construct beginning with `[` is first interpreted as an attribute-list declaration; that ambiguity is not yet resolved in favour of a standalone collection-expression statement.

## 5. Statements and placement

```text
top-level-statement     ::= expression ";" | ";" | return-statement
                          | if-statement | while-statement | label | goto-statement
local                   ::= local-declaration | local-statement
local-statement         ::= expression ";" | ";" | return-statement
                          | if-statement | while-statement | label | goto-statement
                          | "{" { local } "}"

return-statement        ::= "return" [ expression ] ";"
if-statement            ::= "if" "(" expression ")" statement [ "else" statement ]
while-statement         ::= "while" "(" expression ")" statement
label                   ::= identifier ":"
goto-statement          ::= "goto" identifier ";"
```

`statement` is the top-level or local form appropriate to its enclosing production. Top-level statements require `#pragma toplevel enable`; the parser still creates nodes without the pragma but reports `MH0011`.

## 6. Current resolution semantics

Resolution runs only after all compilation units form one `SyntaxTree`. It uses two passes.

### 6.1 Symbol discovery pass

The discovery pass creates scopes and symbols before resolving declaration-owned references. It discovers:

- namespaces and qualified namespace paths;
- top-level types, nested types, local types, aliases, functions, methods, properties, fields, globals, parameters, locals, and labels;
- a child scope for every generic owner, type, function, method, accessor body, and local block;
- `GenericParameterSymbol` instances for every declared generic parameter;
- every variable declarator separately, including multiple declarators in one declaration;
- labels before expression resolution, so forward `goto` references can bind.

If a compilation unit opts into top-level statements, discovery creates a synthetic `Main` function and scope. Ordinary top-level variables in that unit become locals of that `Main`. A top-level block with the `global` modifier is handled specially: its variable declarations bypass that synthetic scope and become global variables.

Namespaces and type declarations retain their containing namespace. Functions and methods retain child scopes for parameters and bodies. Local blocks receive nested scopes.

### 6.2 Declaration-resolution pass

The declaration pass records successful, unambiguous references in `ResolvedTree`. It currently resolves:

- attribute names and attribute argument expressions;
- type base clauses, declared types, parameter types, return types, property types, field types, and variable types;
- type-constraint-clause parameter names and constraint types;
- generic-parameter declaration syntax;
- generic target names and generic arguments according to their declared parameter kind;
- aliases and their targets;
- expression names in bodies, initializers, calls, member-access operands, binary/unary expressions, casts, arrays, object creation, conditions, returns, and assignments;
- labels for `goto`, restricted to the same containing function.

A simple or qualified type/name reference is recorded only when exactly one symbol is found in the applicable scope. The resolver does not currently emit an unresolved-name or ambiguity diagnostic when this lookup fails; later semantic passes are expected to supply full diagnostic policy.

### 6.3 Generic arguments and constraints

For a generic type application, the resolver first resolves the target generic symbol. It then uses the target's generic parameter list positionally:

- A type-valued parameter resolves a bare named argument as a type symbol.
- A non-type parameter resolves a bare named argument as an expression name.
- A literal generic argument is retained as syntax for a later compile-time-expression/metaprogramming pass.
- No type-kind compatibility diagnostic is emitted for non-type arguments in this pass.
- Primitive compile-time kinds such as `int` and `float` are syntax classifications, not compiler-recognized library types yet.

An alias target is retained only if its type-valued generic arguments satisfy the target's currently known constraints. Constraint satisfaction follows direct equality, recursively declared generic-parameter constraints, base types, and already-resolved aliases. Non-type generic arguments are deliberately excluded from this compatibility check until compile-time expression evaluation exists.

### 6.4 Rules deliberately deferred

The following are not guaranteed or diagnosed by the present resolver:

- evaluation, substitution, or constness checking of non-type generic arguments;
- mapping `int`, `float`, or literal values to compiler-recognized special/library types;
- generic specialization, variadic expansion, and reflective metaprogramming operations;
- general type compatibility, assignability, conversion, overload resolution, and return-type validation;
- complete modifier applicability and accessibility enforcement;
- full unresolved-name, duplicate-declaration, and ambiguity diagnostics;
- control-flow correctness beyond binding a `goto` to a same-function label.

## 7. Worked accepted examples

```maho
#pragma toplevel enable

namespace Std
{
    public struct Int32;
}

using Counted<T, N: int,> where T : Constraint = Namespace.Box<T, N>;

global
{
    Std.Int32 globalCount = 0;
}

public struct Example<T, N: int, Rest...,> : Base<T,>
    where T : Constraint,
{
    public void Function(Std.Int32 first, Std.Int32 second,)
    {
    again:
        Call(first, second,);
        new Std.Int32[] { first, second, };
        goto again;
    }
}

Example<Std.Int32, 100,> value;
```

This example demonstrates syntactically legal trailing commas, a global-modified top-level block, a non-type generic parameter, a literal generic argument, a variadic parameter, an alias, and label/goto binding. It does **not** imply that `100` is already evaluated or checked against a compiler-recognized `int` type.
