# Experiment 1: the syntax that language models expect

The main author of Bimlint rules is a language model. A syntax that models guess without documentation needs
fewer corrections than one they have to learn. This experiment asked models to write requirements in a language
that does not exist, and counted what they wrote.

## Method

- **Task.** Twelve requirements in plain English (R1 to R12) that together need every IDS facet, the three
  cardinalities, both kinds of count, a pattern, a list, a range with units, a data type and a reused list.
- **Two prompts.** [prompt-blind.txt](prompt-blind.txt) says only that the language is a small plain-text language
  for information requirements on IFC models. [prompt-constrained.txt](prompt-constrained.txt) adds the design
  constraints: one statement per line, no nesting, keywords, one way to write each thing, compiles to IDS 1.0.
  Neither prompt shows any syntax. The wording avoids the candidate keywords.
- **Subjects.** Four language models of one vendor, from the smallest to the largest tier (S, M, L, XL), each with
  both prompts: eight answers. Each answer came from a fresh session without tools.
- **Answers.** The file, the conventions the model assumed, and four questions about meaning: are subtypes included,
  which unit do the numbers have, does an element without an optional property pass.
- The raw answers are in [raw/](raw/).

## What the models wrote

| Question | Result (of 8) | Bimlint v0.1 |
| --- | --- | --- |
| Comment | `#` 8 | `#` |
| IFC version | `ifc IFC4` 3, `schema IFC4` 3, `ifc-version IFC4` 1, none 1 | `ifc IFC4` |
| Start of a rule | `requirement ID "text"` 3, `rule ID "text"` 1, `spec ID "text"` 1, the bare ID 3. The ID comes first in all 8 | `rule ID "name"` |
| Selection | `for` 3, `applies to` 3, `applicable` 1, none (sentence form) 1 | `for` |
| Filter | `where` 6, `with` 1, repeated selection lines 1 | `where` |
| Requirement | `require` 6, `requires` 1, `must have` 1 | `require` |
| Optional | `optional` first in the line 6, `optional` last 1, `may have` 1 | `optional` |
| Prohibited | `prohibit` 4, `forbid` 2, `prohibited` last 1, `must not have` 1 | `prohibit` |
| Property | `Set.Property` with a dot 7, two words 1 | `Set.Property` |
| Keyword `property` in front | 5 with, 3 without | with |
| Keyword `attribute` in front | 7 with, 1 without | with |
| Classification | `classification "system" ...` 8 | the same |
| Material | `material` and a constraint 8 | the same |
| Equality | `=` 7, `IS` 1, `==` 0 | `=` |
| Pattern | `matches` 8; in quotes 6, between slashes 2 | `matches "..."` |
| Anchors `^...$` in the pattern | 5 | error with a fix |
| List | `[a, b]` 4, `a, b` 3, `{a, b}` 1 | `[a, b]` |
| Named list | `define Name = ...` 5, other `define ...` 2, `LIST` 1 | `define Name = [...]` |
| Membership | `in` 7, `is one of` 1 | `in` |
| Range | `between a and b` 6, two bounds 2 | `between a and b` |
| Unit after the number | 5 (all four with the blind prompt) | required for measures |
| Predefined type | `where PredefinedType = FLOOR` 5, with the class 3 (`type`, `predefined`, `with predefined type`) | `IfcSlab type FLOOR`; the other form is a warning with a fix |
| Data type | `datatype X` 3, `type X` 2, `OF TYPE X` 1, `: X` 1, `as X` 1 | `datatype X` |
| Containment | `part of` 3 (all with the constrained prompt), `contained in` 2, others 3 | `part of ... via ...` |
| At least one element | `count >= 1` 3, `at least 1` 2, others 3 | `require count >= 1` |
| No element | `prohibit` / `forbid` with the class 6, `expect none` 1, `must not exist` 1 | `require count = 0` |
| Several classes | `A, B` 6, `A OR B` 1, one rule per class 1 | `A, B` |
| "Any element" | an abstract class (`IfcElement`, `IfcProduct`) 3, `any` 2, no class 2, `element` 1 | both: `for IfcElement`, `for any where ...` |
| Keywords in lower case | 7 | lower case |

## What the models assumed about meaning

| Question | Result (of 8) | Consequence |
| --- | --- | --- |
| Does `IfcWall` include `IfcWallStandardCase`? | yes 6, no 2 | IDS says no. Bimlint includes subtypes and writes the class list out; `only` gives the IDS meaning |
| Unit of a number without a unit | SI 3, the unit of the model 2, "the unit of the schema" 1, always written 2 | IDS says SI. Bimlint makes the unit mandatory for measures |
| Does an element without an optional property pass? | yes 8 | `optional` is understood as IDS means it |

The two models that answered "no" on subtypes were the larger ones with the constrained prompt, which names IDS.
The others would have written rules that silently skip most walls of an IFC2X3 model.

## What changed in the design

The draft syntax of the project brief was `rule "name"`, `source`, `for ... where ... == ...`, `require Set.Property matches "..."`.
The experiment confirmed `for`, `where`, `require`, `matches`, `in`, quoted patterns and the dotted property.
It changed four things:

1. `=` instead of `==`.
2. Every facet starts with its keyword (`property`, `attribute`, ...).
3. The identifier of a rule is part of its first line.
4. `define` for named lists, `between` for ranges, `optional` and `prohibit` for the cardinalities.

It also produced the list of other spellings that the compiler recognises and repairs
(`src/Bimlint/Syntax/Parser.Support.cs`), and three checks that guard meaning, not syntax:
anchors in patterns, units on measures, and the predefined type written as an attribute.

## Limits

- All subjects come from one vendor; other model families may have other habits. To repeat the experiment,
  give [prompt-blind.txt](prompt-blind.txt) to a model in a fresh session and tally the answer against the table.
- Eight answers show tendencies, not frequencies.
- The requirements were written in English and name some IFC terms (`Pset_DoorCommon`, `IfcBuildingStorey`),
  which fixes the spelling of names.
- The experiment measures writing without documentation. Writing with the one-page description is measured in experiment 2.
