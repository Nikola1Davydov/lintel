(a)

```lintel
ifc IFC4

define Exposure = ["XC1", "XC2", "XC3", "XC4"]

rule "4.1.1" "Room number format"
  source "4.1.1 Alle Räume müssen eine Raumnummer (Attribut Name) im Format „R-“ gefolgt von drei Ziffern haben, z. B. R-101."
  for IfcSpace
  require attribute Name matches "R-[0-9]{3}" note "R- and three digits, e.g. R-101"

rule "4.1.2" "Room net floor area above zero"
  source "4.1.2 Jeder Raum hat eine Nettogrundfläche größer als 0 m² (Mengensatz Qto_SpaceBaseQuantities, NetFloorArea)."
  for IfcSpace
  require property Qto_SpaceBaseQuantities.NetFloorArea > 0 m2

rule "4.2.1" "Fire resistance class of load-bearing columns"
  source "4.2.1 Tragende Stützen müssen eine Feuerwiderstandsklasse R30, R60, R90 oder R120 haben (Standard-Eigenschaftssatz für Stützen)."
  for IfcColumn
  where property Pset_ColumnCommon.LoadBearing = true
  require property Pset_ColumnCommon.FireRating in ["R30", "R60", "R90", "R120"]

rule "4.2.3" "External doors at least 0.90 m wide"
  source "4.2.3 Außentüren müssen mindestens 0,90 m breit sein (Qto_DoorBaseQuantities, Width)."
  for IfcDoor
  where property Pset_DoorCommon.IsExternal = true
  require property Qto_DoorBaseQuantities.Width >= 0.9 m

rule "4.3.1" "DIN 276 cost group 300"
  source "4.3.1 Alle Wände, Decken und Dächer müssen nach „DIN 276“ klassifiziert sein; der Code beginnt mit 3 (Kostengruppe 300)."
  for IfcWall, IfcSlab, IfcRoof
  require classification "DIN 276" matches "3.*" note "code starts with 3"

rule "4.3.2" "Windows are located in a storey"
  source "4.3.2 Fenster müssen einem Geschoss räumlich zugeordnet sein."
  for IfcWindow
  require part of IfcBuildingStorey via IfcRelContainedInSpatialStructure

rule "4.4.1" "Exposure class of reinforced concrete"
  source "4.4.1 Bauteile aus dem Material „Stahlbeton“ müssen die Eigenschaft Expositionsklasse im Eigenschaftssatz „BIM_Beton“ haben; erlaubt sind XC1, XC2, XC3 und XC4."
  for IfcElement
  where material = "Stahlbeton"
  require property BIM_Beton.Expositionsklasse in Exposure

rule "4.4.2" "Acoustic rating of walls, when given"
  source "4.4.2 Wände müssen kein Schalldämmmaß angeben. Wenn eine Wand aber die Eigenschaft AcousticRating (Pset_WallCommon) hat, muss der Wert dem Muster „Rw NN dB“ mit zwei Ziffern entsprechen, z. B. „Rw 42 dB“."
  for IfcWall
  optional property Pset_WallCommon.AcousticRating matches "Rw [0-9]{2} dB" note "Rw, two digits, dB"

rule "4.5.1" "No placeholder objects"
  source "4.5.1 Das Modell darf keine Platzhalterobjekte (IfcBuildingElementProxy) enthalten."
  for IfcBuildingElementProxy
  require count = 0

rule "4.5.2" "At least one site"
  source "4.5.2 Das Modell muss mindestens ein Grundstück (IfcSite) enthalten."
  for IfcSite
  require count >= 1

rule "4.6.1" "Flat roof slope 2 to 5 degrees"
  source "4.6.1 Flachdächer (Dächer mit dem vordefinierten Typ FLAT_ROOF) müssen eine Dachneigung zwischen 2° und 5° haben (Eigenschaft „Neigung“ im Eigenschaftssatz „BIM_Dach“, ein Winkel)."
  for IfcRoof type FLAT_ROOF
  require property BIM_Dach.Neigung datatype IfcPlaneAngleMeasure between 2 deg and 5 deg

rule "4.6.2" "No internal remark property"
  source "4.6.2 Kein Bauteil darf die Eigenschaft „Bemerkung intern“ im Eigenschaftssatz „BIM_Intern“ tragen."
  for IfcElement
  prohibit property BIM_Intern."Bemerkung intern"

# 4.7.1 not expressible: sum of net floor areas of all office rooms (>= 300 m2); Lintel has no sums.
# "Die Summe der Nettogrundflächen aller Büroräume muss mindestens 300 m² betragen."

# 4.7.2 not expressible: compares door width with the thickness of the host wall (two elements, two properties).
# "Türen dürfen nicht breiter sein, als die Wand, in der sie sitzen, dick ist."
```

(b)
- Not expressible: 4.7.1 (sum over rooms) and 4.7.2 (comparison of door width with host wall thickness). Both are left as comments, as rule 9 says.
- Unclear: how to select "Büroräume" for 4.7.1, and how to pick "Decken" (I used IfcSlab). Ceilings could also be IfcCovering with type CEILING, and the page doesn't say which is intended.
- Unclear: `where material = "Stahlbeton"` is my guess at the material condition syntax. The page only shows `require material ...` and `material in [...]`. It also doesn't say whether `where` accepts `material` and `classification` without a class, apart from the `for any` example.
- Unclear: whether `datatype` goes before the constraint for a standard-named but custom-set property (`BIM_Dach.Neigung`), and whether a `define` list may be used for a single-property check. Also whether a `note` on `matches` is fine.
- Assumed: "tragende" and "Außen" map to `Pset_ColumnCommon.LoadBearing` and `Pset_DoorCommon.IsExternal`, which the page names only for walls. The 4.2.1 requirement doesn't spell this out, so it is a guess.