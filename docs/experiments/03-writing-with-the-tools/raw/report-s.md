REPORT (Lintel IFC4 test, folder <folder>)

Files: <folder>/rules.lintel and <folder>/rules.ids

1. Requirement coverage
1: R1 (storeys named, attribute Name; plus count >= 1 in the same rule)
2: R2 (Qto_BuildingStoreyBaseQuantities.NetHeigtht present)
3: R3 (Pset_DoorCommon.FireRating matches "(T|EI)[0-9]{2,3}")
4: R4 (attribute OverallHeight >= 2.10 m)
5: R5 (IfcWall where Pset_WallCommon.IsExternal = true; ThermalTransmittance <= 0.24 W/m2K)
6: R6 (Pset_WallCommon.Status in ["NEW", "EXISTING"])
7: R7 (AG_Wand.Schallschutzklasse in ["SSK1", "SSK2", "SSK3"])
8: R8 (IfcSlab type BASESLAB, Pset_SlabCommon.LoadBearing = true)
9: R9 (Qto_SpaceBaseQuantities.NetFloorArea > 0 m2)
10: not expressible (sum of space net areas per storey vs. storey gross area; Lintel cannot sum). Left as a comment in rules.lintel. No rule R10 exists.
11: R11 (for IfcBuildingElementProxy, IfcProxy; require count = 0)
12: R12 (IfcColumn where Pset_ColumnCommon.LoadBearing = true; material in ["Stahlbeton", "Stahl"])

2. Unclear, misleading, missing or wrong; places where I guessed
- lintel_list_properties returns "NetHeigtht" for Qto_BuildingStoreyBaseQuantities as the standard name. This looks like a misspelling (IFC4 is, to my knowledge, "NetHeight"). The server presents it without a flag. I used the server's name as instructed; a reviewer should check it against the IFC4 schema.
- Pset_BuildingStoreyCommon.AboveGround is typed IfcLogical but described as "text in quotes". IfcLogical takes true/false/unknown, so the description is wrong.
- lintel_language_guide gives a unit list (m cm mm, m2 ..., W, Pa, ...) that omits W/m2K, yet its own example uses 0.24 W/m2K and the validator and list_properties both accept W/m2K. The unit list should include it.
- Guide and server have no guidance for German enumeration values. Requirement 6 names "neu" and "Bestand"; I mapped them to the IFC values NEW and EXISTING and noted the mapping in a comment. This is my decision and needs review.
- Guide has no statement on what happens to elements that lack the property used in a where condition (e.g. walls without IsExternal). They are silently not selected. I accepted this for R5 and R12; a reviewer should decide.
- Requirement 8: I read "Bodenplatten (Fundamentplatten)" as IfcSlab type BASESLAB, not FLOOR. Decision, noted in a comment.
- Requirement 1 has two sentences. I kept them in one rule (count >= 1 plus attribute Name), because the guide's example does the same. The guide's "one rule per requirement" rule is ambiguous here.
- Requirement 11: IfcProxy lists no property sets and no predefined types, so I used one rule covering both IfcBuildingElementProxy and IfcProxy. Decision.
- lintel_list_properties search "Schallschutz" returns nothing (expected: custom set). The response is correct, but it gives no hint that custom sets such as AG_Wand need no lookup.
- The rules are written in German in 'source' text; the guide does not say whether that is intended. I assumed yes.

3. Final validation and output
- lintel_validate_rules (path rules.lintel, run once after the only write): ok true, 0 errors, 0 warnings, no diagnostics. No fixes were needed.
- lintel_compile_rules wrote rules.ids: IDS 1.0 with 11 specifications, 11377 characters. Spot checks of the IDS (enumerations, bounds 2.10 and 0.24, BASESLAB, NetFloorArea minExclusive 0) match the rules.
