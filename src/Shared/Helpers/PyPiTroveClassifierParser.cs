// Copyright (c) Microsoft Corporation. Licensed under the MIT License.

namespace Microsoft.CST.OpenSource.Helpers;

using Model.Metadata;
using System;
using System.Collections.Generic;

public static class PyPiTroveClassifierParser
{
    internal static readonly List<string> ParentClassifiers = new()
    {
        "Development Status",
        "Environment",
        "Framework",
        "Intended Audience",
        "License",
        "Natural Language",
        "Operating System",
        "Programming Language",
        "Topic",
        "Typing",
    };
    
    public static PyPiPackageVersionMetadata.TroveClassification? Parse(string classifier, out PyPiPackageVersionMetadata.TroveClassification classification)
    {
        string[] split = classifier.Split(" :: ");
        if (split.Length == 1)
        {
            throw new InvalidOperationException($"Invalid Trove Classification: {classifier}");
        }

        string parent;
        string target;
        string[] sub = Array.Empty<string>();
        switch (split.Length)
        {
            case 2:
                parent = split[0];
                target = split[1];
                break;
            default:
                parent = split[0];
                target = split[1];
                sub = split[2..];
                break;
        }

        if (!ParentClassifiers.Contains(parent))
        {
            throw new InvalidOperationException(
                $"Invalid Trove Parent Classification: {parent} in classifier: {classifier}");
        }

        classification = new PyPiPackageVersionMetadata.TroveClassification(parent, target, sub);
        return classification;
    }
}