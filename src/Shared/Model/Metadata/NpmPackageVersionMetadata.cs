// Copyright (c) Microsoft Corporation. Licensed under the MIT License.

namespace Microsoft.CST.OpenSource.Model.Metadata;

using PackageManagers;
using System.Collections.Generic;

public record NpmPackageVersionMetadata : BasePackageVersionMetadata
{
    public new string Protocol = NPMProjectManager.Type;

    /// <summary>
    /// Scope is the term used in NPM for the namespace, this is just for convenience sake.
    /// </summary>
    public string? Scope => Namespace;

    public new IEnumerable<NpmDependency> Dependencies { get; protected internal set; }
    
    public IEnumerable<NpmDependency> DevDependencies { get; protected internal set; }

    public IEnumerable<NpmScript>? Scripts { get; protected internal set; }

    /// <summary>
    /// A record representing a script in an NPM package.json
    /// </summary>
    /// <param name="Name">The name of the command, to be triggered by `npm run {name}`</param>
    /// <param name="Command">The command that gets ran in the console when triggered.</param>
    public record NpmScript(string Name, string Command);

    /// <summary>
    /// A record representing a dependency of an NPM package.
    /// </summary>
    /// <param name="Name">The name of the package being depended on.</param>
    /// <param name="Version">The version of the package being depended on.</param>
    public record NpmDependency(string Name, string Version);
    
    public new enum ArtifactType
    {
        Unknown = 0,
        Tarball,
        PackageJson,
    }
}