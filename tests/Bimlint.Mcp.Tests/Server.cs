using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Bimlint.Mcp.Tests;

/// <summary>
/// The server as a client meets it: started as a process of its own and spoken to over standard input and output.
/// One process serves every test of the collection.
/// </summary>
public sealed class Server : IAsyncLifetime
{
    private readonly List<string> _errorLines = new();

    public McpClient Client { get; private set; } = null!;

    /// <summary>What the server wrote to standard error so far.</summary>
    public IReadOnlyList<string> ErrorLines
    {
        get
        {
            lock (_errorLines)
            {
                return _errorLines.ToList();
            }
        }
    }

    public async Task InitializeAsync()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "bimlint",
            Command = "dotnet",
            Arguments = new[] { Path.Combine(AppContext.BaseDirectory, "Bimlint.Mcp.dll") },
            StandardErrorLines = line =>
            {
                lock (_errorLines)
                {
                    _errorLines.Add(line);
                }
            },
        });
        Client = await McpClient.CreateAsync(transport);
    }

    public async Task DisposeAsync() => await Client.DisposeAsync();

    /// <summary>Calls a tool and returns its answer.</summary>
    public async Task<ToolAnswer> Call(string tool, params (string Name, object? Value)[] arguments)
    {
        var result = await Client.CallToolAsync(tool, arguments.ToDictionary(a => a.Name, a => a.Value));
        return new ToolAnswer(result);
    }
}

/// <summary>The answer of a tool: its text blocks, and whether the tool refused.</summary>
public sealed class ToolAnswer
{
    public ToolAnswer(CallToolResult result)
    {
        Refused = result.IsError == true;
        Blocks = result.Content.OfType<TextContentBlock>().Select(b => b.Text).ToList();
    }

    public bool Refused { get; }

    public IReadOnlyList<string> Blocks { get; }

    /// <summary>The first text block, which holds the main answer.</summary>
    public string Text => Blocks.Count > 0 ? Blocks[0] : string.Empty;

    public string All => string.Join("\n", Blocks);
}

[CollectionDefinition("server")]
public sealed class ServerCollection : ICollectionFixture<Server>
{
}

internal static class Repository
{
    /// <summary>The repository root: the first folder above the test assembly that holds the solution file.</summary>
    public static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Bimlint.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Bimlint.slnx was not found above the test assembly.");
        }
    }
}

/// <summary>A folder for the files of one test; it is removed with its content.</summary>
internal sealed class Folder : IDisposable
{
    public Folder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bimlint-mcp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // a file that is still open is left to the system
        }
    }
}
