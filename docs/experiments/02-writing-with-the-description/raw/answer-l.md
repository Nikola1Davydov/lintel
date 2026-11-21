```lintel
ifc IFC4

rule "4.1.1" "Space number format"
  source "4.1.1 Alle Räume müssen eine Raumnummer (Attribut Name) im Format „R-“ gefolgt von drei Ziffern haben, z. B. R-101."
  for IfcSpace
  require attribute Name matches "R-[0-9]{3}" note "R- and three digits"

rule "4.1.2" "Spaces have a net floor area"
  source "4.1.2 Jeder Raum hat eine Nettogrundfläche größer als 0 m² (Mengensatz Qto_SpaceBaseQuantities, NetFloorArea)."
  for IfcSpace
  require property Qto_SpaceBaseQuantities.NetFloorArea > 0 m2

rule "4.2.1" "Fire rating of load-bearing columns"
  source "4.2.1 Tragende Stützen müssen eine Feuerwiderstandsklasse R30, R60, R90 oder R120 haben (Standard-Eigenschaftssatz für Stützen)."
  for IfcColumn
  where property Pset_ColumnCommon.LoadBearing = true
  require property Pset_ColumnCommon.FireRating in ["R30", "R60", "R90", "R120"]

rule "4.2.3" "External doors are at least 0.90 m wide"
  source "4.2.3 Außentüren müssen mindestens 0,90 m breit sein (Qto_DoorBaseQuantities, Width)."
  for IfcDoor
  where property Pset_DoorCommon.IsExternal = true
  require property Qto_DoorBaseQuantities.Width >= 0.9 m

rule "4.3.1" "Walls, slabs and roofs classified to DIN 276 cost group 300"
  source "4.3.1 Alle Wände, Decken und Dächer müssen nach „DIN 276“ klassifiziert sein; der Code beginnt mit 3 (Kostengruppe 300)."
  for IfcWall, IfcSlab, IfcRoof
  require classification "DIN 276" matches "3.*" note "code starts with 3"

rule "4.3.2" "Windows are located in a storey"
  source "4.3.2 Fenster müssen einem Geschoss räumlich zugeordnet sein."
  for IfcWindow
  require part of IfcBuildingStorey via IfcRelContainedInSpatialStructure

rule "4.4.1" "Exposure class of reinforced concrete elements"
  source "4.4.1 Bauteile aus dem Material „Stahlbeton“ müssen die Eigenschaft Expositionsklasse im Eigenschaftssatz „BIM_Beton“ haben; erlaubt sind XC1, XC2, XC3 und XC4."
  for any
  where material = "Stahlbeton"
  require property BIM_Beton.Expositionsklasse in ["XC1", "XC2", "XC3", "XC4"]

rule "4.4.2" "Acoustic rating format of walls, when given"
  source "4.4.2 Wände müssen kein Schalldämmmaß angeben. Wenn eine Wand aber die Eigenschaft AcousticRating (Pset_WallCommon) hat, muss der Wert dem Muster „Rw NN dB“ mit zwei Ziffern entsprechen, z. B. „Rw 42 dB“."
  for IfcWall
  optional property Pset_WallCommon.AcousticRating matches "Rw [0-9]{2} dB"

rule "4.5.1" "No proxy objects"
  source "4.5.1 Das Modell darf keine Platzhalterobjekte (IfcBuildingElementProxy) enthalten."
  for IfcBuildingElementProxy
  require count = 0

rule "4.5.2" "The model has a site"
  source "4.5.2 Das Modell muss mindestens ein Grundstück (IfcSite) enthalten."
  for IfcSite
  require count >= 1

rule "4.6.1" "Pitch of flat roofs between 2 and 5 degrees"
  source "4.6.1 Flachdächer (Dächer mit dem vordefinierten Typ FLAT_ROOF) müssen eine Dachneigung zwischen 2° und 5° haben (Eigenschaft „Neigung“ im Eigenschaftssatz „BIM_Dach“, ein Winkel)."
  for IfcRoof type FLAT_ROOF
  require property BIM_Dach.Neigung datatype IfcPlaneAngleMeasure between 2 deg and 5 deg

rule "4.6.2" "No internal remarks"
  source "4.6.2 Kein Bauteil darf die Eigenschaft „Bemerkung intern“ im Eigenschaftssatz „BIM_Intern“ tragen."
  for IfcElement
  prohibit property BIM_Intern."Bemerkung intern"

# 4.7.1 "Die Summe der Nettogrundflächen aller Büroräume muss mindestens 300 m² betragen."
#   Not expressible: Lintel/IDS has no sums over elements.

# 4.7.2 "Türen dürfen nicht breiter sein, als die Wand, in der sie sitzen, dick ist."
#   Not expressible: compares properties of two different elements (door width vs. host wall thickness).
```

- Could not express 4.7.1 (needs a sum over elements) or 4.7.2 (compares a door with its host wall). Both are left out with a comment, as rule 9 says. There is also no way to say which spaces are "Büroräume".
- The page doesn't say whether an ID like `4.1.1` must be quoted. I quoted it because a dotted word didn't seem to count as a "plain word".
- 4.3.1: I don't know if "Decken" should be all `IfcSlab` or only `IfcSlab type FLOOR`/`ROOF`. The page also doesn't say whether `type` can be used inside a list of several classes. I used all of `IfcSlab`. `IfcRoof` may select the roof container rather than its slabs.
- 4.4.1: The page doesn't explain how `material = "Stahlbeton"` matches an element with several materials or layers. I used `for any` with that `where`, and I'm not sure that counts as a valid selector.
- 4.6.1: The page doesn't say whether `between ... deg` works for a custom set, or which degree/radian unit IDS uses to compare. I wrote `datatype IfcPlaneAngleMeasure` because of the measure note. 4.2.1/4.2.3 rely on `Pset_ColumnCommon.LoadBearing` and `Pset_DoorCommon.IsExternal`, which the requirements imply but don't name.