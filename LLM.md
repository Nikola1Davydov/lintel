# Bimlint in one page

Bimlint states information requirements for IFC models. It compiles to buildingSMART IDS 1.0.
This page is the whole language for an author; it is meant to be the description of the tool that checks rules.

## Shape of a file

```bimlint
title "Fire safety requirements"            # optional: the title of the IDS file
ifc IFC4                                    # IFC2X3, IFC4 or IFC4X3_ADD2; several with commas
define Status = ["New", "Existing", "Demolish"]         # optional: a named list

rule R1 "Fire rating of load-bearing walls"             # rule ID "name"; the ID is optional
  source "Alle tragenden Wände müssen eine Feuerwiderstandsklasse aufweisen."
  for IfcWall                                           # which elements
  where property Pset_WallCommon.LoadBearing = true     # optional conditions, joined with 'and'
  require property Pset_WallCommon.FireRating matches "(F|REI)[0-9]{2,3}"   # one requirement per statement
```

The ID is a plain word of letters, digits and `_` (`R1`); any other ID is quoted: `rule "4.2.1" "Fire rating"`.
Order inside a rule: `rule`, then `source` / `description` / `instructions` / `ifc` (all optional), then `for`,
then `where`, then the requirements. `source` holds the original wording of the requirement, word for word,
without its number.
Comments start with `#`; they are for the reader of the rules and are not written to IDS.
Line breaks and indentation have no meaning.
Besides `title`, the top of the file may say `description`, `version`, `author` (an e-mail address),
`date` ("2026-11-22"), `purpose`, `milestone` and `copyright`, each with a text.

## for: the elements

| Write | Selects |
| --- | --- |
| `for IfcWall` | walls **and all subtypes** (`IfcWallStandardCase`, ...). `for IfcElement` selects every element |
| `for IfcWall, IfcSlab` | several classes |
| `for only IfcWall` | exactly this class, without subtypes |
| `for IfcSlab type FLOOR` | a predefined type; a user-defined type is quoted: `type "Podium"` |
| `for any` | no class; the `where` conditions select, and at least one is needed |

## Facets: what is checked

A facet follows `where`, `and`, `require`, `optional` or `prohibit`, and reads the same in each place.
It always starts with its keyword. Without a constraint it means "is present".
`where` selects: an element that lacks the property, attribute or material named there is not selected, and the
rule says nothing about it.

| Facet | Example |
| --- | --- |
| `property` | `property Pset_WallCommon.FireRating = "F90"`, also quantities: `property Qto_SlabBaseQuantities.Width` |
| `attribute` | `attribute Name matches "D[0-9]{3}"` |
| `classification` | `classification "Uniclass 2015" matches "Ss_25.*"` (system, then the code) |
| `material` | `material in ["Concrete", "Masonry"]`, `material = "Stahlbeton"`: the name or the category of any material of the element, layers and constituents included |
| `part of` | `part of IfcBuildingStorey via IfcRelContainedInSpatialStructure` (also `IfcRelAggregates`, `IfcRelAssignsToGroup`, `IfcRelNests`, `IfcRelVoidsElement IfcRelFillsElement`; without `via`: any of them) |
| `entity` | `require entity IfcBuildingElementProxy` (the class as a requirement) |

A name with spaces or other characters is quoted, and only that name: `property BIM_Intern."Bemerkung intern"`.
A custom property with a value gets the data type of the value (text: `IfcLabel`); for a measure write it:
`property ACME.ClearHeight datatype IfcLengthMeasure >= 2.2 m`. Common data types: `IfcLabel`, `IfcText`,
`IfcIdentifier`, `IfcBoolean`, `IfcInteger`, `IfcReal`, `IfcCountMeasure`, `IfcLengthMeasure`, `IfcAreaMeasure`,
`IfcVolumeMeasure`, `IfcPlaneAngleMeasure`, `IfcMassMeasure`, `IfcThermalTransmittanceMeasure`.

## Constraints: what the value must be

| Write | Meaning |
| --- | --- |
| `= "F90"`, `= 42`, `= true` | exactly this value |
| `in ["A", "B"]`, `in Status` | one of a list |
| `matches "F[0-9]{2}"` | the whole value matches the pattern |
| `> 0`, `>= 0`, `< 10`, `<= 10` | a bound |
| `between 150 mm and 400 mm` | a range, both ends included |
| `length <= 12`, `length = 4`, `length >= 2` | number of characters |
| `> 0 m2 and <= 500 m2` | several terms that must all hold |
| `... note "D and three digits"` | explains a constraint to the reader |

## require, optional, prohibit, count

| Write | An element passes when |
| --- | --- |
| `require F` | it has F (and F meets the constraint) |
| `optional F` | it has no F, or F meets the constraint. Needs a constraint. Only for "if given": a limit such as "at most 0.24" means the value must be there, so `require` |
| `prohibit F` | it has no F. `prohibit property` takes no constraint |
| `require count >= 1` | (whole rule) at least one element is selected; it may be the only requirement |
| `require count = 0` | (whole rule) no element is selected; the rule has no other requirement |

## Rules that authors get wrong

1. Compare with `=`. There is no `==`, `!=`, `or`, `not`. IDS cannot express them: use a list, a pattern or a second rule.
2. Text values are in double quotes; numbers, `true` and `false` are not. Text is compared with case.
   A double quote inside a string is written twice (`""`); every other character is taken as it is.
   A date, a time or a duration is text in the ISO form and can have bounds: `>= "2026-01-01"`, `= "P1D"`.
3. A pattern is an XML Schema regular expression in double quotes. It always matches the whole value:
   **no `^` and no `$`**, no `(?...)`, no lazy quantifiers, no `\b`. Strings are raw: write `\d`, not `\\d`.
4. A number that is compared with a measure **carries its unit**: `150 mm`, `2.2 m`, `12 m2`, `0.24 W/m2K`, `45 deg`.
   Write the unit of the requirement; the compiler converts it to the SI unit that IDS stores.
   Units: `m cm mm`, `m2 cm2 mm2`, `m3`, `rad deg`, `kg g t`, `s min h d`, `K degC`, `W kW`, `Pa kPa MPa`, `N kN`,
   `W/m2K`; any other measure takes its SI unit written without spaces, such as `kg/m3`.
5. The predefined type belongs to the class: `for IfcSlab type FLOOR`, not `where attribute PredefinedType = ...`.
6. Each requirement has its own `require` / `optional` / `prohibit`. Conditions after `where` are joined with `and`.
7. Use the standard property sets of the IFC version (`Pset_...`, `Qto_...`) with their exact names;
   these prefixes are reserved, so a custom set has another name. Where the standard lists the values of a
   property (`Status`: `"NEW"`, `"EXISTING"`, ...), write the value that means the same as the wording of the
   requirement; the wording itself stays in `source`.
8. One rule for one numbered requirement of the source document; it may hold several `require` lines.
   Do not merge and do not invent requirements.
9. Bimlint cannot express: a comparison of two properties, sums and counts other than the two above, uniqueness,
   geometry, or anything between two elements beyond `part of`. Leave such a requirement out and say so in a comment;
   when the IDS file itself must say what it leaves out, say it in `description` at the top of the file.

## More examples

```bimlint
ifc IFC4
define Status = ["New", "Existing", "Demolish", "Temporary"]

rule R2 "Spaces are named"
  for IfcSpace
  require attribute Name
  require attribute LongName

rule R3 "Floor slabs are 150 to 400 mm thick"
  for IfcSlab type FLOOR
  require property Qto_SlabBaseQuantities.Width between 150 mm and 400 mm

rule R4 "Structural systems are concrete or masonry"
  for any
  where classification "Uniclass 2015" matches "Ss_25.*"
  require material in ["Concrete", "Masonry"]

rule R5 "Doors are located in a storey"
  for IfcDoor
  require part of IfcBuildingStorey via IfcRelContainedInSpatialStructure

rule R6 "Acoustic rating of doors, when given"
  for IfcDoor
  optional property Pset_DoorCommon.AcousticRating in ["Rw 32 dB", "Rw 37 dB", "Rw 42 dB"]

rule R7 "No legacy codes"
  for IfcElement
  prohibit property ACME_Old.LegacyCode

rule R8 "U-value of external load-bearing walls"
  for IfcWall
  where property Pset_WallCommon.IsExternal = true
    and property Pset_WallCommon.LoadBearing = true
  require property Pset_WallCommon.ThermalTransmittance <= 0.24 W/m2K

rule R9 "No proxies"
  for IfcBuildingElementProxy
  require count = 0

rule R10 "The model has named storeys"
  for IfcBuildingStorey
  require count >= 1
  require attribute Name

rule R11 "Status of load-bearing elements"
  for IfcWall, IfcColumn, IfcBeam, IfcSlab
  require property ACME_Common.Status in Status
    instructions "Ask the project lead when the status is unclear."
```

## The answer of the checker

The checker answers with JSON: `ok`, and `diagnostics` with `severity`, `code`, `message`, `line`, `column`, `hint`,
`expected` (the allowed values) and `fix`. When `fixed` is present it is the whole source with every fix applied;
when `fixedOk` is true, use it as it is. `ok` is true when there are no errors. A warning is valid IDS that
probably says something else than you mean: repair it too.
