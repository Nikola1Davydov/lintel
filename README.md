# Lintel

A small plain-text language for information requirements on BIM models.
It compiles to [buildingSMART IDS 1.0](https://github.com/buildingSMART/IDS), and every IDS 1.0 file can be written in it.
The main author is a language model; a BIM coordinator reads and signs.

**Status: the language draft v0.1 and a compiler prototype.** It reads Lintel, checks it against the IFC schema,
writes IDS 1.0 and reads IDS 1.0 back. It does not check models yet. "Lintel" is a working title.

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
- Patterns are checked to be XML Schema regular expressions. A pattern with `^...$` matches nothing in a checker
  that follows the standard; the compiler warns and gives the portable form.

## How it is kept honest

The judge of what a valid IDS file is, is the official audit of buildingSMART
([IDS-Audit-tool](https://github.com/buildingSMART/IDS-Audit-tool), the library `ids-lib`). The compiler uses its
IFC schema data and runs it as its last check, and the tests hold it to three claims:

| Claim | How it is tested |
| --- | --- |
| The compiler never writes IDS that the audit rejects | The audit runs on every result; what it rejects is an error at the place of the source |
| Every IDS file that the audit accepts can be written in Lintel and comes back unchanged | The 346 IDS files of the official repository, and generated files: IDS to Lintel to IDS, compared as canonical XML |
| An IDS file that the audit rejects does not get through | The 27 invalid official files, and generated files |

The generated files are random combinations of facets, names of the IFC schema and names that are not, in the
combinations IDS allows and in some it forbids. Three hundred of them run with every test run; the first run of
3000 found 164 disagreements between the compiler and the audit, and they are why the compiler now asks the audit.
What Lintel cannot keep from an IDS file is listed in [SPEC.md, section 7](SPEC.md#7-mapping-to-ids-10);
the importer reports each case and no official file has one.

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
| Checks against the IFC schema (`src/Lintel/Analysis`, `src/Lintel/Ifc`) | done for IFC2X3, IFC4 and IFC4X3_ADD2: classes with subtypes, predefined types, attributes, standard property sets and properties, data types, units, values; they follow the rules of the official audit |
| Diagnostics as data, with fixes (`src/Lintel/Diagnostics`) | done: code, range, hint, expected values, fix, and the fixed source |
| IDS output (`src/Lintel/Ids`) | done: IDS 1.0, with the official audit as the last check |
| IDS import (`src/Lintel/Ids`) | done: reads IDS 1.0, folds a class list into a class with its subtypes, and checks that the file comes back unchanged |
| Canonical printer (`src/Lintel/Text`) | done, without comments |
| Running rules on IFC and in Revit | not started |
| MCP tools for agents | not started |

## Build and test

Requires the .NET SDK 9.0.200 or later (the solution file is a `.slnx`). The library itself targets .NET Standard 2.0 and .NET 8.

```bash
dotnet test
```

```bash
dotnet run --project src/Lintel.Cli -- check examples/showcase.lintel
```

```bash
dotnet run --project src/Lintel.Cli -- compile examples/showcase.lintel
```

```bash
dotnet run --project src/Lintel.Cli -- import path/to/file.ids
```

`check` prints the diagnostics as JSON and returns 1 when there are errors. `compile` prints the IDS file.
`import` prints an IDS file as Lintel, and what it could not keep on the error stream.

The tests on the official IDS files need those files, which are not part of this repository.
Clone [buildingSMART/IDS](https://github.com/buildingSMART/IDS) next to this folder, or set `LINTEL_IDS_REPO` to
its path; without it these tests are skipped. All other tests, the generated files included, need nothing.

## Layout

```text
SPEC.md, LLM.md          the language
examples/                rule files; the tests check them
src/Lintel/              the library: netstandard2.0 and net8.0
src/Lintel.Cli/          the command line
tests/Lintel.Tests/      tests, including the code blocks of the documents
docs/experiments/        the experiments behind the design
```

## Third-party material

- The library depends on [ids-lib](https://www.nuget.org/packages/ids-lib) (MIT licence, © buildingSMART
  International Ltd.): the IFC schema data of the official IDS audit, and the audit itself.
- The IDS standard, its documentation and its test cases belong to buildingSMART International and are licensed
  CC BY-ND 4.0. They are not copied into this repository.

## Licence

MIT, see [LICENSE](LICENSE).
