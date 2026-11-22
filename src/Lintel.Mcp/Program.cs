using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace Lintel.Mcp;

/// <summary>
/// The MCP server of Lintel. An AI agent starts it as a child process and talks to it over standard input and
/// output: it reads the language guide, looks up names of the IFC schema, checks rules and compiles them to IDS.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0)
        {
            Console.Error.WriteLine("lintel-mcp " + Version);
            Console.Error.WriteLine("The MCP server of Lintel. It takes no arguments and talks MCP over standard input and output;");
            Console.Error.WriteLine("it is started by an MCP client, such as Claude Code, Claude Desktop or Codex.");
            return args[0] is "--help" or "-h" or "--version" ? 0 : 2;
        }

        var builder = Host.CreateApplicationBuilder(args);

        // Standard output belongs to the protocol: every log line goes to standard error.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "lintel", Title = "Lintel", Version = Version };
                options.ServerInstructions = ServerText.Instructions;
            })
            .WithStdioServerTransport()
            .WithTools<RuleTools>()
            .WithTools<SchemaTools>();

        await builder.Build().RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static string Version =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
}
