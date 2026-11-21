```lintel
ifc IFC4

# AIA (excerpt), sections 4.1 to 4.7. The rule ID is the paragraph number of the AIA.

rule "4.1.1" "Room number format"
  source "Alle Räume müssen eine Raumnummer (Attribut Name) im Format „R-“ gefolgt von drei Ziffern haben, z. B. R-101."
  for IfcSpace
  require attribute Name matches "R-[0-9]{3}" note "R- and three digits, e.g. R-101"

rule "4.1.2" "Net floor area of spaces"
  source "Jeder Raum hat eine Nettogrundfläche größer als 0 m² (Mengensatz Qto_SpaceBaseQuantities, NetFloorArea)."
  for IfcSpace
  require property Qto_SpaceBaseQuantities.NetFloorArea > 0 m2

rule "4.2.1" "Fire rating of load-bearing columns"
  source "Tragende Stützen müssen eine Feuerwiderstandsklasse R30, R60, R90 oder R120 haben (Standard-Eigenschaftssatz für Stützen)."
  for IfcColumn
  where property Pset_ColumnCommon.LoadBearing = true
  require property Pset_ColumnCommon.FireRating in ["R30", "R60", "R90", "R120"]

rule "4.2.3" "Minimum width of external doors"
  source "Außentüren müssen mindestens 0,90 m breit sein (Qto_DoorBaseQuantities, Width)."
  for IfcDoor
  where property Pset_DoorCommon.IsExternal = true
  require property Qto_DoorBaseQuantities.Width >= 0.9 m

rule "4.3.1" "DIN 276 classification of walls, slabs and roofs"
  source "Alle Wände, Decken und Dächer müssen nach „DIN 276“ klassifiziert sein; der Code beginnt mit 3 (Kostengruppe 300)."
  for IfcWall, IfcSlab, IfcRoof
  require classification "DIN 276" matches "3.*" note "the code begins with 3 (cost group 300)"

rule "4.3.2" "Windows are located in a storey"
  source "Fenster müssen einem Geschoss räumlich zugeordnet sein."
  for IfcWindow
  require part of IfcBuildingStorey via IfcRelContainedInSpatialStructure

rule "4.4.1" "Exposure class of reinforced concrete elements"
  source "Bauteile aus dem Material „Stahlbeton“ müssen die Eigenschaft Expositionsklasse im Eigenschaftssatz „BIM_Beton“ haben; erlaubt sind XC1, XC2, XC3 und XC4."
  for IfcElement
  where material = "Stahlbeton"
  require property BIM_Beton.Expositionsklasse in ["XC1", "XC2", "XC3", "XC4"]

rule "4.4.2" "Acoustic rating of walls, when given"
  source "Wände müssen kein Schalldämmmaß angeben. Wenn eine Wand aber die Eigenschaft AcousticRating (Pset_WallCommon) hat, muss der Wert dem Muster „Rw NN dB“ mit zwei Ziffern entsprechen, z. B. „Rw 42 dB“."
  for IfcWall
  optional property Pset_WallCommon.AcousticRating matches "Rw [0-9]{2} dB" note "Rw NN dB with two digits, e.g. Rw 42 dB"

rule "4.5.1" "No proxy elements"
  source "Das Modell darf keine Platzhalterobjekte (IfcBuildingElementProxy) enthalten."
  for IfcBuildingElementProxy
  require count = 0

rule "4.5.2" "The model has a site"
  source "Das Modell muss mindestens ein Grundstück (IfcSite) enthalten."
  for IfcSite
  require count >= 1

rule "4.6.1" "Pitch of flat roofs"
  source "Flachdächer (Dächer mit dem vordefinierten Typ FLAT_ROOF) müssen eine Dachneigung zwischen 2° und 5° haben (Eigenschaft „Neigung“ im Eigenschaftssatz „BIM_Dach“, ein Winkel)."
  for IfcRoof type FLAT_ROOF
  require property BIM_Dach.Neigung datatype IfcPlaneAngleMeasure between 2 deg and 5 deg

rule "4.6.2" "No internal remarks"
  source "Kein Bauteil darf die Eigenschaft „Bemerkung intern“ im Eigenschaftssatz „BIM_Intern“ tragen."
  for IfcElement
  prohibit property "BIM_Intern"."Bemerkung intern"

# 4.7.1 Die Summe der Nettogrundflächen aller Büroräume muss mindestens 300 m² betragen.
#   Left out: Lintel cannot express a sum over several elements.

# 4.7.2 Türen dürfen nicht breiter sein, als die Wand, in der sie sitzen, dick ist.
#   Left out: Lintel cannot compare a property of a door with a property of its wall (two elements).
```

- **4.7.1 left out** (comment in the file): a sum over several elements cannot be expressed (rule 9). The AIA also gives no criterion for "Büroräume".
- **4.7.2 left out** (comment in the file): it compares a door's width with the thickness of its host wall, i.e. properties of two elements; `part of` could only state the door-in-wall relation.
- **Unclear:** may `require count >= 1` be a rule's only requirement (4.5.2)? The page says "no other requirement" only for `count = 0`, and its example R10 adds a second one.
- **Missing, strings and names:** how to write `"` inside a string, and whether typographic quotes („…“), `°` and `²` are safe in `source` (kept word for word). Also whether only one of the two property names may be quoted (`BIM_Intern."Bemerkung intern"`); I quoted both, as in the page's example.
- **Missing, data types and mappings:** the page gives no list of valid `datatype` names (I assumed `IfcPlaneAngleMeasure` for 4.6.1). Mappings I assumed because the text does not fix them: Außentüren = `Pset_DoorCommon.IsExternal = true`, Decken = `IfcSlab`, Bauteile = `IfcElement` rather than `for any`.