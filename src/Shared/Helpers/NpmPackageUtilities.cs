// Copyright (c) Microsoft Corporation. Licensed under the MIT License.

namespace Microsoft.CST.OpenSource.Helpers;

using System;

public static class NpmPackageUtilities
{
    /// <summary>
    /// Splits a given package name into package scope and package name based on a "/" separator.
    /// </summary>
    /// <example>"@angular/cli" -> ("angular", "cli").</example>
    /// <param name="fullName">A full package name.</param>
    /// <returns>A pair of strings (scope, name). Scope does NOT include the leading '@'.</returns>
    internal static (string? Scope, string Name) SplitScopedName(string fullName)
    {
        if (fullName.IsBlank())
        {
            throw new ArgumentNullException(nameof(fullName), "Unable to split scope from blank package name.");
        }

        string[] splitNames = fullName.Split('/');
        if (splitNames.Length != 2)
        {
            return (null, fullName);
        }

        string scope = splitNames[0].TrimStart('@');
        string name = splitNames[1];
        return (scope, name);
    }

    internal static bool NameIsScoped(string packageName)
    {
        if (packageName.IsBlank())
        {
            return false;
        }

        return packageName.StartsWith('@') || packageName.StartsWith("%40");
    }

    internal static string JoinScopedName(string packageScope, string packageName, bool httpEncoded = false)
    {
        return httpEncoded ? $"%40{packageScope}/{packageName}" : $"@{packageScope}/{packageName}";
    }
}