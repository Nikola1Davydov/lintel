# Lintel language specification, v0.1 (draft)

Lintel is a small plain-text language for information requirements on BIM models.
A Lintel file compiles to buildingSMART IDS 1.0, and every valid IDS 1.0 file can be imported into Lintel.
The main author is an LLM agent; a BIM coordinator reads and signs the result.

"Lintel" is a working title. Nothing in this draft is frozen; open points are listed in [section 11](#11-open-questions).
The short form of this document for agents is [LLM.md](LLM.md).

## 1. Design rules

1. **The core is IDS 1.0.** Every construct of this document maps to IDS 1.0 ([section 7](#7-mapping-to-ids-10)).
   Nothing that IDS cannot express is in the core.
2. **One spelling.** Each thing is written in one way. Another spelling is either an error or a style warning,
   and both carry a fix that a program can apply ([section 9](#9-diagnostics)).
3. **IFC names as in the IFC documentation**: `IfcWall`, `Pset_WallCommon`, `FireRating`, `IfcLabel`.
4. **Flat.** A statement starts with a keyword. There are no blocks, and line breaks and indentation have no meaning.
   The canonical layout has one statement per line.
5. **No program.** No variables, functions, loops or side effects. A rule always terminates, and
   one file with one model always gives one result.
6. **Checked before it runs.** The compiler checks every IFC name, data type and unit against the IFC version(s)
   that the file names.

## 2. A first example

```lintel
ifc IFC4

rule R1 "Fire rating of load-bearing walls"
  source "AIA 4.2.1: Alle tragenden Wände müssen eine Feuerwiderstandsklasse aufweisen."
  for IfcWall
  where property Pset_WallCommon.LoadBearing = true
  require property Pset_WallCommon.FireRating matches "(F|REI)[0-9]{2,3}"
```

It compiles to this IDS specification (the `info` block is left out):

```xml
<specification name="Fire rating of load-bearing walls" identifier="R1" ifcVersion="IFC4">
  <applicability minOccurs="0" maxOccurs="unbounded">
    <entity>
      <name>
        <xs:restriction base="xs:string">
          <xs:enumeration value="IFCWALL"/>
          <xs:enumeration value="IFCWALLELEMENTEDCASE"/>
          <xs:enumeration value="IFCWALLSTANDARDCASE"/>
        </xs:restriction>
      </name>
    </entity>
    <property dataType="IFCBOOLEAN">
      <propertySet><simpleValue>Pset_WallCommon</simpleValue></propertySet>
      <baseName><simpleValue>LoadBearing</simpleValue></baseName>
      <value><simpleValue>true</simpleValue></value>
    </property>
  </applicability>
  <requirements description="AIA 4.2.1: Alle tragenden Wände müssen eine Feuerwiderstandsklasse aufweisen.">
    <property dataType="IFCLABEL" cardinality="required">
      <propertySet><simpleValue>Pset_WallCommon</simpleValue></propertySet>
      <baseName><simpleValue>FireRating</simpleValue></baseName>
      <value>
        <xs:restriction base="xs:string"><xs:pattern value="(F|REI)[0-9]{2,3}"/></xs:restriction>
      </value>
    </property>
  </requirements>
</specification>
```

Three things happened that the author did not write: `IfcWall` was expanded to its subtypes
([section 5.4](#54-classes-and-subtypes)), the data types were taken from the IFC property set definitions
([section 5.7](#57-data-types)), and the pattern was checked to be an XML Schema regular expression.

## 3. Lexical structure

| Element | Form |
| --- | --- |
| Encoding | UTF-8 |
| Comment | `#` to the end of the line |
| White space | Spaces, tabs and line breaks separate tokens and have no other meaning |
| Keyword | Lower case, reserved: see [section 10](#10-reserved-words) |
| Name | A letter or `_`, then letters, digits and `_`: `IfcWall`, `Pset_WallCommon`, `FLOOR`, `R1`, `mm`. Case matters |
| Quoted name | A string, for a name with other characters: `"Brandschutz Türen"."Feuerwiderstand (neu)"` |
| String | `"..."` on one line. A backslash is an ordinary character. A quote inside a string is written twice: `"a ""b"" c"` |
| Text block | `"""` at the end of a line, then any lines, then `"""` alone on a line. For text with line breaks |
| Number | `12`, `-3`, `0.15`, `1e3`. The decimal separator is a dot. `12` is an integer and `12.0` is not |
| Unit | A name after a number on the same line, with at most one `/`: `150 mm`, `0.24 W/m2K` ([section 5.8](#58-units)) |
| Boolean | `true`, `false` |

Strings are raw so that a pattern is written as it is: `"F\d{2,3}"` contains one backslash.

## 4. Grammar

```ebnf
file         = { header } { define | rule } ;

header       = "ifc" versions
             | ( "title" | "description" | "version" | "author" | "date"
               | "purpose" | "milestone" | "copyright" ) text
             | "import" STRING ;
versions     = VERSION { "," VERSION } ;              (* IFC2X3 | IFC4 | IFC4X3_ADD2 *)

define       = "define" NAME "=" list ;

rule         = "rule" [ NAME | STRING ] STRING
               { "source" text | "description" text | "instructions" text | "ifc" versions }
               "for" ( "any" | entity ) [ "where" facet { "and" facet } ]
               { requirement } ;

requirement  = ( "require" | "optional" | "prohibit" ) facet [ "uri" STRING ] [ "instructions" text ]
             | "require" "count" ( ">=" "1" | "=" "0" ) ;

facet        = "entity" entity
             | "attribute" name [ constraint ]
             | "property" name "." name [ "datatype" NAME ] [ constraint ]
             | "classification" name [ constraint ]
             | "material" [ constraint ]
             | "part" "of" entity [ "via" relation ] ;

entity       = classes [ "type" types ] ;
classes      = [ "only" ] NAME { "," NAME } [ note ]
             | "matches" patterns [ note ] ;
types        = ( NAME | STRING ) { "," ( NAME | STRING ) } [ note ]
             | "matches" patterns [ note ] ;
name         = NAME | STRING
             | "[" ( NAME | STRING ) { "," ( NAME | STRING ) } "]" [ note ]
             | "matches" patterns [ note ] ;
relation     = "IfcRelAggregates" | "IfcRelAssignsToGroup" | "IfcRelContainedInSpatialStructure"
             | "IfcRelNests" | "IfcRelVoidsElement" "IfcRelFillsElement" ;

constraint   = "=" literal
             | term { "and" term } [ note ] ;
term         = "in" ( list | NAME )
             | "matches" patterns
             | ( ">" | ">=" | "<" | "<=" ) bound
             | "between" bound "and" bound
             | "length" ( "=" | ">=" | "<=" ) INTEGER ;
patterns     = STRING | "[" STRING { "," STRING } "]" ;
note         = "note" text ;

list         = "[" literal { "," literal } [ "," ] "]" ;
literal      = STRING | number | "true" | "false" ;
number       = NUMBER [ NAME [ "/" NAME ] ] ;         (* a number and its unit *)
bound        = number | STRING ;                      (* text for a date, a time or a duration *)
text         = STRING | TEXTBLOCK ;
```

The grammar is LL(2). The word `and` joins facets after `where` and joins terms inside a constraint;
the next token decides: a facet keyword starts a facet, anything else continues the constraint.

## 5. Meaning

### 5.1 File header

The header statements come before the first rule. Each may appear once.

| Statement | Meaning |
| --- | --- |
| `ifc IFC4` | The IFC version(s) of all rules: `IFC2X3`, `IFC4`, `IFC4X3_ADD2`, separated by commas. A rule may override it. A rule without a version is an error |
| `title "..."` | Title of the rule set. `Untitled` when absent |
| `description`, `version`, `purpose`, `milestone`, `copyright` | Free text |
| `author "name@example.com"` | An e-mail address |
| `date "2026-11-21"` | A date, `YYYY-MM-DD` |
| `import "lists.lintel"` | Makes the `define` lists of another file available. It never adds rules. The path is relative to the importing file |

`define Name = [...]` gives a name to a list of values, anywhere outside a rule. The name is used after `in`.
A list holds strings or numbers, not both. A defined name cannot be defined again.

### 5.2 Rules

A rule has a name, an optional identifier before the name, optional metadata, one `for`, and requirements:

```lintel fragment
rule R1 "Fire rating of load-bearing walls"                  # identifier and name
  source "AIA 4.2.1: Alle tragenden Wände müssen ..."        # the original wording of the requirement
  description "Needed for the fire protection concept."      # why it matters
  instructions "Take the class from the fire protection report."  # how to fulfil it
  ifc IFC4, IFC4X3_ADD2                                      # overrides the file header
  for IfcWall
  where property Pset_WallCommon.LoadBearing = true
  require property Pset_WallCommon.FireRating
```

The identifier is a name (`R1`) or a string (`"AIA 4.2.1"`). The order of statements is fixed:
header of the rule, metadata, `for`, `where`, requirements.

`for` and `where` select the elements the rule applies to. All conditions must hold; there is no `or` and no `not`,
because IDS has none. `for any` selects by the `where` conditions alone and needs at least one.

Each requirement is checked on every selected element. The rule passes when every selected element meets
every requirement.

### 5.3 Requirements and cardinality

There are two levels, as in IDS.

**For one requirement**, the first word says what happens when the information is missing:

| Statement | An element passes when |
| --- | --- |
| `require F` | it has `F`, and `F` meets the constraint if there is one |
| `optional F` | it does not have `F`, or it has `F` and `F` meets the constraint |
| `prohibit F` | it does not have `F`; with a constraint: it has no `F` that meets the constraint |

"Has" means present with a value that is not empty. IDS does not allow every combination; the compiler rejects the others:

| Facet | `require` | `optional` | `prohibit` |
| --- | --- | --- | --- |
| `entity` | yes | no | no |
| `attribute` | yes | only with a constraint | yes |
| `property` | yes | only with a constraint or `datatype` | only without constraint and `datatype` |
| `classification` | yes | only with a constraint | yes |
| `material` | yes | only with a constraint | yes |
| `part of` | yes | no | yes |

**For the whole rule**, `count` says how many elements must be selected:

| Statement | Meaning |
| --- | --- |
| *(none)* | The rule applies when elements are selected, and passes when there are none |
| `require count >= 1` | At least one element must be selected, and all selected elements must meet the requirements |
| `require count = 0` | No element may be selected. Such a rule has no other requirement |

No other count is part of IDS 1.0. A rule without `count` needs at least one requirement.

IDS takes a constraint on text that an empty text can meet, such as `matches ".*"` or a list with `""`, for a
missing one. So `optional attribute Name matches ".*"` is an error, like `optional attribute Name`; write `".+"`.

### 5.4 Classes and subtypes

A class name selects **that class and its subtypes**. `for IfcWall` in IFC4 selects `IfcWall`,
`IfcWallStandardCase` and `IfcWallElementedCase`; `for IfcElement` selects every element class.
IDS itself matches a class name exactly, so the compiler writes out the list of classes.

| Written | Selected |
| --- | --- |
| `IfcWall` | `IfcWall` and all its subtypes |
| `IfcWall, IfcSlab` | both, with their subtypes |
| `only IfcWall` | exactly `IfcWall` |
| `only IfcWall, IfcWallStandardCase` | exactly these two |
| `matches "IFC.*TYPE"` | every class whose upper-case name matches the pattern |
| `IfcSlab type FLOOR` | slabs with the predefined type `FLOOR` |
| `IfcSlab type FLOOR, ROOF` | either type |
| `IfcWall type "Brandwand"` | a user-defined type, written as a string |

A predefined type written as a name must be a value of the enumeration of a selected class in the IFC schema.
A string is free text: it is compared with `ObjectType` or `ElementType` when the predefined type is `USERDEFINED`.
The predefined type may come from the type object of the element, as IDS defines.
IDS takes free text only when every selected class has a predefined type with the value `USERDEFINED`.
A class without a predefined type, such as `IfcWall` in IFC2X3, cannot be selected with `type` at all.

`only` with an abstract class, such as `only IfcElement`, is valid IDS and selects nothing; the compiler warns.

For a rule with several IFC versions the list of classes is the union over the versions.
Each named class must exist in at least one of the versions, and each version must have at least one of the
selected classes. A class that one version lacks works there through those of its subtypes that the version has too:
`for IfcBuiltElement` in a rule for IFC4 and IFC4X3_ADD2 selects walls, slabs and so on in both. The compiler says so.

The same form follows `entity` in a requirement and `part of` in a relationship.

### 5.5 Facets

A facet names one piece of information. The same facets are used after `where`, `and`, `require`, `optional`
and `prohibit`. Without a constraint a facet means "is present".

| Facet | Example | Notes |
| --- | --- | --- |
| `entity` | `require entity IfcBuildingElementProxy` | The class of the element; only in requirements, because `for` states it for the selection |
| `attribute` | `attribute Name matches "D[0-9]{3}"` | An attribute of the IFC class |
| `property` | `property Pset_WallCommon.FireRating = "F90"` | Property set, dot, property. Also for quantities: `property Qto_SlabBaseQuantities.Width` |
| `classification` | `classification "Uniclass 2015" matches "Ss_25.*"` | The system, then a constraint on the code. A more specific code of the same system also matches, as IDS defines |
| `material` | `material in ["Concrete", "Masonry"]` | The name or category of any material of the element |
| `part of` | `part of IfcBuildingStorey via IfcRelContainedInSpatialStructure` | The element is part of such an object. Without `via` any of the five relationships counts; the search is transitive |

Names of property sets, properties and attributes may also be a list or a pattern:
`property [Pset_WallCommon, Pset_SlabCommon].FireRating`, `attribute matches ".*Name"`.

### 5.6 Constraints

| Constraint | Meaning |
| --- | --- |
| `= "F90"`, `= 42`, `= true` | Exactly this value. Text is compared with case |
| `in ["A", "B"]`, `in Status` | One of the values of the list |
| `matches "F[0-9]{2}"` | The whole value matches the pattern. `matches ["a.*", "b.*"]` accepts either pattern |
| `> 0`, `>= 0`, `< 10`, `<= 10` | A bound on a number, a date, a time or a duration: `>= "2026-01-01"` |
| `between 150 mm and 400 mm` | Both ends included |
| `length = 4`, `length >= 2`, `length <= 12` | Number of characters of a text |
| `... and ...` | Several terms, all of which must hold: `> 0 m2 and <= 500 m2` |
| `... note "..."` | A sentence that explains the constraint to a reader |

Patterns are XML Schema regular expressions. The whole value must match, so there are no anchors:
`^` and `$` are ordinary characters there. Lazy quantifiers, look-around, back-references and `\b` do not exist.
`\d`, `\w`, `\s`, `[A-Z]`, `{2,3}`, `|`, `(...)` and `.` work as usual.
The official audit and the common checkers read patterns with the regular expressions of .NET or Python, which
accept more: there `^...$` are anchors, and `(?i)`, `.*?` and `\/` work. Such a pattern is valid IDS that a checker
following XML Schema rejects or reads differently. The compiler warns about each of them and offers the form that
every checker reads the same way; it rejects only a pattern that the official audit cannot read.

A real number is equal to the required value within the IDS tolerance: a relative part of 10⁻⁶ plus 10⁻⁶.
Bounds are compared exactly.

### 5.7 Data types

A property with a constraint needs a data type in IDS. It is found in this order:

1. `datatype IfcLengthMeasure` written after the property;
2. the definition of the property in a standard property set (`Pset_...`, `Qto_...`) of the IFC version;
3. the kind of the value: text is `IfcLabel`, an integer is `IfcInteger`, a number with a dot is `IfcReal`,
   `true` and `false` are `IfcBoolean`.

`datatype` on a standard property must be its standard type.
A property without a constraint gets a data type only when `datatype` is written.
When the type comes from a number (step 3), the compiler warns: a model that stores the property as a measure
fails the rule.

The value must fit the data type, and IDS allows different terms for different kinds of value:

| Kind | Data types | Written as | Terms |
| --- | --- | --- | --- |
| Text | `IfcLabel`, `IfcText`, `IfcIdentifier`, ... | `"F90"` | `=`, `in`, `matches`, `length` |
| Number | `IfcReal`, `IfcInteger`, `IfcCountMeasure`, every measure | `42`, `0.24 W/m2K`; `IfcInteger` without a dot | `=`, `in`, bounds, `between` |
| True or false | `IfcBoolean`, `IfcLogical` | `true`, `false` | `=` |
| Date and time | `IfcDate`, `IfcDateTime`, `IfcTime`, `IfcDuration` | `"2026-11-22"`, `"2026-11-22T10:00:00"`, `"10:00:00"`, `"P1D"` | `=`, `in`, bounds, `between` |

IDS also allows `matches` on a value that is not text; the pattern is then compared with the way the checker
writes the value, which IDS does not fix, and the compiler warns.
A value that IDS can spell and the language cannot write as its kind is written as text and passed on as it is:
`= "NaN"` for a number, `= "1"` for true.

The value of an attribute has the type of the attribute in the IFC schema, and follows the same table.

### 5.8 Units

IDS stores every measure in its SI unit, whatever the unit of the model. In Lintel **a number that is compared with
a measure carries its unit**, and the compiler converts it: `150 mm` becomes `0.15`.
A measure without a unit is an error, and so is a unit on a value that is not a measure.

| Measure | Units |
| --- | --- |
| `IfcLengthMeasure`, `IfcPositiveLengthMeasure`, `IfcNonNegativeLengthMeasure` | `m`, `cm`, `mm` |
| `IfcAreaMeasure` | `m2`, `cm2`, `mm2` |
| `IfcVolumeMeasure` | `m3` |
| `IfcPlaneAngleMeasure`, `IfcPositivePlaneAngleMeasure` | `rad`, `deg` |
| `IfcMassMeasure` | `kg`, `g`, `t` |
| `IfcTimeMeasure` | `s`, `min`, `h`, `d` |
| `IfcThermodynamicTemperatureMeasure` | `K`, `degC` |
| `IfcPowerMeasure` | `W`, `kW` |
| `IfcPressureMeasure` | `Pa`, `kPa`, `MPa` |
| `IfcForceMeasure` | `N`, `kN` |
| `IfcThermalTransmittanceMeasure` | `W/m2K` |
| every other measure of the IDS unit table | its SI unit without spaces: `kg/m3`, `m3/s`, `m/s`, `lx`, `Hz` |

Counts, ratios and plain numbers (`IfcCountMeasure`, `IfcRatioMeasure`, `IfcReal`, `IfcInteger`) have no unit.

## 6. Examples

The file header (`ifc IFC4`) is left out of the fragments. The full file is [examples/showcase.lintel](examples/showcase.lintel).

**1. A property must be present.**

```lintel fragment
rule "Walls state whether they are load-bearing"
  for IfcWall
  require property Pset_WallCommon.LoadBearing
```

**2. Attributes.** Each requirement has its own statement.

```lintel fragment
rule R2 "Spaces are named"
  for IfcSpace
  require attribute Name
  require attribute LongName
```

**3. A predefined type and a quantity range with units.**

```lintel fragment
rule R3 "Floor slabs are 150 to 400 mm thick"
  for IfcSlab type FLOOR
  require property Qto_SlabBaseQuantities.Width between 150 mm and 400 mm
```

**4. Selection by classification, material from a list.**

```lintel fragment
rule R4 "Structural systems are concrete or masonry"
  for any
  where classification "Uniclass 2015" matches "Ss_25.*"
  require material in ["Concrete", "Masonry"]
```

**5. A relationship.**

```lintel fragment
rule R5 "Doors are located in a storey"
  for IfcDoor
  require part of IfcBuildingStorey via IfcRelContainedInSpatialStructure
```

**6. Optional: checked only when present.** A door without the property passes.

```lintel fragment
rule R6 "Acoustic rating of doors"
  for IfcDoor
  optional property Pset_DoorCommon.AcousticRating in ["Rw 32 dB", "Rw 37 dB", "Rw 42 dB"]
```

**7. Prohibited information.**

```lintel fragment
rule R7 "No asbestos"
  for IfcElement
  prohibit material matches ".*[Aa]sbest.*"
```

**8. Two conditions, an upper bound with a unit.**

```lintel fragment
rule R8 "U-value of external load-bearing walls"
  for IfcWall
  where property Pset_WallCommon.IsExternal = true
    and property Pset_WallCommon.LoadBearing = true
  require property Pset_WallCommon.ThermalTransmittance <= 0.24 W/m2K
```

**9. Nothing of this kind may exist.**

```lintel fragment
rule R9 "No proxies"
  for IfcBuildingElementProxy
  require count = 0
```

**10. At least one must exist, and each must be named.**

```lintel fragment
rule R10 "The model has named storeys"
  for IfcBuildingStorey
  require count >= 1
  require attribute Name
```

**11. A named list, several classes, a custom property set.**

```lintel fragment
define Status = ["New", "Existing", "Demolish", "Temporary"]

rule R11 "Status of load-bearing elements"
  for IfcWall, IfcColumn, IfcBeam, IfcSlab
  require property ACME_Common.Status in Status

rule R12 "Status of openings"
  for IfcDoor, IfcWindow
  require property ACME_Common.Status in Status
```

**12. A custom property with a measure.** The data type is written, so the unit can be checked.

```lintel fragment
rule R13 "Clear height of escape routes"
  for IfcSpace
  where property ACME_Fire.EscapeRoute = true
  require property ACME_Fire.ClearHeight datatype IfcLengthMeasure >= 2.2 m
```

**13. An exact class and another IFC version.**

```lintel fragment
rule R14 "Standard walls carry a reference"
  ifc IFC2X3
  for only IfcWallStandardCase
  require property Pset_WallCommon.Reference
```

**14. Several terms, a note for the reader, instructions for the modeller, a link to a dictionary.**

```lintel fragment
rule R15 "Door numbers"
  for IfcDoor
  require attribute Name matches "D[0-9]{3}" note "D and three digits"
    instructions "Take the number from the door schedule."
  require attribute Tag length <= 12
  require property Pset_DoorCommon.FireRating
    uri "https://identifier.buildingsmart.org/uri/buildingsmart/ifc/4.3/prop/FireRating"
```

**15. Selection without a class, a requirement on the class.**

```lintel fragment
rule R16 "The survey point is a proxy"
  for any
  where attribute Name = "Nullpunkt"
  require entity IfcBuildingElementProxy
```

## 7. Mapping to IDS 1.0

| Lintel | IDS 1.0 |
| --- | --- |
| `title`, `description`, `version`, `author`, `date`, `purpose`, `milestone`, `copyright` | `ids/info/*` |
| `ifc ...` in the header or in a rule | `specification/@ifcVersion` |
| `rule ID "name"` | `specification/@identifier`, `specification/@name` |
| `description` in a rule | `specification/@description` |
| `instructions` in a rule | `specification/@instructions` |
| `source` | `specification/requirements/@description` |
| `for C` | `applicability/entity/name`, an `xs:enumeration` of `C` and its subtypes, or a `simpleValue` when there is one class |
| `for only C` | `applicability/entity/name` with exactly the classes written |
| `for matches "..."` | `applicability/entity/name` with `xs:pattern` |
| `type T` | `entity/predefinedType` |
| `for any` | an applicability without `entity` |
| `where F and F` | the other facets of `applicability` |
| no `count` | `applicability minOccurs="0" maxOccurs="unbounded"` |
| `require count >= 1` | `applicability minOccurs="1" maxOccurs="unbounded"` |
| `require count = 0` | `applicability minOccurs="0" maxOccurs="0"` |
| `require F`, `optional F`, `prohibit F` | a facet in `requirements` with `@cardinality` `required`, `optional`, `prohibited` |
| `entity C type T` | `entity/name`, `entity/predefinedType` |
| `attribute N c` | `attribute/name`, `attribute/value` |
| `property S.N datatype D c` | `property/propertySet`, `property/baseName`, `property/@dataType`, `property/value` |
| `classification S c` | `classification/system`, `classification/value` |
| `material c` | `material/value` |
| `part of E via R` | `partOf/entity`, `partOf/@relation` |
| `= v` | `simpleValue` |
| `in [...]`, `in Name`, `[A, B]` in a name position | `xs:restriction` with `xs:enumeration` |
| `matches "..."`, `matches ["...", "..."]` | `xs:restriction` with one `xs:pattern` for each pattern |
| `>`, `>=`, `<`, `<=` | `xs:minExclusive`, `xs:minInclusive`, `xs:maxExclusive`, `xs:maxInclusive` |
| `between a and b` | `xs:minInclusive` and `xs:maxInclusive` |
| `length = n`, `length >= n`, `length <= n` | `xs:length`, `xs:minLength`, `xs:maxLength` |
| `note "..."` | `xs:restriction/xs:annotation/xs:documentation` |
| `uri "..."` after a requirement | `@uri` of the facet (`property`, `classification`, `material`) |
| `instructions "..."` after a requirement | `@instructions` of the facet |
| `150 mm` | the number in the SI unit of the data type: `0.15` |
| `define`, `import`, comments | nothing: lists are written out, comments are dropped |

The `base` of an `xs:restriction` follows from the data type as in the IDS data type table
(`xs:string`, `xs:double`, `xs:integer`, `xs:boolean`, `xs:date`, ...).

**What round trips.** IDS to Lintel to IDS gives the same IDS, compared as canonical XML, for every file that the
official audit accepts. The exceptions are constructs that no IDS file in the official repository uses;
the importer reports each of them (`W701`):

- a restriction on a name (class, predefined type, attribute, property set, property, classification system)
  other than a list or patterns: a length, a bound, or a list together with a pattern;
- a base type other than `xs:string` for a name, a classification code or a material;
- an annotation on a single value of a list (Lintel has `note` for a whole constraint);
- a property with a value and no data type, which the audit lets pass only when an empty text meets the value;
- a value of an attribute whose XML type the IFC schema leaves open, when the file chooses another type than Lintel would;
- a file that declares another version of IDS than 1.0, or none: it is read as IDS 1.0.

The importer checks itself: it compiles what it has written and compares the IDS with the one it read.
Without a `W701` note the file comes back unchanged.

Lintel to IDS to Lintel gives a file with the same meaning: the names of lists, comments, the units as written and
the short form of a class with its subtypes are not stored in IDS.
The importer writes a class list as `C` when it is exactly `C` with its subtypes, and as `only ...` otherwise.

## 8. Checks of the compiler

The judge of what a valid IDS file is, is the official IDS audit of buildingSMART
([IDS-Audit-tool](https://github.com/buildingSMART/IDS-Audit-tool), the library `ids-lib`). The compiler is built on it:

1. **The compiler never writes an IDS file that the audit rejects.** The audit runs as the last check on the IDS
   that the file compiles to. What it rejects is an error at the place of the source it comes from.
2. **An error means: the audit rejects this**, or it breaks a rule of the language itself (a measure carries its unit,
   a value is written as its kind, one canonical spelling). For most of these the compiler has a message of its own,
   with a hint, the allowed values and a fix; the rest is reported with the words of the audit (`E499`).
3. **What the audit accepts is not an error.** Where it is probably not what the author means, the compiler warns.
   IDS is produced; `fixed` has the repaired source when there is a repair.

The checks that need the IFC schema follow the audit, which works with sets of classes: every facet stands for the
classes that could meet it, and a rule is invalid when no class can meet all its conditions and required
requirements at once.

- A class exists in at least one IFC version of the rule, and every version has at least one of the selected classes.
- A predefined type is possible for every selected class ([section 5.4](#54-classes-and-subtypes)).
- An attribute exists in the schema, and a selected class has it. With a constraint it holds a single value.
- A property set named `Pset_...` is a set of the standard, its property is one of its properties, and the set is
  made for the selected classes. `datatype` is the type the standard gives the property.
- With `via`, the whole and the part are of the classes that the relationship connects.
- A class in `require entity` is among the selected classes.
- Values fit their data type; an enumerated property of the standard takes one of the listed values.

IDS checks property sets, attributes on the selected classes and `part of` only in a requirement that must be met.
In `optional` and `prohibit`, a name that the schema does not have is valid IDS that checks nothing; the compiler
warns (`W511`). The same holds for the prefix `Qto_`, which IDS does not reserve, and for one unknown name in a list.

## 9. Diagnostics

The compiler reports problems as data. Lines and columns start at 1; the end position is the first character after the range.

```json
{
  "ok": false,
  "diagnostics": [
    {
      "severity": "error",
      "code": "E404",
      "name": "unknown-property",
      "message": "Pset_WallCommon has no property FireRatin in IFC4.",
      "line": 6, "column": 36, "endLine": 6, "endColumn": 45,
      "rule": "R1",
      "hint": "Did you mean 'FireRating'?",
      "expected": ["AcousticRating", "Combustible", "FireRating", "IsExternal", "LoadBearing", "Reference", "ThermalTransmittance"],
      "fix": [ { "line": 6, "column": 36, "endLine": 6, "endColumn": 45, "text": "FireRating" } ]
    }
  ],
  "fixed": "ifc IFC4\n\nrule R1 ...",
  "fixedOk": true
}
```

| Field | Meaning |
| --- | --- |
| `severity` | `error`: no IDS is produced. `warning`: IDS is produced, but the author probably means something else or uses a spelling that is not the canonical one |
| `code`, `name` | Stable identifiers. The code never changes its meaning |
| `message` | One sentence that says what is wrong |
| `line`, `column`, `endLine`, `endColumn` | The range in the source |
| `rule` | The identifier or the name of the rule, when the problem is inside one |
| `hint` | What to do |
| `expected` | The values allowed at this place, at most 30 |
| `fix` | Edits that repair the problem. Present only when the repair cannot change the meaning the author intended |
| `fixed` | The whole source with all fixes applied, when there are fixes |
| `fixedOk` | Whether `fixed` is free of errors |

Codes are grouped by the stage that finds the problem:

| Range | Stage | Examples |
| --- | --- | --- |
| `E1xx` | Characters | `E101` unterminated-string, `E102` unexpected-character, `E103` malformed-number, `E104` unterminated-text-block |
| `E2xx` | Grammar | `E201` unexpected-token, `E202` unknown-keyword, `E203` misplaced-statement, `E204` duplicate-statement, `E205` missing-for, `E206` unsupported-operator, `E207` one-facet-per-requirement, `E208` unsupported-phrase, `E209` missing-facet-keyword, `E210` pattern-in-slashes, `E211` reserved-word |
| `E3xx` | Structure, no IFC schema needed | `E302` invalid-pattern, `E303` invalid-cardinality, `E304` unsupported-count, `E305` requirement-not-allowed, `E306` missing-requirement, `E307` missing-where, `E308` unknown-list, `E309` duplicate-definition, `E310` invalid-ifc-version, `E311` missing-ifc-version, `E312` invalid-list, `E313` invalid-metadata, `E314` invalid-length, `E315` uri-not-allowed, `E316` invalid-relation, `E317` empty-match |
| `E4xx` | IFC schema | `E401` unknown-class, `E402` unknown-attribute, `E403` unknown-property-set, `E404` unknown-property, `E405` unknown-predefined-type, `E406` unknown-datatype, `E407` datatype-mismatch, `E408` value-type-mismatch, `E409` unit-required, `E410` unknown-unit, `E411` property-set-not-applicable, `E412` class-not-in-version, `E413` datatype-required, `E414` incompatible-entity, `E415` invalid-relation-target, `E416` no-common-class |
| `E499` | The official IDS audit | `E499` rejected-by-ids-audit: the audit rejects the result for a reason that no other code covers; the message is the audit's |
| `W5xx` | Warnings | `W501` predefined-type-as-attribute, `W502` quoted-name, `W503` double-backslash, `W504` range-as-two-bounds, `W505` duplicate-identifier, `W506` non-portable-escape, `W507` inferred-number-type, `W508` abstract-class, `W509` anchored-pattern, `W510` non-standard-pattern, `W511` not-in-schema, `W512` pattern-on-number, `W513` odd-range |
| `W7xx` | Import of IDS | `W701` ids-not-representable |

`E301` and the old meaning of `E314` (an empty range) are not used any more: IDS accepts an anchored pattern and an
empty range, so they are the warnings `W509` and `W513`.

Spellings that authors are known to try get their own message and fix. Examples:
`==` for `=`; `^...$` in a pattern (a warning, with the fix); `forbid` or `must not` for `prohibit`; `requirement` or `spec` for `rule`;
`applies to` for `for`; `schema IFC4` for `ifc IFC4`; `/regex/` for `"regex"`;
`where PredefinedType = FLOOR` for `type FLOOR`; `require Pset_WallCommon.FireRating` without `property`.

## 10. Reserved words

Keywords of v0.1:

`and` `any` `attribute` `author` `between` `classification` `copyright` `count` `datatype` `date` `define`
`description` `entity` `false` `for` `ifc` `import` `in` `instructions` `length` `matches` `material` `milestone`
`note` `of` `only` `optional` `part` `prohibit` `property` `purpose` `require` `rule` `source` `title` `true`
`type` `uri` `version` `via` `where`

Reserved for later levels, not usable as names: `or` `not` `sum` `unique` `each` `exists` `if` `then` `else`
`fix` `set` `warn`.

Level 2 (rules between elements, `count` with other numbers, `sum`, `unique`, comparison of two properties) and
level 3 (`fix` and `set`, for the Revit adapter only) are not part of v0.1. They will never be written to IDS:
IDS forbids proprietary extensions. A file without `fix` changes nothing in a model.

## 11. Decisions and open questions

Decided:

1. **Subtypes by default** ([5.4](#54-classes-and-subtypes)): a class name selects the class and its subtypes;
   `only` gives the exact meaning of IDS. 6 of 8 language models assumed this in the syntax experiment, and a missed
   class is a silent error.
2. **Units are mandatory** for measures ([5.8](#58-units)); the compiler converts to the SI unit that IDS stores.
3. **`source`** is stored in `requirements/@description`. IDS has no field for the original wording of a requirement.
4. **The official audit is the judge** of validity ([8](#8-checks-of-the-compiler)): an error where it rejects,
   a warning at most where it accepts.

Open:

1. **Name of the language** and the file extension (`.lintel` here).
2. **`import`** brings lists only. Rule sets are combined by compiling several files.
3. **Imperial units** are missing; `in` is a keyword.
4. **Restrictions on names** other than a list or patterns, and the other limits of the round trip
   ([7](#7-mapping-to-ids-10)), are reported and not kept. No official IDS file uses them.
5. **Warnings and the agent**: a warning does not stop the compiler. Whether a tool for agents should refuse to
   write IDS while a warning has a fix is a decision for the tool, not for the language.
