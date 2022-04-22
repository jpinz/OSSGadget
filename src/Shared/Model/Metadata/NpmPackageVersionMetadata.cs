// Copyright (c) Microsoft Corporation. Licensed under the MIT License.

namespace Microsoft.CST.OpenSource.Model.Metadata;

using System.Collections.Generic;

public record NpmPackageVersionMetadata : BasePackageVersionMetadata
{
    public IEnumerable<NpmScript>? Scripts { get; protected internal set; }

    /// <summary>
    /// A record representing a script in an NPM package.json
    /// </summary>
    /// <param name="Name">The name of the command, to be triggered by `npm run {name}`</param>
    /// <param name="Command">The command that gets ran in the console when triggered.</param>
    public record NpmScript(string Name, string Command);
    
    public enum NpmArtifactType
    {
        Unknown = 0,
        Tarball,
        PackageJson,
    }
}