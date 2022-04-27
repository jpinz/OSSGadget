// Copyright (c) Microsoft Corporation. Licensed under the MIT License.

namespace Microsoft.CST.OpenSource.PackageManagers
{
    using Contracts;
    using Helpers;
    using PackageUrl;
    using Model;
    using Model.Metadata;
    using NuGet.Packaging;
    using NuGet.Packaging.Core;
    using NuGet.Versioning;
    using PackageActions;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading.Tasks;

    public class NuGetProjectManager : TypedProjectManager<NuGetPackageVersionMetadata>
    {
        /// <summary>
        /// The type of the project manager from the package-url type specifications.
        /// </summary>
        /// <seealso href="https://www.github.com/package-url/purl-spec/blob/master/PURL-TYPES.rst"/>
        public const string Type = "nuget";

        public override string ManagerType => Type;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0044:Add readonly modifier", Justification = "Modified through reflection.")]
        public const string ENV_NUGET_ENDPOINT_API = "https://api.nuget.org";
        public const string ENV_NUGET_ENDPOINT = "https://www.nuget.org";
        public const string NUGET_DEFAULT_REGISTRATION_ENDPOINT = "https://api.nuget.org/v3/registration5-gz-semver2/";
        private const string NUGET_DEFAULT_CONTENT_ENDPOINT = "https://api.nuget.org/v3-flatcontainer/";

        private string? RegistrationEndpoint { get; set; } = null;

        public NuGetProjectManager(
            IHttpClientFactory? httpClientFactory = null,
            IManagerPackageActions<NuGetPackageVersionMetadata>? actions = null,
            string directory = ".")
            : base(httpClientFactory ?? new DefaultHttpClientFactory(), actions ?? new NuGetPackageActions(), directory)
        {
            GetRegistrationEndpointAsync().Wait();
        }
        
        /// <inheritdoc />
        public override IEnumerable<ArtifactUri<Enum>> GetArtifactDownloadUris(PackageURL purl)
        {
            string feedUrl = (purl.Qualifiers?["repository_url"] ?? NUGET_DEFAULT_CONTENT_ENDPOINT).EnsureTrailingSlash();

            string nupkgUri = $"{feedUrl}{purl.Name.ToLower()}/{purl.Version}/{purl.Name.ToLower()}.{purl.Version}.nupkg";
            yield return new ArtifactUri<Enum>(NuGetPackageVersionMetadata.ArtifactType.Nupkg, nupkgUri);
            string nuspecUri = $"{feedUrl}{purl.Name.ToLower()}/{purl.Version}/{purl.Name.ToLower()}.nuspec";
            yield return new ArtifactUri<Enum>(NuGetPackageVersionMetadata.ArtifactType.Nuspec, nuspecUri);
        }

        /// <summary>
        /// Dynamically identifies the registration endpoint.
        /// </summary>
        /// <returns>NuGet registration endpoint</returns>
        private async Task<string> GetRegistrationEndpointAsync()
        {
            if (RegistrationEndpoint != null)
            {
                return RegistrationEndpoint;
            }

            try
            {
                HttpClient httpClient = CreateHttpClient();
                JsonDocument doc = await GetJsonCache(httpClient, $"{ENV_NUGET_ENDPOINT_API}/v3/index.json");
                JsonElement.ArrayEnumerator resources = doc.RootElement.GetProperty("resources").EnumerateArray();
                foreach (JsonElement resource in resources)
                {
                    try
                    {
                        string? _type = resource.GetProperty("@type").GetString();
                        if (_type != null && _type.Equals("RegistrationsBaseUrl/Versioned", StringComparison.InvariantCultureIgnoreCase))
                        {
                            string? _id = resource.GetProperty("@id").GetString();
                            if (!string.IsNullOrWhiteSpace(_id))
                            {
                                RegistrationEndpoint = _id;
                                return _id;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug(ex, "Error parsing NuGet API endpoint: {0}", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Error parsing NuGet API endpoint: {0}", ex.Message);
            }
            RegistrationEndpoint = NUGET_DEFAULT_REGISTRATION_ENDPOINT;
            return RegistrationEndpoint;
        }

        /// <summary>
        /// Gets the <see cref="DateTime"/> a package version was published at.
        /// </summary>
        /// <param name="purl">Package URL specifying the package. Version is mandatory.</param>
        /// <param name="useCache">If the cache should be used when looking for the published time.</param>
        /// <returns>The <see cref="DateTime"/> when this version was published, or null if not found.</returns>
        public async Task<DateTime?> GetPublishedAtAsync(PackageURL purl, bool useCache = true)
        {
            Check.NotNull(nameof(purl.Version), purl.Version);
            NuGetPackageVersionMetadata? metadata = await this.GetPackageMetadataAsync(purl, useCache);
            DateTimeOffset? publishTime = metadata?.PublishTime;
            return publishTime?.DateTime;
        }

        /// <inheritdoc />
        public override async Task<string?> GetMetadataAsync(PackageURL purl, bool useCache = true)
        {
            try
            {
                string? packageName = purl.Name;
                string? packageVersion = purl.Version;
                if (packageName == null)
                {
                    return null;
                }

                // If no package version provided, default to the latest version.
                if (string.IsNullOrWhiteSpace(packageVersion))
                {
                    string latestVersion = await Actions.GetLatestVersionAsync(purl) ??
                                           throw new InvalidOperationException($"Can't find the latest version of {purl}");
                    packageVersion = latestVersion;
                }

                // Construct a new PackageURL that's guaranteed to have a version.
                PackageURL purlWithVersion = new (purl.Type, purl.Namespace, packageName, packageVersion, purl.Qualifiers, purl.Subpath);
                
                NuGetPackageVersionMetadata? packageVersionMetadata =
                    await Actions.GetMetadataAsync(purlWithVersion, useCache: useCache);

                return JsonSerializer.Serialize(packageVersionMetadata);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, $"Error fetching NuGet metadata: {ex.Message}");
                return null;
            }
        }

        public override Uri GetPackageAbsoluteUri(PackageURL purl)
        {
            return new Uri($"{ENV_NUGET_HOMEPAGE}/{purl?.Name}");
        }
        
        /// <inheritdoc />
        public override async Task<BasePackageVersionMetadata?> GetPackageMetadataAsync(PackageURL purl, bool useCache = true)
        {
            string? latestVersion = await Actions.GetLatestVersionAsync(purl) ??
                                    throw new InvalidOperationException($"Can't find the latest version of {purl}");;

            // Construct a new PackageURL that's guaranteed to have a version, the latest version is used if no version was provided.
            PackageURL purlWithVersion = !string.IsNullOrWhiteSpace(purl.Version) ? 
                purl : new PackageURL(purl.Type, purl.Namespace, purl.Name, latestVersion, purl.Qualifiers, purl.Subpath);

            NuGetPackageVersionMetadata? packageVersionMetadata =
                await Actions.GetMetadataAsync(purlWithVersion, useCache: useCache);

            if (packageVersionMetadata is null)
            {
                return null;
            }

            NuGetPackageVersionMetadata metadata = packageVersionMetadata;

            metadata.RepositoryUrl = new Uri(ENV_NUGET_ENDPOINT_API);
            // metadata.Platform = "NUGET";
            // metadata.Language = "C#";
            metadata.PackageUri = new Uri($"{ENV_NUGET_HOMEPAGE}/{packageVersionMetadata.Name.ToLowerInvariant()}");
            metadata.PackageMetadataUri = new Uri($"{RegistrationEndpoint}{packageVersionMetadata.Name.ToLowerInvariant()}/index.json");

            metadata.Version = purlWithVersion.Version;
            metadata.LatestPackageVersion = latestVersion;

            // Get the metadata for either the specified package version, or the latest package version
            await UpdateVersionMetadata(metadata, packageVersionMetadata);

            return metadata;
        }

        /// <summary>
        /// Updates the package version specific values in <see cref="NuGetPackageVersionMetadata"/>.
        /// </summary>
        /// <param name="finalMetadata">The <see cref="NuGetPackageVersionMetadata"/> object to update with the values for this version.</param>
        /// <param name="packageVersionMetadata">The <see cref="NuGetPackageVersionMetadata"/> representing this version.</param>
        private async Task UpdateVersionMetadata(NuGetPackageVersionMetadata finalMetadata, NuGetPackageVersionMetadata packageVersionMetadata)
        {
            // Construct the artifact contents url.
            finalMetadata.SourceArtifactUri = GetNupkgUrl(packageVersionMetadata.Name, finalMetadata.Version);

            // TODO: size and hash

            // Authors and Maintainers
            UpdateMetadataAuthorsAndMaintainers(finalMetadata, packageVersionMetadata);

            // Repository
            await UpdateMetadataRepository(finalMetadata);

            // Dependencies
            IList<PackageDependencyGroup> dependencyGroups = packageVersionMetadata.DependencySets.ToList();
            finalMetadata.Dependencies ??= dependencyGroups.SelectMany(group => group.Packages, (dependencyGroup, package) => new { dependencyGroup, package})
                .Select(dependencyGroupAndPackage => new Dependency() { Package = dependencyGroupAndPackage.package.ToString(), Framework = dependencyGroupAndPackage.dependencyGroup.TargetFramework?.ToString()})
                .ToList();

            // Licenses
            if (packageVersionMetadata.LicenseMetadata is not null)
            {
                // TODO: Idk if this is right?
                finalMetadata.Licenses ??= new List<License>();
                
                List<License> licenses = new()
                {
                    new License
                    {
                        Name = packageVersionMetadata.LicenseMetadata.License,
                        Url = packageVersionMetadata.LicenseMetadata.LicenseUrl.ToString()
                    }
                };

                finalMetadata.Licenses = ((List<License>)finalMetadata.Licenses).Concat(licenses);
            }
        }

        /// <summary>
        /// Updates the author(s) and maintainer(s) in <see cref="NuGetPackageVersionMetadata"/> for this package version.
        /// </summary>
        /// <param name="finalMetadata">The <see cref="NuGetPackageVersionMetadata"/> object to set the author(s) and maintainer(s) for this version.</param>
        /// <param name="packageVersionMetadata">The <see cref="NuGetPackageVersionMetadata"/> representing this version.</param>
        private static void UpdateMetadataAuthorsAndMaintainers(NuGetPackageVersionMetadata finalMetadata, NuGetPackageVersionMetadata packageVersionMetadata)
        {
            // Author(s)
            // TODO: CSV string to list instead.
            finalMetadata.Authors = packageVersionMetadata.Authors;

            // TODO: Collect the data about a package's maintainers as well.
        }

        /// <summary>
        /// Updates the <see cref="Repository"/> for this package version in the <see cref="NuGetPackageVersionMetadata"/>.
        /// </summary>
        /// <param name="finalMetadata">The <see cref="NuGetPackageVersionMetadata"/> object to update with the values for this version.</param>
        private async Task UpdateMetadataRepository(NuGetPackageVersionMetadata finalMetadata)
        {
            NuspecReader? nuspecReader = GetNuspec(finalMetadata.Name, finalMetadata.Version);
            RepositoryMetadata? repositoryMetadata = nuspecReader?.GetRepositoryMetadata();

            if (repositoryMetadata != null && GitHubProjectManager.IsGitHubRepoUrl(repositoryMetadata.Url, out PackageURL? githubPurl))
            {
                Repository ghRepository = new()
                {
                    Type = "github"
                };
                
                await ghRepository.ExtractRepositoryMetadata(githubPurl!);

                finalMetadata.SourceCodeRepository = ghRepository;
            }
        }

        /// <summary>
        /// Helper method to get the URL to download a NuGet package's .nupkg.
        /// </summary>
        /// <param name="id">The id/name of the package to get the .nupkg for.</param>
        /// <param name="version">The version of the package to get the .nupkg for.</param>
        /// <returns>The URL for the nupkg file.</returns>
        private static Uri GetNupkgUrl(string id, string version)
        {
            string lowerId = id.ToLowerInvariant();
            string lowerVersion = NuGetVersion.Parse(version).ToNormalizedString().ToLowerInvariant();
            string url = $"{NUGET_DEFAULT_CONTENT_ENDPOINT.TrimEnd('/')}/{lowerId}/{lowerVersion}/{lowerId}.{lowerVersion}.nupkg";
            return new Uri(url);
        }

        /// <summary>
        /// Searches the package manager metadata to figure out the source code repository.
        /// </summary>
        /// <param name="purl">The <see cref="PackageURL"/> that we need to find the source code repository.</param>
        /// <param name="metadata">The json representation of this package's metadata.</param>
        /// <remarks>If no version specified, defaults to latest version.</remarks>
        /// <returns>
        /// A dictionary, mapping each possible repo source entry to its probability/empty dictionary
        /// </returns>
        protected override async Task<Dictionary<PackageURL, double>> SearchRepoUrlsInPackageMetadata(PackageURL purl, string metadata)
        {
            Dictionary<PackageURL, double> mapping = new();
            try
            {
                string? version = purl.Version;
                if (string.IsNullOrEmpty(version))
                {
                    version = (await EnumerateVersionsAsync(purl)).First();
                }
                NuspecReader? nuspecReader = GetNuspec(purl.Name, version);
                RepositoryMetadata? repositoryMetadata = nuspecReader?.GetRepositoryMetadata();
                if (repositoryMetadata != null && GitHubProjectManager.IsGitHubRepoUrl(repositoryMetadata.Url, out PackageURL? githubPurl))
                {
                    if (githubPurl != null)
                    {
                        mapping.Add(githubPurl, 1.0F);
                    }
                }
                
                return mapping;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, $"Error fetching/parsing NuGet repository metadata: {ex.Message}");
            }

            // If nothing worked, return the default empty dictionary
            return mapping;
        }
        
        private NuspecReader? GetNuspec(string id, string version)
        {
            string lowerId = id.ToLowerInvariant();
            string lowerVersion = NuGetVersion.Parse(version).ToNormalizedString().ToLowerInvariant();
            string uri = $"{NUGET_DEFAULT_CONTENT_ENDPOINT.TrimEnd('/')}/{lowerId}/{lowerVersion}/{lowerId}.nuspec";
            try
            {
                HttpClient httpClient = this.CreateHttpClient();
                HttpResponseMessage response = httpClient.GetAsync(uri).GetAwaiter().GetResult();
                using (Stream stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                {
                    return new NuspecReader(stream);
                }
            }
            catch
            {
                return null;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0044:Add readonly modifier", Justification = "Modified through reflection.")]
        private static string ENV_NUGET_HOMEPAGE = "https://www.nuget.org/packages";
    }
}
