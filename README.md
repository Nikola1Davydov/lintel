# Lintel

A small plain-text language for information requirements on BIM models.
It compiles to [buildingSMART IDS 1.0](https://github.com/buildingSMART/IDS), and every IDS 1.0 file can be written in it.
The main author is a language model; a BIM coordinator reads and signs.

**Status: design draft and parser prototype.** There is no IDS output and no model checking yet. "Lintel" is a working title.

```lintel
ifc IFC4

rule R1 "Fire rating of load-bearing walls"
  source "AIA 4.2.1: Alle tragenden Wände müssen eine Feuerwiderstandsklasse aufweisen."
  for IfcWall
  where property Pset_WallCommon.LoadBearing = true
  require property Pset_WallCommon.FireRating matches "(F|REI)[0-9]{2,3}"
```

## Why

IDS is XML, and nobody writes it by hand. Language models write it badly: they have to get the XML, the IFC names,
the data types, the SI units and the rules of the IDS audit right at the same time, and nothing tells them what went wrong.
Lintel splits the work: the model writes a short text in a syntax it mostly guesses, and a compiler checks it
against the IFC schema, answers with diagnostics a program can act on, and produces the IDS.

Three things the language does that IDS leaves to the author:

- `for IfcWall` includes the subtypes of the class. IDS matches a class name exactly, so in an IFC2X3 model a rule
  for `IFCWALL` skips every `IfcWallStandardCase`.
- A number that is compared with a measure carries its unit: `150 mm`. IDS stores SI values and no unit.
- Patterns are checked to be XML Schema regular expressions. A pattern with `^...$` matches nothing in a strict checker.

## Documents

| File | Content |
| --- | --- |
| [SPEC.md](SPEC.md) | The language, v0.1 draft: grammar, meaning, examples, mapping to IDS 1.0, diagnostics, open questions |
| [LLM.md](LLM.md) | The language on one page, for the description of a tool that an agent calls |
| [examples/showcase.lintel](examples/showcase.lintel) | Every construct in one file |
| [docs/experiments/01-syntax-priors](docs/experiments/01-syntax-priors/README.md) | Which syntax language models expect without documentation |
| [docs/experiments/02-writing-with-the-description](docs/experiments/02-writing-with-the-description/README.md) | Whether one page is enough for a model to write valid rules |

## What exists

| Part | State |
| --- | --- |
| Lexer, parser, model (`src/Lintel/Syntax`, `src/Lintel/Model`) | done for v0.1 |
| Checks that need no IFC schema (`src/Lintel/Analysis`) | done: the combinations IDS allows, patterns, lists, metadata |
| Diagnostics as data, with fixes (`src/Lintel/Diagnostics`) | done: code, range, hint, expected values, fix, and the fixed source |
| Canonical printer (`src/Lintel/Text`) | done, without comments |
| IDS import (`src/Lintel/Ids`) | first version: reads every official IDS file; classes are imported as `only` |
| Checks against the IFC schema (names, data types, units) | not started |
| IDS output | not started |
| Running rules on IFC and in Revit | not started |

## Build and test

Requires the .NET SDK 8 or later.

```bash
dotnet test
```

```bash
dotnet run --project src/Lintel.Cli -- check examples/showcase.lintel
```

```bash
dotnet run --project src/Lintel.Cli -- import path/to/file.ids
```

`check` prints the diagnostics as JSON and returns 1 when there are errors. `import` prints an IDS file as Lintel.

The tests include the claim "every official IDS file can be written as Lintel": each file of the buildingSMART
repository is imported, printed, read again and printed again. Those files are not part of this repository.
Clone [buildingSMART/IDS](https://github.com/buildingSMART/IDS) next to this folder, or set `LINTEL_IDS_REPO` to
its path; without it these tests are skipped.

## Layout

```text
SPEC.md, LLM.md          the language
examples/                rule files; the tests check them
src/Lintel/              the library: netstandard2.0 and net8.0, no dependencies
src/Lintel.Cli/          the command line
tests/Lintel.Tests/      tests, including the code blocks of the documents
tools/                   generators of data files
docs/experiments/        the experiments behind the design
```

## Third-party material

- `src/Lintel/Ids/Data` holds the names of IFC classes and data types with their XML base types and SI units.
  They are generated from the schema code of [ids-lib](https://github.com/buildingSMART/IDS-Audit-tool)
  (MIT licence, © buildingSMART International Ltd.) by `tools/gen-ifc-names.py`; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- The IDS standard, its documentation and its test cases belong to buildingSMART International and are licensed
  CC BY-ND 4.0. They are not copied into this repository.

## Licence

MIT, see [LICENSE](LICENSE).
