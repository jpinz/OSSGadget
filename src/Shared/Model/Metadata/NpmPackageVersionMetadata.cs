// Copyright (c) Microsoft Corporation. Licensed under the MIT License.

namespace Microsoft.CST.OpenSource.Model.Metadata;

using Helpers;
using PackageManagers;
using System.Collections.Generic;
using System.Net;

public record NpmPackageVersionMetadata : BasePackageVersionMetadata
{
    public new string Protocol = NPMProjectManager.Type;

    /// <summary>
    /// Scope is the term used in NPM for the namespace, this is just for convenience sake.
    /// It also prepends the @ symbol for npm scopes.
    /// </summary>
    public string? Scope => $"@{Namespace}";

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

    /// <inheritdoc cref="BasePackageVersionMetadata.GetFullName" />
    public new string GetFullName(bool encoded = false)
    {
        if (this.Namespace.IsBlank())
        {
            return this.Name;
        }

        string name = $"{this.Scope}/{this.Name}";
        
        // The full name for scoped npm packages should have an '@' at the beginning.
        return encoded ? name : WebUtility.UrlDecode(name);
    }
    
    public new enum ArtifactType
    {
        Unknown = 0,
        Tarball,
        PackageJson,
    }
}