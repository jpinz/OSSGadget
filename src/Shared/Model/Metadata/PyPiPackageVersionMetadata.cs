// Copyright (c) Microsoft Corporation. Licensed under the MIT License.

namespace Microsoft.CST.OpenSource.Model.Metadata;

using System.Collections.Generic;

public record PyPiPackageVersionMetadata : BasePackageVersionMetadata
{
    public IEnumerable<TroveClassification>? Classifiers { get; protected internal set; }

    /// <summary>
    /// A record representing a PyPI trove classification.
    /// </summary>
    /// <seealso href="https://peps.python.org/pep-0301/#distutils-trove-classification"/>
    /// <param name="Name">The name of the command, to be triggered by `npm run {name}`</param>
    /// <param name="Command">The command that gets ran in the console when triggered.</param>
    public record TroveClassification(string Parent, string Target, string[] SubClasses);
    
    public new enum ArtifactType
    {
        Unknown = 0,
        Tarball,
        Wheel,
    }
}