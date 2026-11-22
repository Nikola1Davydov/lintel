using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Lintel.Ifc;

namespace Lintel.Tests;

/// <summary>
/// Writes random IDS documents: well formed, mostly valid, with names of the IFC schema and names that are not,
/// in the combinations IDS allows and in some it forbids. The official audit decides which of them are valid;
/// the tests compare its verdict with the compiler's.
/// </summary>
internal sealed class IdsGenerator
{
    private static readonly XNamespace Ids = "http://standards.buildingsmart.org/IDS";
    private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    private static readonly string[] Versions = { "IFC2X3", "IFC4", "IFC4X3_ADD2" };

    private static readonly string[] CommonClasses =
    {
        "IfcWall", "IfcWall", "IfcSlab", "IfcDoor", "IfcWindow", "IfcSpace", "IfcBeam", "IfcColumn", "IfcBuildingStorey",
        "IfcBuilding", "IfcSite", "IfcProject", "IfcElement", "IfcBuildingElement", "IfcBuiltElement", "IfcWallType",
        "IfcDoorStyle", "IfcDoorType", "IfcFlowSegment", "IfcPipeSegment", "IfcGroup", "IfcSystem", "IfcZone", "IfcProduct",
        "IfcWallStandardCase", "IfcCovering", "IfcRoof", "IfcStair", "IfcFurnishingElement", "IfcBuildingElementProxy",
        "IfcOpeningElement", "IfcMaterial", "IfcPropertySet", "IfcTask", "IfcSpatialZone", "IfcFacility", "IfcBridge",
    };

    private static readonly string[] Relations =
    {
        "IFCRELAGGREGATES", "IFCRELASSIGNSTOGROUP", "IFCRELCONTAINEDINSPATIALSTRUCTURE", "IFCRELNESTS",
        "IFCRELVOIDSELEMENT IFCRELFILLSELEMENT",
    };

    private static readonly string[] Texts =
    {
        "F90", "REI 120", "Wand", "x", "Beton C25/30", "Außenwand", "a \"quoted\" word", "0", "12", "1.5", "TRUE", "true",
        "Ss_25_10", "EF_25_10", "A.B", "it's", "<tag> & more", "back\\slash", "  padded  ", "#hash", "[a, b]", string.Empty,
    };

    private static readonly string[] Patterns =
    {
        "F[0-9]{2}", "(F|REI)[0-9]{2,3}", ".*", "[A-Z]{2}-\\d+", "W.*", "\\w+", "[^ ]+", "Ss_25.*", "a|b|c",
        "\\p{Lu}.*", "[a-z-[aeiou]]+", ".+\\.pdf", "^F90$", "(?=x)", "x{2", "\\d{3}?",
    };

    private static readonly string[] ClassPatterns =
    {
        "IFC.*TYPE", "IFCWALL.*", "IFC(DOOR|WINDOW)", ".*", "IFCSLAB", "IfcWall.*", "IFCXYZ.*", ".*PROXY", "IFC[A-Z]+STOREY",
    };

    private static readonly string[] Doubles = { "0", "1", "0.5", "1.5", "42.0", "-3.25", "100", "0.001", "1e3", "1.5E-2", ".5", "5.", "+2", "007", "INF", "NaN" };
    private static readonly string[] Integers = { "0", "1", "42", "-7", "100", "+3", "007", "1.0" };
    private static readonly string[] Booleans = { "true", "false", "true", "false", "TRUE", "1", "0", "yes" };

    private readonly Random _random;
    private List<IfcSchema> _schemas = new();
    private List<string>? _classes;

    public IdsGenerator(int seed) => _random = new Random(seed);

    /// <summary>One IDS document, mostly with one specification.</summary>
    public string Document()
    {
        var roll = _random.Next(100);
        var specifications = Enumerable.Range(0, roll < 85 ? 1 : roll < 95 ? 2 : 3).Select(_ => Specification()).ToList();

        var info = new XElement(Ids + "info", new XElement(Ids + "title", Chance(0.9) ? "Generated" : Text()));
        if (Chance(0.2))
        {
            info.Add(new XElement(Ids + "copyright", Text()));
        }

        if (Chance(0.2))
        {
            info.Add(new XElement(Ids + "version", Pick("1.0", "0.1", "draft 2")));
        }

        if (Chance(0.2))
        {
            info.Add(new XElement(Ids + "description", Chance(0.3) ? "two\nlines" : Text()));
        }

        if (Chance(0.2))
        {
            info.Add(new XElement(Ids + "author", Chance(0.1) ? "nobody" : Pick("bim@example.com", "a.b@c.de")));
        }

        if (Chance(0.2))
        {
            info.Add(new XElement(Ids + "date", Chance(0.1) ? "22.11.2026" : Pick("2026-11-22", "2024-01-31")));
        }

        if (Chance(0.15))
        {
            info.Add(new XElement(Ids + "purpose", Text()));
        }

        if (Chance(0.15))
        {
            info.Add(new XElement(Ids + "milestone", Text()));
        }

        var root = new XElement(
            Ids + "ids",
            new XAttribute(XNamespace.Xmlns + "xs", Xs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName),
            new XAttribute(Xsi + "schemaLocation", "http://standards.buildingsmart.org/IDS http://standards.buildingsmart.org/IDS/1.0/ids.xsd"),
            info,
            new XElement(Ids + "specifications", specifications));
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + root;
    }

    private XElement Specification()
    {
        _schemas = PickVersions().Select(v => IfcSchema.Get(v)!).ToList();
        _classes = null;

        var specification = new XElement(
            Ids + "specification",
            new XAttribute("name", Chance(0.9) ? "Rule " + _random.Next(1000) : Text()),
            new XAttribute("ifcVersion", string.Join(" ", _schemas.Select(s => s.Version))));
        if (Chance(0.3))
        {
            specification.Add(new XAttribute("identifier", Pick("R1", "AIA 4.2.1", "A-12", "1", "id with \"quote\"")));
        }

        if (Chance(0.2))
        {
            specification.Add(new XAttribute("description", Text()));
        }

        if (Chance(0.2))
        {
            specification.Add(new XAttribute("instructions", Text()));
        }

        // The applicability, with the occurrence that says how many elements must be selected.
        var applicability = new XElement(Ids + "applicability");
        var occurs = _random.Next(100);
        var (min, max) = occurs < 62 ? ("0", "unbounded")
            : occurs < 81 ? ("1", "unbounded")
            : occurs < 94 ? ("0", "0")
            : occurs < 95 ? ("1", "1")
            : occurs < 96 ? ("0", "1")
            : occurs < 97 ? ("2", "unbounded")
            : occurs < 98 ? ("1", "0")
            : (null, null);
        if (min is not null)
        {
            applicability.Add(new XAttribute("minOccurs", min), new XAttribute("maxOccurs", max!));
        }
        else if (Chance(0.5))
        {
            applicability.Add(new XAttribute("maxOccurs", "unbounded"));
        }

        if (Chance(0.85))
        {
            applicability.Add(Entity("entity", context: null, remember: true));
        }

        // The other facets follow in the order of the schema.
        var others = _random.Next(100) < 55 ? 0 : _random.Next(1, 3);
        if (applicability.IsEmpty && others == 0 && Chance(0.9))
        {
            others = 1;
        }

        var kinds = Enumerable.Range(0, others).Select(_ => _random.Next(5)).OrderBy(k => k).ToList();
        foreach (var kind in kinds)
        {
            applicability.Add(Facet(kind, requirement: false));
        }

        specification.Add(applicability);

        // The requirements, in any order.
        var count = max == "0" ? (Chance(0.85) ? 0 : 1) : _random.Next(100) < 8 ? 0 : _random.Next(1, 4);
        if (count > 0 || Chance(0.3))
        {
            var requirements = new XElement(Ids + "requirements");
            if (Chance(0.25))
            {
                requirements.Add(new XAttribute("description", Text()));
            }

            for (var i = 0; i < count; i++)
            {
                requirements.Add(Facet(_random.Next(100) < 8 ? 5 : _random.Next(5), requirement: true));
            }

            specification.Add(requirements);
        }

        return specification;
    }

    private List<string> PickVersions()
    {
        var roll = _random.Next(100);
        if (roll < 60)
        {
            return new List<string> { Pick(Versions) };
        }

        if (roll < 85)
        {
            var skip = _random.Next(3);
            return Versions.Where((_, i) => i != skip).ToList();
        }

        return Versions.ToList();
    }

    // ---------------------------------------------------------------- facets

    private XElement Facet(int kind, bool requirement)
    {
        XElement facet;
        switch (kind)
        {
            case 0:
                facet = new XElement(Ids + "partOf");
                if (Chance(0.6))
                {
                    facet.Add(new XAttribute("relation", Pick(Relations)));
                }

                facet.Add(Entity("entity", context: (string?)facet.Attribute("relation"), remember: false));
                break;
            case 1:
                facet = new XElement(Ids + "classification");
                if (Chance(0.6))
                {
                    facet.Add(new XElement(Ids + "value", TextValue()));
                }

                facet.Add(new XElement(Ids + "system", Chance(0.85) ? Simple(Pick("Uniclass 2015", "DIN 276", "OmniClass", "eBKP-H")) : TextValue()));
                break;
            case 2:
                facet = Attribute();
                break;
            case 3:
                facet = Property();
                break;
            case 4:
                facet = new XElement(Ids + "material");
                if (Chance(0.7))
                {
                    facet.Add(new XElement(Ids + "value", TextValue()));
                }

                break;
            default:
                facet = Entity("entity", context: null, remember: false);
                break;
        }

        if (!requirement)
        {
            return facet;
        }

        if (kind != 5)
        {
            var roll = _random.Next(100);
            if (roll < 30)
            {
                facet.Add(new XAttribute("cardinality", "required"));
            }
            else if (roll < 50)
            {
                facet.Add(new XAttribute("cardinality", "optional"));
            }
            else if (roll < 70)
            {
                facet.Add(new XAttribute("cardinality", "prohibited"));
            }
        }

        if (kind is 1 or 3 or 4 && Chance(0.15))
        {
            facet.Add(new XAttribute("uri", Pick("https://example.org/a", "https://identifier.buildingsmart.org/uri/x/y", "not a uri")));
        }

        if (Chance(0.15))
        {
            facet.Add(new XAttribute("instructions", Text()));
        }

        return facet;
    }

    private XElement Entity(string element, string? context, bool remember)
    {
        var entity = new XElement(Ids + element);
        List<string>? classes = null;
        var roll = _random.Next(100);
        if (roll < 62)
        {
            var name = ClassName(context);
            classes = new List<string> { name };
            entity.Add(new XElement(Ids + "name", Simple(Spell(name))));
        }
        else if (roll < 74)
        {
            // a class with its subtypes, as the compiler writes it, sometimes with one name missing
            var name = ClassName(context);
            var concrete = _schemas.SelectMany(s => s.FindClass(name)?.ConcreteClasses ?? Array.Empty<string>()).Distinct().ToList();
            if (concrete.Count > 1 && Chance(0.3))
            {
                concrete.RemoveAt(_random.Next(concrete.Count));
            }

            if (concrete.Count == 0)
            {
                concrete.Add(name);
            }

            classes = concrete;
            entity.Add(new XElement(Ids + "name", Enumeration("xs:string", concrete.Select(c => c.ToUpperInvariant()))));
        }
        else if (roll < 90)
        {
            classes = Enumerable.Range(0, _random.Next(1, 4)).Select(_ => ClassName(context)).Distinct().ToList();
            entity.Add(new XElement(Ids + "name", Enumeration("xs:string", classes.Select(Spell))));
        }
        else
        {
            entity.Add(new XElement(Ids + "name", Restriction("xs:string", new XElement(Xs + "pattern", new XAttribute("value", Pick(ClassPatterns))))));
        }

        if (remember)
        {
            _classes = classes;
        }

        if (Chance(0.3))
        {
            entity.Add(new XElement(Ids + "predefinedType", PredefinedType(classes)));
        }

        return entity;
    }

    /// <summary>A class name as IDS wants it, in upper case; now and then as the IFC documentation spells it.</summary>
    private string Spell(string name) => Chance(0.03) ? name : name.ToUpperInvariant();

    private string ClassName(string? relation)
    {
        var roll = _random.Next(100);
        if (roll < 3)
        {
            return Pick("IfcWal", "IfcFoo", "Wall", "IfcWallTyp");
        }

        if (relation is not null && roll < 60)
        {
            return relation switch
            {
                "IFCRELASSIGNSTOGROUP" => Pick("IfcGroup", "IfcSystem", "IfcZone"),
                "IFCRELCONTAINEDINSPATIALSTRUCTURE" => Pick("IfcBuildingStorey", "IfcSpace", "IfcBuilding", "IfcSite"),
                "IFCRELVOIDSELEMENT IFCRELFILLSELEMENT" => Pick("IfcWall", "IfcSlab", "IfcElement"),
                _ => Pick("IfcWall", "IfcBuildingStorey", "IfcElementAssembly", "IfcStair", "IfcSlab"),
            };
        }

        if (roll < 80)
        {
            return Pick(CommonClasses);
        }

        var names = PickOf(_schemas).ClassNames;
        return names[_random.Next(names.Count)];
    }

    private XElement PredefinedType(List<string>? classes)
    {
        var standard = (classes ?? new List<string>())
            .SelectMany(c => _schemas.SelectMany(s => s.FindClass(c)?.PredefinedTypes ?? Array.Empty<string>()))
            .Distinct()
            .ToList();
        var roll = _random.Next(100);
        if (standard.Count > 0 && roll < 50)
        {
            return Simple(PickOf(standard));
        }

        if (standard.Count > 1 && roll < 65)
        {
            return Enumeration("xs:string", new[] { PickOf(standard), PickOf(standard), "Sonderfall" }.Distinct());
        }

        if (roll < 75)
        {
            return Simple(Pick("FLOOR", "USERDEFINED", "NOTDEFINED", "SOLIDWALL", "DOOR"));
        }

        if (roll < 90)
        {
            return Simple(Pick("Brandwand", "WALDO", "My Type", "floor", "x-1"));
        }

        return Restriction("xs:string", new XElement(Xs + "pattern", new XAttribute("value", Pick("FLOOR|ROOF", ".*WALL", "[A-Z]+"))));
    }

    private XElement Attribute()
    {
        var facet = new XElement(Ids + "attribute");
        IfcAttribute? known = null;
        var available = (_classes ?? new List<string>())
            .Select(c => _schemas[0].FindClass(c))
            .Where(c => c is not null)
            .SelectMany(c => c!.Attributes.Values)
            .ToList();

        var roll = _random.Next(100);
        if (available.Count > 0 && roll < 60)
        {
            var values = available.Where(a => a.XmlBase.Length > 0 && !a.IsCollection).ToList();
            known = values.Count > 0 && Chance(0.85) ? PickOf(values) : PickOf(available);
            facet.Add(new XElement(Ids + "name", Simple(known.Name)));
        }
        else if (roll < 80)
        {
            facet.Add(new XElement(Ids + "name", Simple(Pick("Name", "Description", "GlobalId", "Tag", "ObjectType", "LongName", "PredefinedType", "OverallHeight", "Elevation", "CompositionType"))));
        }
        else if (roll < 86)
        {
            facet.Add(new XElement(Ids + "name", Simple(Pick("name", "Nam", "FireRating", "Foo Bar"))));
        }
        else if (roll < 93)
        {
            facet.Add(new XElement(Ids + "name", Enumeration("xs:string", new[] { "Name", Pick("Description", "Tag", "Nope") })));
        }
        else
        {
            facet.Add(new XElement(Ids + "name", Restriction("xs:string", new XElement(Xs + "pattern", new XAttribute("value", Pick(".*Name", "Name|Description", "Overall.*", "Zzz.*"))))));
        }

        if (Chance(0.55))
        {
            var xmlBase = known?.XmlBase is { Length: > 0 } b && Chance(0.9) ? b : Pick("xs:string", "xs:string", "xs:string", "xs:double", "xs:integer", "xs:boolean");
            facet.Add(new XElement(Ids + "value", Value(xmlBase)));
        }

        return facet;
    }

    private XElement Property()
    {
        var facet = new XElement(Ids + "property");
        var schema = _schemas[0];
        IfcProperty? standard = null;

        var roll = _random.Next(100);
        if (roll < 45)
        {
            // a property set of the standard: one that fits the selected classes, or any
            var fitting = _classes is null
                ? new List<string>()
                : schema.PropertySetNames.Where(n => _classes.Any(c => schema.ClassesOf(schema.FindPropertySet(n)!).Contains(c))).ToList();
            var setName = fitting.Count > 0 && Chance(0.85) ? PickOf(fitting) : schema.PropertySetNames[_random.Next(schema.PropertySetNames.Count)];
            var set = schema.FindPropertySet(setName)!;
            facet.Add(new XElement(Ids + "propertySet", Simple(setName)));
            if (set.PropertyNames.Count > 0 && Chance(0.9))
            {
                standard = set.FindProperty(PickOf(set.PropertyNames));
                facet.Add(new XElement(Ids + "baseName", Simple(standard!.Name)));
            }
            else
            {
                facet.Add(new XElement(Ids + "baseName", Simple(Pick("FireRatin", "Custom", "IsExternal"))));
            }
        }
        else if (roll < 85)
        {
            facet.Add(new XElement(Ids + "propertySet", Simple(Pick("ACME", "ACME_Wall", "My Set", "Projekt.Allgemein", "Äußeres", "CPset_Custom", "pset_lower", "1st"))));
            facet.Add(new XElement(Ids + "baseName", Chance(0.9) ? Simple(Pick("Status", "FireRating", "Bemerkung", "Fire Rating", "U-Wert", "A.B", "Länge", "2nd", "in")) : TextValue()));
        }
        else if (roll < 90)
        {
            facet.Add(new XElement(Ids + "propertySet", Simple(Pick("Pset_Custom", "Qto_Custom", "Pset_WallComon"))));
            facet.Add(new XElement(Ids + "baseName", Simple("Status")));
        }
        else if (roll < 95)
        {
            facet.Add(new XElement(Ids + "propertySet", Enumeration("xs:string", new[] { "Pset_WallCommon", Pick("Pset_SlabCommon", "ACME", "Pset_DoorCommon") })));
            facet.Add(new XElement(Ids + "baseName", Simple(Pick("FireRating", "IsExternal", "Status"))));
        }
        else
        {
            facet.Add(new XElement(Ids + "propertySet", Restriction("xs:string", new XElement(Xs + "pattern", new XAttribute("value", Pick("Pset_.*", ".*Common", "ACME.*", "Qto_.*BaseQuantities"))))));
            facet.Add(new XElement(Ids + "baseName", Chance(0.7) ? Simple(Pick("FireRating", "NetVolume", "Status")) : TextValue()));
        }

        // the data type: the one of the standard, another one, or none
        IfcDataType? type = null;
        var typeRoll = _random.Next(100);
        if (standard is { DataTypes.Count: > 0 } && typeRoll < 70)
        {
            type = IfcDataTypes.Find(PickOf(standard.DataTypes));
        }
        else if (typeRoll < 80)
        {
            // none
        }
        else
        {
            type = IfcDataTypes.Find(Pick(
                "IfcLabel", "IfcLabel", "IfcText", "IfcIdentifier", "IfcBoolean", "IfcLogical", "IfcInteger", "IfcReal", "IfcLengthMeasure",
                "IfcPositiveLengthMeasure", "IfcAreaMeasure", "IfcVolumeMeasure", "IfcCountMeasure", "IfcThermalTransmittanceMeasure",
                "IfcPlaneAngleMeasure", "IfcDate", "IfcDateTime", "IfcDuration", "IfcTime", "IfcTimeStamp", "IfcNonNegativeLengthMeasure",
                "IfcRatioMeasure", "IfcMassMeasure", "IfcPowerMeasure", "IfcElectricCurrentMeasure", "IfcDimensionCount"));
        }

        if (type is not null)
        {
            var spelled = Chance(0.03) ? type.Name : type.Name.ToUpperInvariant();
            facet.Add(new XAttribute("dataType", Chance(0.02) ? "IFCNOPE" : spelled));
        }

        if (Chance(type is null ? 0.35 : 0.6))
        {
            var xmlBase = type?.XmlBase is { Length: > 0 } b && Chance(0.92) ? b : Pick("xs:string", "xs:string", "xs:double", "xs:integer", "xs:boolean");
            var allowed = standard?.AllowedValues;
            facet.Add(new XElement(Ids + "value", allowed is { Count: > 0 } && Chance(0.7) ? Simple(Chance(0.9) ? PickOf(allowed) : "OTHERVALUE") : Value(xmlBase)));
        }

        return facet;
    }

    // ---------------------------------------------------------------- values

    private static XElement Simple(string text) => new(Ids + "simpleValue", text);

    private static XElement Restriction(string? xmlBase, params object[] content)
    {
        var restriction = new XElement(Xs + "restriction", content);
        if (xmlBase is not null)
        {
            restriction.Add(new XAttribute("base", xmlBase));
        }

        return restriction;
    }

    private static XElement Enumeration(string xmlBase, IEnumerable<string> values) =>
        Restriction(xmlBase, values.Select(v => (object)new XElement(Xs + "enumeration", new XAttribute("value", v))).ToArray());

    private XElement TextValue() => Value("xs:string");

    /// <summary>A value of an XML base type: one value, a list, a pattern, bounds or a length.</summary>
    private XElement Value(string xmlBase)
    {
        var numeric = xmlBase is "xs:double" or "xs:integer" or "xs:decimal" or "xs:float";
        var roll = _random.Next(100);
        if (roll < 45)
        {
            return Simple(Literal(xmlBase));
        }

        // now and then the base and the content disagree
        var written = Chance(0.04) ? Pick("xs:string", "xs:double", "xs:integer", "xs:boolean") : xmlBase;
        var content = new List<object>();
        if (Chance(0.12))
        {
            content.Add(new XElement(Xs + "annotation", new XElement(Xs + "documentation", Text())));
        }

        if (roll < 65)
        {
            var values = Enumerable.Range(0, _random.Next(1, 4)).Select(_ => Literal(xmlBase)).Distinct();
            content.AddRange(values.Select(v => new XElement(Xs + "enumeration", new XAttribute("value", v))));
        }
        else if (roll < 80 && !numeric || roll < 68)
        {
            content.Add(new XElement(Xs + "pattern", new XAttribute("value", Pick(Patterns))));
            if (Chance(0.2))
            {
                content.Add(new XElement(Xs + "pattern", new XAttribute("value", Pick(Patterns))));
            }
        }
        else if (numeric || roll < 84)
        {
            var low = Chance(0.7);
            var high = !low || Chance(0.5);
            if (low)
            {
                content.Add(new XElement(Xs + (Chance(0.6) ? "minInclusive" : "minExclusive"), new XAttribute("value", Literal(numeric ? xmlBase : "xs:double"))));
            }

            if (high)
            {
                content.Add(new XElement(Xs + (Chance(0.6) ? "maxInclusive" : "maxExclusive"), new XAttribute("value", Literal(numeric ? xmlBase : "xs:double"))));
            }

            if (Chance(0.1))
            {
                content.Reverse();
            }
        }
        else if (roll < 94)
        {
            var form = _random.Next(4);
            if (form == 0)
            {
                content.Add(new XElement(Xs + "length", new XAttribute("value", Pick("1", "4", "12", "0"))));
            }

            if (form is 1 or 3)
            {
                content.Add(new XElement(Xs + "minLength", new XAttribute("value", Pick("1", "2", "0"))));
            }

            if (form is 2 or 3)
            {
                content.Add(new XElement(Xs + "maxLength", new XAttribute("value", Pick("8", "12", "255"))));
            }
        }
        else if (roll < 97)
        {
            // two kinds of facets in one restriction
            content.Add(new XElement(Xs + "pattern", new XAttribute("value", Pick(Patterns))));
            content.Add(new XElement(Xs + (Chance(0.5) ? "minLength" : "maxLength"), new XAttribute("value", "3")));
            if (Chance(0.3))
            {
                content.Add(new XElement(Xs + "enumeration", new XAttribute("value", Literal(xmlBase))));
            }
        }
        else
        {
            // facets of XML Schema that the IDS documentation does not mention
            content.Add(new XElement(Xs + Pick("totalDigits", "fractionDigits", "whiteSpace"), new XAttribute("value", Pick("2", "collapse"))));
        }

        return Restriction(Chance(0.02) ? null : written, content.ToArray());
    }

    private string Literal(string xmlBase) => xmlBase switch
    {
        "xs:double" or "xs:decimal" or "xs:float" => Pick(Doubles),
        "xs:integer" => Pick(Integers),
        "xs:boolean" => Pick(Booleans),
        "xs:date" => Pick("2026-11-22", "2024-02-29", "22.11.2026"),
        "xs:dateTime" => Pick("2026-11-22T10:00:00", "2026-11-22T10:00:00Z", "today"),
        "xs:time" => Pick("10:00:00", "23:59:59", "noon"),
        "xs:duration" => Pick("P1D", "PT2H", "P1Y2M", "one day"),
        _ => Pick(Texts),
    };

    private string Text() => Pick(
        "Alle tragenden Wände müssen eine Feuerwiderstandsklasse aufweisen.", "see section 4.2", "a \"quoted\" word", "ask the engineer",
        "tabs\tand spaces", "<b>bold</b> & more", "x", "why not", "100 %", "# not a comment");

    private bool Chance(double probability) => _random.NextDouble() < probability;

    private T Pick<T>(params T[] items) => items[_random.Next(items.Length)];

    private T PickOf<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];
}
