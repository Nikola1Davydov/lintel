# Experiment 2: is one page enough to write valid rules?

Experiment 1 asked what language models write without documentation. This one asks what they write with it:
a model gets [LLM.md](../../../LLM.md), the one-page description of Lintel, and a requirements text it has not seen,
and writes a rule file. The file goes through the checker of this repository.

## Method

- **Description.** `LLM.md` as it was when the experiment ran; the prompt told the model to read this file and
  nothing else. The transcripts confirm that each model read only this file.
- **Task.** [prompt.txt](prompt.txt): fourteen paragraphs of a German requirements document (AIA) for IFC4.
  None of them is an example of the description. Twelve can be expressed in IDS: an attribute pattern, a quantity
  with a unit, a list of values, a condition on a property, a classification, a spatial relation, a material
  condition with a custom property set, an optional pattern, both counts, a predefined type with an angle in
  degrees, and a prohibited property whose name has a space. Two cannot: a sum over elements and a comparison
  between a door and its wall.
- **Subjects.** Four language models of one vendor, from the smallest to the largest tier (S, M, L, XL),
  each in a fresh session, one attempt, no feedback from the checker.
- **Measure.** The diagnostics of `lintel check` on the answer, unchanged. The checker of this stage covers the
  grammar and the rules of IDS that need no IFC schema; IFC names, data types and units were reviewed by hand.

## Result

| Model | Rules written | Errors | Warnings | Not expressible, as comments |
| --- | --- | --- | --- | --- |
| S | 12 | 0 | 0 | both |
| M | 12 | 0 | 0 | both |
| L | 12 | 0 | 0 | both |
| XL | (pending) | | | |

Every answer received so far passed on the first attempt. The answers are in [raw/](raw/).

Reviewed by hand, the twelve rules of each answer say what the paragraphs say:

- units are written and correct: `> 0 m2`, `>= 0.9 m`, `between 2 deg and 5 deg`;
- the custom angle has its data type: `datatype IfcPlaneAngleMeasure`;
- the predefined type is with the class: `for IfcRoof type FLAT_ROOF`;
- the name with a space is quoted: `prohibit property BIM_Intern."Bemerkung intern"`;
- the optional requirement is `optional property Pset_WallCommon.AcousticRating matches "Rw [0-9]{2} dB"`;
- patterns have no anchors;
- the two requirements that IDS cannot express are left out and named in a comment, as the description asks.

The answers differ where the requirement leaves room: "Bauteile aus Stahlbeton" became `for IfcElement where material ...`
in one answer and `for any where material ...` in the others, and "Decken" became `IfcSlab` in all, with a remark
that ceilings may be meant.

## What the models found unclear

Each answer ended with what the description did not say. The description was changed afterwards for the points
that are about the language:

| Remark | Change |
| --- | --- |
| Whether an identifier such as `4.1.1` must be quoted | Stated: an ID of letters, digits and `_` is bare, any other is quoted |
| Whether `material = "..."` is valid, and how it treats several materials | An example was added; "any material of the element, layers and constituents included" |
| Whether a facet reads the same after `where` | Stated |
| Which unit IDS uses for an angle | Stated: write the unit of the requirement, the compiler converts |
| Whether `source` carries the paragraph number | Stated: the wording without its number |

The other remarks are about the requirements, not the language: which IFC class "Decken" means, how "Büroräume"
would be selected, and that `Pset_ColumnCommon.LoadBearing` and `Pset_DoorCommon.IsExternal` are implied by
"tragend" and "Außen" and not named. They are the questions a reviewer has to answer, and the models asked them.

## Limits

- One vendor, four models, one attempt each, one task.
- The checker does not yet verify IFC names, data types and units; the hand review found no mistake, but the
  requirements named most of the property sets.
- The requirements are short and clear. Real documents are longer and vaguer.
