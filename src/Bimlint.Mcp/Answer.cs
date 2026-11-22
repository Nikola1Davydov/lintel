using System;
using System.Collections.Generic;
using System.Linq;
using ModelContextProtocol.Protocol;

namespace Bimlint.Mcp;

/// <summary>
/// A problem with what a tool was asked: a missing file, an unknown class, an answer that is too long.
/// Its message goes to the agent as the answer of the tool, with what to do next.
/// </summary>
internal sealed class ToolProblem : Exception
{
    public ToolProblem(string message)
        : base(message)
    {
    }
}

/// <summary>The answers of the tools: text for the agent, marked as a failure when the tool did not do what was asked.</summary>
internal static class Answer
{
    public static CallToolResult Done(params string[] texts) =>
        new() { Content = texts.Select(t => (ContentBlock)new TextContentBlock { Text = t }).ToList() };

    public static CallToolResult Refused(string text) =>
        new() { IsError = true, Content = new List<ContentBlock> { new TextContentBlock { Text = text } } };

    /// <summary>Runs a tool; a problem with the request becomes its answer instead of an exception of the server.</summary>
    public static CallToolResult Of(Func<CallToolResult> tool)
    {
        try
        {
            return tool();
        }
        catch (ToolProblem problem)
        {
            return Refused(problem.Message);
        }
    }
}
