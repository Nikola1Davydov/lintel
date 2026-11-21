(a) Lintel file:

```lintel
ifc IFC4

rule "4.1.1" "Room numbers"
  source "AIA 4.1.1: Alle Räume müssen eine Raumnummer (Attribut Name) im Format „R-“ gefolgt von drei Ziffern haben, z. B. R-101."
  for IfcSpace
  require attribute Name matches "R-[0-9]{3}"

rule "4.1.2" "Net floor area of rooms"
  source "AIA 4.1.2: Jeder Raum hat eine Nettogrundfläche größer als 0 m² (Mengensatz Qto_SpaceBaseQuantities, NetFloorArea)."
  for IfcSpace
  require property Qto_SpaceBaseQuantities.NetFloorArea > 0 m2

rule "4.2.1" "Fire rating of load-bearing columns"
  source "AIA 4.2.1: Tragende Stützen müssen eine Feuerwiderstandsklasse R30, R60, R90 oder R120 haben (Standard-Eigenschaftssatz für Stützen)."
  for IfcColumn
  where property Pset_ColumnCommon.LoadBearing = true
  require property Pset_ColumnCommon.FireRating in ["R30", "R60", "R90", "R120"]

rule "4.2.3" "Width of external doors"
  source "AIA 4.2.3: Außentüren müssen mindestens 0,90 m breit sein (Qto_DoorBaseQuantities, Width)."
  for IfcDoor
  where property Pset_DoorCommon.IsExternal = true
  require property Qto_DoorBaseQuantities.Width >= 0.90 m

rule "4.3.1" "DIN 276 classification"
  source "AIA 4.3.1: Alle Wände, Decken und Dächer müssen nach „DIN 276“ klassifiziert sein; der Code beginnt mit 3 (Kostengruppe 300)."
  for IfcWall, IfcSlab, IfcRoof
  require classification "DIN 276" matches "3.*"

rule "4.3.2" "Windows assigned to a storey"
  source "AIA 4.3.2: Fenster müssen einem Geschoss räumlich zugeordnet sein."
  for IfcWindow
  require part of IfcBuildingStorey via IfcRelContainedInSpatialStructure

rule "4.4.1" "Exposure class of reinforced concrete"
  source "AIA 4.4.1: Bauteile aus dem Material „Stahlbeton“ müssen die Eigenschaft Expositionsklasse im Eigenschaftssatz „BIM_Beton“ haben; erlaubt sind XC1, XC2, XC3 und XC4."
  for any
  where material = "Stahlbeton"
  require property BIM_Beton.Expositionsklasse in ["XC1", "XC2", "XC3", "XC4"]

rule "4.4.2" "Acoustic rating of walls, when given"
  source "AIA 4.4.2: Wände müssen kein Schalldämmmaß angeben. Wenn eine Wand aber die Eigenschaft AcousticRating (Pset_WallCommon) hat, muss der Wert dem Muster „Rw NN dB“ mit zwei Ziffern entsprechen, z. B. „Rw 42 dB“."
  for IfcWall
  optional property Pset_WallCommon.AcousticRating matches "Rw [0-9]{2} dB"

rule "4.5.1" "No placeholder objects"
  source "AIA 4.5.1: Das Modell darf keine Platzhalterobjekte (IfcBuildingElementProxy) enthalten."
  for IfcBuildingElementProxy
  require count = 0

rule "4.5.2" "Site present"
  source "AIA 4.5.2: Das Modell muss mindestens ein Grundstück (IfcSite) enthalten."
  for IfcSite
  require count >= 1

rule "4.6.1" "Slope of flat roofs"
  source "AIA 4.6.1: Flachdächer (Dächer mit dem vordefinierten Typ FLAT_ROOF) müssen eine Dachneigung zwischen 2° und 5° haben (Eigenschaft „Neigung“ im Eigenschaftssatz „BIM_Dach“, ein Winkel)."
  for IfcRoof type FLAT_ROOF
  require property BIM_Dach.Neigung datatype IfcPlaneAngleMeasure between 2 deg and 5 deg

rule "4.6.2" "No internal remarks"
  source "AIA 4.6.2: Kein Bauteil darf die Eigenschaft „Bemerkung intern“ im Eigenschaftssatz „BIM_Intern“ tragen."
  for IfcElement
  prohibit property BIM_Intern."Bemerkung intern"

# AIA 4.7.1 not expressed: the sum of the net floor areas of all office rooms (>= 300 m2) is a sum, which Lintel cannot state.
# AIA 4.7.2 not expressed: comparing a door width with the thickness of its wall compares two properties, which Lintel cannot state.
```

(b) Notes:
- 4.7.1 (sum of office room areas) and 4.7.2 (door width vs. wall thickness) are left out and marked as comments in the file: Lintel has no sums and no comparison of two properties.
- Assumed property names not given in the excerpt: `Pset_DoorCommon.IsExternal` marks external doors, and `Pset_ColumnCommon.LoadBearing` marks load-bearing columns.
- 4.4.1 uses the literal material name "Stahlbeton". The description shows `material in [...]` but not whether `material = "..."` is valid, so I assumed it is.
- 4.3.1 selects `IfcSlab` for Decken, but ceilings are often `IfcCovering` in IFC4. The description does not say how ceilings should be handled.
- The description does not say whether the `AIA x.y.z:` prefix in `source` is expected or whether `source` should be the bare German sentence.