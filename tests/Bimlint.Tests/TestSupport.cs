using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bimlint.Diagnostics;

namespace Bimlint.Tests;

internal static class TestSupport
{
    /// <summary>The repository root: the first folder above the test assembly that holds the solution file.</summary>
    public static string RepositoryRoot
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

    /// <summary>A rule set with the header every fragment needs.</summary>
    public static string WithHeader(string rules) => "ifc IFC4\n\n" + rules;

    public static IReadOnlyList<string> Codes(this CheckResult result) =>
        result.Diagnostics.Select(d => d.Code).ToList();

    public static Diagnostic Single(this CheckResult result, string code) =>
        result.Diagnostics.Single(d => d.Code == code);

    public static string Describe(this CheckResult result) =>
        string.Join("\n", result.Diagnostics.Select(d => d.ToString()));
}
