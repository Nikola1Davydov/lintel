# Bimlint MCP server

The MCP server of [Bimlint](https://github.com/Nikola1Davydov/lintel), a small language for information requirements
on IFC models that compiles to buildingSMART IDS 1.0. It gives an AI agent what it needs to write such rules and to
check its own work: the language on one page, the names of the IFC schema, the checker and the compiler.

It runs as a child process of an MCP client and talks over standard input and output.

| Tool | What it does |
| --- | --- |
| `bimlint_language_guide` | The language on one page |
| `bimlint_list_properties` | The property sets and properties of the IFC standard with data types, units, allowed values and what each property means; the attributes, predefined types and subtypes of a class |
| `bimlint_validate_rules` | Checks rules: grammar, the rules of IDS, every name against the IFC schema. Answers with diagnostics as JSON, with fixes and the fixed source |
| `bimlint_compile_rules` | Writes the IDS 1.0 file. Refuses while the rules have errors, or warnings with a ready fix |
| `bimlint_import_ids` | Turns an IDS file into Bimlint rules |

Requires the .NET runtime 8 or later.
