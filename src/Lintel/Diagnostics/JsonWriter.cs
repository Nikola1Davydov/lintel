using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Lintel.Diagnostics;

/// <summary>
/// Writes the result of a check as JSON (SPEC.md, section 9). Written by hand so that the core library
/// has no dependency and runs on .NET Standard 2.0.
/// </summary>
internal sealed class JsonWriter
{
    private readonly StringBuilder _text = new();
    private readonly Stack<bool> _hasItems = new();
    private int _depth;

    public override string ToString() => _text.ToString();

    public JsonWriter BeginObject(string? name = null)
    {
        Open(name, '{');
        return this;
    }

    public JsonWriter EndObject()
    {
        Close('}');
        return this;
    }

    public JsonWriter BeginArray(string? name = null)
    {
        Open(name, '[');
        return this;
    }

    public JsonWriter EndArray()
    {
        Close(']');
        return this;
    }

    public JsonWriter Value(string name, string? value)
    {
        if (value is not null)
        {
            Separator(name);
            String(value);
        }

        return this;
    }

    public JsonWriter Value(string name, int value)
    {
        Separator(name);
        _text.Append(value.ToString(CultureInfo.InvariantCulture));
        return this;
    }

    public JsonWriter Value(string name, bool value)
    {
        Separator(name);
        _text.Append(value ? "true" : "false");
        return this;
    }

    public JsonWriter Item(string value)
    {
        Separator(null);
        String(value);
        return this;
    }

    private void Open(string? name, char bracket)
    {
        Separator(name);
        _text.Append(bracket);
        _hasItems.Push(false);
        _depth++;
    }

    private void Close(char bracket)
    {
        _depth--;
        if (_hasItems.Pop())
        {
            NewLine();
        }

        _text.Append(bracket);
    }

    private void Separator(string? name)
    {
        if (_hasItems.Count > 0)
        {
            if (_hasItems.Pop())
            {
                _text.Append(',');
            }

            _hasItems.Push(true);
            NewLine();
        }

        if (name is not null)
        {
            String(name);
            _text.Append(": ");
        }
    }

    private void NewLine()
    {
        _text.Append('\n');
        _text.Append(' ', _depth * 2);
    }

    private void String(string value)
    {
        _text.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    _text.Append("\\\"");
                    break;
                case '\\':
                    _text.Append("\\\\");
                    break;
                case '\n':
                    _text.Append("\\n");
                    break;
                case '\r':
                    _text.Append("\\r");
                    break;
                case '\t':
                    _text.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                    {
                        _text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        _text.Append(c);
                    }

                    break;
            }
        }

        _text.Append('"');
    }
}
