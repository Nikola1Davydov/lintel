# Experiment 3: can a model write rules with the tools alone?

Experiment 2 gave a model the language on one page and asked for rules; the requirements named most of the
property sets. This one gives a model the MCP server and requirements that name none of them. The model has to
find the names in the IFC schema, write the rules, check them and produce the IDS file, with the tools only.

## Method

- **Server.** `src/Lintel.Mcp` as it was when the experiment ran. [tools.txt](tools.txt) is what an MCP client
  shows a model about it: the server instructions and the five tools with their parameters.
- **Task.** [task.txt](task.txt): twelve requirements of a German requirements document (AIA) for IFC4. No property
  set, property, attribute or class of IFC is named, except a property set of the client's own. Eleven can be
  expressed in IDS; one is a sum over elements and cannot. Three are traps for a model that writes from memory:
  the quantity for the net height of a storey is misspelled in the IFC4 standard (`NetHeigtht`); the status of a wall
  is an enumeration of the standard with English values, and the requirement gives German words; a base slab is a
  predefined type and not a class.
- **Subjects.** Three language models of one vendor, a small (S), a medium (M) and a large one (L), each in a
  fresh session with one attempt. [prompt.txt](prompt.txt) is what each was given.
- **Tools.** The models called the tools through a shell command that speaks MCP to the real server and prints the
  answer of the tool, so they saw the texts that a client shows. Every call was logged. They were told to use no
  other source: no files, no web, no documents of this repository.
- **Measures.** The log of the calls; the rule file under the checker; the rules against a reference solution
  ([reference.lintel](reference.lintel)), requirement by requirement; and the report that each model wrote on what
  was unclear, which was the main purpose.

## Result

| | S | M | L |
| --- | --- | --- | --- |
| Tool calls | 21 | 16 | 15 |
| of these, lookups of the IFC schema | 18 | 12 | 10 |
| Checks until the rules had no diagnostics | 1 | 2 | 1 |
| Requirements written as in the reference, of 11 | 11 | 10 | 11 |
| The requirement that IDS cannot express | left out, with a comment | left out, with a comment | left out, with a comment |
| Final check | no diagnostics | no diagnostics | no diagnostics |
| IDS file | written, 11 specifications | written, 11 specifications | written, 11 specifications |
| Text read from the tools | 35,000 characters | 29,000 characters | 31,000 characters |
| Time from the first to the last call | 92 s | 54 s | 68 s |

Every model produced a rule file without diagnostics and an IDS file that the official audit accepts, and the files
read back as the same rules. The files are in [raw/](raw/), with the call logs and the reports.

Against the reference:

- All three found `Qto_BuildingStoreyBaseQuantities.NetHeigtht`, `Pset_WallCommon.Status in ["NEW", "EXISTING"]`,
  `for IfcSlab type BASESLAB`, the units (`2.1 m`, `0 m2`, `0.24 W/m2K`) and the property set of the client.
- The smallest model had no diagnostics at its first check, after eighteen lookups. It looked up more than the
  larger ones and guessed less.
- M wrote the German words into the enumeration first (`in ["neu", "Bestand"]`); the checker rejected them, and M
  mapped them to the values of the standard. See the first finding below.
- M read "dürfen höchstens 0,24 haben" as "at most 0.24 when given" and wrote `optional`; S and L wrote `require`,
  as the reference does. This is a reading of the requirement, and M said so.
- S forbade `IfcProxy` together with `IfcBuildingElementProxy`. Both readings are sound.

## What the models found, and what was changed

| Finding | By | Change |
| --- | --- | --- |
| The fix of the checker replaced the word of the requirement "neu" by the value "NEW" of the standard because the two look alike, and called the result repaired | M | A value is repaired only when it is the same word in another case (`"New"` to `"NEW"`). Otherwise the checker lists the values and says: write the one that means the same. The guide says the same |
| `NetHeigtht` looks like a mistake of the tool. Nothing says that the standard spells it so | S, M, L | The lookup names the version that spells it differently: "Written exactly so in IFC4 (IFC4X3_ADD2: NetHeight)", and marks such names "(sic)" in the overview of a class. IFC4 has 41 of them |
| The lookup does not say what a property means, so two candidates cannot be told apart | M, L | A property comes with the first sentence of its definition in the standard |
| `IfcLogical` is described as "text in quotes" | S | It is described as it is in IDS: true, false or unknown, compared as text, with no fixed spelling. The specification said "true or false" for it, which was wrong, and is corrected |
| The first example of the guide puts the number of the requirement into `source`; the text below says not to | M | The examples have no number in `source` |
| The guide links to the specification, which no tool serves | L | The link is gone from the guide |
| The guide has no word on the title and the other texts of the IDS file; the IDS is titled "Untitled" | L | The guide shows `title` and names the others. The compiler tool says how to give a title when there is none |
| Whether an element that lacks the property of a `where` condition is checked | S, L | The guide says: it is not selected |
| "At most 0.24": `require` or `optional`? | M, L | The guide says: a limit means the value must be there; `optional` is for "if given" |
| The list of units in the guide lacks `W/m2K`, which its own example uses | S | Added, with the rule for every other measure |
| "One rule for one requirement": are two sentences of one numbered requirement one? | S, L | The guide says: one rule for one numbered requirement; it may hold several `require` lines |
| A comment on a requirement that IDS cannot express does not reach the IDS file | L | The guide says that comments are not written to IDS, and that `description` at the top of the file is |
| A search without a match does not say that a property set of one's own needs no lookup | S | It says so |

Not changed: which of an attribute and a quantity a requirement means (`OverallHeight` or `Qto_DoorBaseQuantities.Height`)
is a decision about the requirement; the schema data has no descriptions of predefined types; where `note` and
`source` go in IDS is in the specification and not in the guide.

## Limits

- One vendor, three models, one attempt each, one task of twelve requirements.
- The task and the reference solution were written by the author of the tools, who knew where the traps were.
- The models called the tools through a shell command, not through an MCP client, and were told that this was a
  usability test.
- The reports are stored as written, with folder paths replaced and one remark about the test harness left out.
