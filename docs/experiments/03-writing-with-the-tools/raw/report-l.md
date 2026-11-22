Report (Lintel usability test, folder <folder>)

1. Coverage
- 1: R01. `for IfcBuildingStorey`, `require count >= 1` plus `require attribute Name` in one rule, as the guide's example R10 does.
- 2: R02. `Qto_BuildingStoreyBaseQuantities.NetHeigtht` is required to be present. The spelling is the server's, see 2b.
- 3: R03. `Pset_DoorCommon.FireRating matches "(T|EI)[0-9]{2,3}"`.
- 4: R04. `attribute OverallHeight >= 2.10 m`.
- 5: R05. `IfcWall where Pset_WallCommon.IsExternal = true`, `require ThermalTransmittance <= 0.24 W/m2K`.
- 6: R06. `Pset_WallCommon.Status in ["NEW","EXISTING"]`.
- 7: R07. `AG_Wand.Schallschutzklasse in ["SSK1","SSK2","SSK3"]`.
- 8: R08. `IfcSlab type BASESLAB`, `require Pset_SlabCommon.LoadBearing = true`.
- 9: R09. `Qto_SpaceBaseQuantities.NetFloorArea > 0 m2`.
- 10: not expressible. It needs a sum over several elements compared with another value. It is left out with a `#` comment in the .lintel file.
- 11: R11. `IfcBuildingElementProxy`, `require count = 0`.
- 12: R12. `IfcColumn where Pset_ColumnCommon.LoadBearing = true`, `require material in ["Stahlbeton","Stahl"]`.

2. Problems and guesses
a) The language guide links to SPEC.md as "the full specification", but no tool serves it. An agent limited to the tools cannot follow that link.
b) `lintel_list_properties` gives `Qto_BuildingStoreyBaseQuantities.NetHeigtht` for IFC4, with "tht" at the end. The same lookup for IFC4X3_ADD2 gives `NetHeight`. The validator rejects `NetHeight` for IFC4 with E404 and offers `NetHeigtht` as the fix (`fixedOk` true). I followed the server because its instructions say not to write names from memory. I cannot confirm from here whether IFC4 really has this typo. If it does not, the server pushes a misspelling into the IDS and nothing warns about it. If it does, the tool should say it is a known typo of the standard, because it looks like an error in the tool. Either way it was a guess.
c) There is no information on how to set the IDS `<info>`: title, author, version, date. The compiled file has `<title>Untitled</title>`. The guide lists no file-level header keyword.
d) Comments for requirements that IDS cannot express are lost when compiling. The IDS that the person reviews and signs says nothing about requirement 10. The instructions say "say so in a comment", but there is no way to carry that note into the IDS.
e) The guide says "one rule for one requirement" and "do not merge". Requirement 1 has two sentences: storeys are named, and at least one storey exists. I put both in one rule, following the guide's example R10. The guide does not say whether two sentences of one numbered requirement count as one requirement.
f) Mapping "neu"/"Bestand" to the IFC enumeration NEW/EXISTING was my decision. The tools give the allowed English values but say nothing about translating requirement wording into enumeration values.
g) Requirement 5 says "dürfen höchstens", which may mean "only limited if given" or "required and limited". I used `require`, so the value must be present. `optional` would also be defensible. The guide does not discuss this.
h) Requirement 4: I chose the attribute `IfcDoor.OverallHeight` for "Gesamthöhe" over `Qto_DoorBaseQuantities.Height`. The tools list both without saying which one is meant for the overall height.
i) Requirement 8: I assumed Bodenplatte = predefined type BASESLAB. The tools give the type names but no descriptions of them.
j) Small points:
   - `note` on a constraint compiles into `xs:annotation`/`xs:documentation`. The guide does not say what the IDS reader sees of it.
   - `source` is written to `requirements@description`, not documented.
   - The guide does not say that a `where` condition makes R05 and R12 skip walls and columns that lack IsExternal or LoadBearing entirely.
   - `lintel_list_properties` with 'class' lists property names but not types. A second call is needed for types, which the tool does say.

3. Final result
The final `lintel_validate_rules` on rules.lintel returned ok: true with 0 errors, 0 warnings and no diagnostics. rules.ids was written by `lintel_compile_rules`: IDS 1.0 with 11 specifications.

Files are in <folder>:
- rules.lintel
- rules.ids
