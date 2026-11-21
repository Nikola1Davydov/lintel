#!/usr/bin/env python3
"""Generates the name tables of src/Lintel/Ids/Data from the IDS audit library of buildingSMART.

IDS writes IFC names in upper case (IFCWALL, IFCLABEL); Lintel writes them as the IFC documentation does
(IfcWall, IfcLabel). The importer needs the spelling, the XML base type and the SI unit of each data type.
The source is the generated schema code of ids-lib (https://github.com/buildingSMART/IDS-Audit-tool, MIT licence).

usage: gen-ifc-names.py <path to IDS-Audit-tool/ids-lib/IfcSchema> <output directory>
"""
import os
import re
import sys


def main(schema_dir, out_dir):
    read = lambda name: open(os.path.join(schema_dir, name), encoding="utf-8").read()

    # every class and defined type in the spelling of the IFC documentation
    names = sorted(set(re.findall(r'new IfcClassInformation\("(Ifc[A-Za-z0-9_]+)"', read("SchemaInfo.ClassAndAttributeNames.g.cs"))))
    spelling = {name.upper(): name for name in names}

    # entity classes: the first argument of ClassInfo("Name", "Parent", ClassType.X, ...)
    classes = sorted(set(re.findall(r'new ClassInfo\("(Ifc[A-Za-z0-9_]+)", "[A-Za-z0-9_]*", ClassType\.(?:Concrete|Abstract)', read("SchemaInfo.Schemas.g.cs"))))
    with open(os.path.join(out_dir, "ifc-classes.txt"), "w", encoding="utf-8", newline="\n") as out:
        out.write("\n".join(classes) + "\n")

    # data types: name, XML base type, SI unit as written in Lintel
    rows = []
    measures = read("SchemaInfo.MeasureNames.g.cs")
    for line in measures.splitlines():
        match = re.search(r'new IfcDataTypeInformation\("([A-Z0-9_]+)", new\[\] \{[^}]*\}, (.*)\),?\s*$', line)
        if not match:
            continue
        upper, rest = match.group(1), match.group(2)
        base = re.search(r'"(xs:[a-zA-Z]+)"\s*$', rest)
        unit = ""
        measure = re.search(r'new IfcMeasureInformation\("[^"]*","[^"]*","[^"]*","[^"]*","([^"]*)"', rest)
        if measure:
            unit = measure.group(1).replace(" ", "").replace("°", "")
            # Lintel writes a unit as a name with at most one slash; "1/K" and "1/s" cannot be written that way.
            if not re.fullmatch(r"[^\W\d]\w*(/[^\W\d]\w*)?", unit):
                unit = ""
        rows.append((spelling.get(upper, upper), base.group(1) if base else "", unit))
    rows.sort()
    with open(os.path.join(out_dir, "ifc-datatypes.tsv"), "w", encoding="utf-8", newline="\n") as out:
        for row in rows:
            out.write("\t".join(row) + "\n")

    print(f"{len(classes)} classes, {len(rows)} data types, {sum(1 for r in rows if r[2])} with a unit")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
