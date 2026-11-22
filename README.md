# Lintel

A small plain-text language for information requirements on BIM models.
It compiles to [buildingSMART IDS 1.0](https://github.com/buildingSMART/IDS), and every IDS 1.0 file can be written in it.
The main author is a language model; a BIM coordinator reads and signs.

**Status: the language draft v0.1, a compiler prototype and an MCP server for AI agents.** The compiler reads Lintel,
checks it against the IFC schema, writes IDS 1.0 and reads IDS 1.0 back. It does not check models yet.
"Lintel" is a working title.

```lintel
ifc IFC4

rule R1 "Fire rating of load-bearing walls"
  source "Alle tragenden Wände müssen eine Feuerwiderstandsklasse aufweisen."
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
| Every IDS file that the audit accepts can be written in Lintel and comes back unchanged | The IDS files of the official repository, and generated files: IDS to Lintel to IDS, compared as canonical XML. Of the 319 official files that the audit accepts, 318 come back unchanged; one labels single values of a list, which Lintel reports and does not keep |
| An IDS file that the audit rejects does not get through | The 27 official files that the audit rejects, and generated files |

The generated files are random combinations of facets, names of the IFC schema and names that are not, in the
combinations IDS allows and in some it forbids. Three hundred of them run with every test run; the first run of
3000 found 164 disagreements between the compiler and the audit, and they are why the compiler now asks the audit.
What Lintel cannot keep from an IDS file is listed in [SPEC.md, section 7](SPEC.md#7-mapping-to-ids-10);
the importer reports each case.

## Documents

| File | Content |
| --- | --- |
| [SPEC.md](SPEC.md) | The language, v0.1 draft: grammar, meaning, examples, mapping to IDS 1.0, diagnostics, open questions |
| [LLM.md](LLM.md) | The language on one page, for the description of a tool that an agent calls |
| [examples/showcase.lintel](examples/showcase.lintel) | Every construct in one file |
| [docs/experiments/01-syntax-priors](docs/experiments/01-syntax-priors/README.md) | Which syntax language models expect without documentation |
| [docs/experiments/02-writing-with-the-description](docs/experiments/02-writing-with-the-description/README.md) | Whether one page is enough for a model to write valid rules |
| [docs/experiments/03-writing-with-the-tools](docs/experiments/03-writing-with-the-tools/README.md) | Whether a model can go from requirements to an IDS file with the tools of the MCP server alone |

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
| MCP server for agents (`src/Lintel.Mcp`) | done: the language guide, a lookup of the IFC schema, the checker, the IDS compiler, the IDS reader |
| Running rules on IFC and in Revit | not started |

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

## Use it from an AI agent

`src/Lintel.Mcp` is an MCP server. MCP (the Model Context Protocol) is the way an AI agent such as Claude Code,
Claude Desktop or Codex calls the functions of a program; here the program is the compiler. With it an agent can
write rules from a requirements text and check its own work, instead of writing names from memory.

| Tool | What the agent gets |
| --- | --- |
| `lintel_language_guide` | The language on one page ([LLM.md](LLM.md)) |
| `lintel_list_properties` | The property sets and properties of the IFC standard with data types, units, allowed values and what each property means; the attributes, predefined types and subtypes of a class. Finds `FireRating` for "fire", and marks the names that the standard misspells, such as `NetHeigtht` |
| `lintel_validate_rules` | The diagnostics as JSON, with fixes and the fixed source |
| `lintel_compile_rules` | The IDS 1.0 file. It is held back while the rules have errors, or warnings with a ready fix; `acceptWarnings` compiles them as written |
| `lintel_import_ids` | An IDS file as Lintel rules, and what Lintel does not keep of it |

A tool takes its input as text or as the path of a file. The server reads only `.lintel`, `.ids` and `.xml` files,
writes only `.ids` and `.lintel` files where a tool is told to, replaces a file only with `overwrite`, and uses no
network. A relative path starts at the project folder.

**In this repository** Claude Code finds the server by itself: [.mcp.json](.mcp.json) starts it with `dotnet run`.

**Anywhere else**, install it as a .NET tool and name it to the client:

```bash
dotnet pack src/Lintel.Mcp -c Release -o artifacts
```

```bash
dotnet tool install --global --add-source artifacts Lintel.Mcp
```

```bash
claude mcp add --transport stdio lintel -- lintel-mcp
```

Other clients take the same command in their list of MCP servers, for example in `claude_desktop_config.json`
of Claude Desktop:

```json
{
  "mcpServers": {
    "lintel": { "command": "lintel-mcp" }
  }
}
```

Then ask for the work, for example: "Turn the requirements in aia.md into Lintel rules for IFC4 and write the IDS file."

Three language models, from small to large, were given nothing but these tools and twelve requirements that name
no property of IFC. Each wrote a rule file without diagnostics and an IDS file; what they found unclear changed the
tools and the guide ([experiment 3](docs/experiments/03-writing-with-the-tools/README.md)).
[src/Lintel.Mcp/evaluation.xml](src/Lintel.Mcp/evaluation.xml) holds ten questions for testing whether a model can
use the server; the tests work out every answer with the tools.

## Layout

```text
SPEC.md, LLM.md          the language
examples/                rule files; the tests check them
src/Lintel/              the library: netstandard2.0 and net8.0
src/Lintel.Cli/          the command line
src/Lintel.Mcp/          the MCP server for AI agents
tests/Lintel.Tests/      tests of the library, including the code blocks of the documents
tests/Lintel.Mcp.Tests/  tests of the server, over the protocol
docs/experiments/        the experiments behind the design
```

## Third-party material

- The library depends on [ids-lib](https://www.nuget.org/packages/ids-lib) (MIT licence, © buildingSMART
  International Ltd.): the IFC schema data of the official IDS audit, and the audit itself.
- The MCP server depends on the [MCP C# SDK](https://www.nuget.org/packages/ModelContextProtocol) (Apache-2.0).
- The IDS standard, its documentation and its test cases belong to buildingSMART International and are licensed
  CC BY-ND 4.0. They are not copied into this repository.

## Licence

MIT, see [LICENSE](LICENSE).
