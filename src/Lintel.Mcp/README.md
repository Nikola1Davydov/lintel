# Lintel MCP server

The MCP server of [Lintel](https://github.com/Nikola1Davydov/lintel), a small language for information requirements
on IFC models that compiles to buildingSMART IDS 1.0. It gives an AI agent what it needs to write such rules and to
check its own work: the language on one page, the names of the IFC schema, the checker and the compiler.

It runs as a child process of an MCP client and talks over standard input and output.

| Tool | What it does |
| --- | --- |
| `lintel_language_guide` | The language on one page |
| `lintel_list_properties` | The property sets and properties of the IFC standard with data types, units and allowed values; the attributes, predefined types and subtypes of a class |
| `lintel_validate_rules` | Checks rules: grammar, the rules of IDS, every name against the IFC schema. Answers with diagnostics as JSON, with fixes and the fixed source |
| `lintel_compile_rules` | Writes the IDS 1.0 file. Refuses while the rules have errors, or warnings with a ready fix |
| `lintel_import_ids` | Turns an IDS file into Lintel rules |

Requires the .NET runtime 8 or later.
