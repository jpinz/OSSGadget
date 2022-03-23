﻿// Copyright (c) Microsoft Corporation. Licensed under the MIT License.

namespace Microsoft.CST.OpenSource.PackageManagers
{
    using Contracts;
    using PackageUrl;
    using Model;
    using Model.Metadata;
    using Model.Providers;
    using NuGet.Packaging;
    using NuGet.Packaging.Core;
    using NuGet.Versioning;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading.Tasks;

    public class NuGetProjectManager : BaseProjectManager
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0044:Add readonly modifier", Justification = "Modified through reflection.")]
        public const string ENV_NUGET_ENDPOINT_API = "https://api.nuget.org";
        public const string ENV_NUGET_ENDPOINT = "https://www.nuget.org";
        public const string NUGET_DEFAULT_REGISTRATION_ENDPOINT = "https://api.nuget.org/v3/registration5-gz-semver2/";
        private const string NUGET_DEFAULT_CONTENT_ENDPOINT = "https://api.nuget.org/v3-flatcontainer/";

        private string? RegistrationEndpoint { get; set; } = null;

        public NuGetProjectManager(IHttpClientFactory httpClientFactory, IManagerProvider<IManagerMetadata>? provider, string destinationDirectory) : base(httpClientFactory, provider, destinationDirectory)
        {
            GetRegistrationEndpointAsync().Wait();
        }
        
        public NuGetProjectManager(IManagerProvider<IManagerMetadata>? provider, string destinationDirectory) : base(provider, destinationDirectory)
        {
            GetRegistrationEndpointAsync().Wait();
        }

        public NuGetProjectManager(string destinationDirectory) : base(destinationDirectory)
        {
            GetRegistrationEndpointAsync().Wait();
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
        /// Download one NuGet package and extract it to the target directory.
        /// </summary>
        /// <remarks>The target directory is defined when creating the <see cref="NuGetProjectManager"/> in the subdirectory named `nuget-{packagename}@{packageversion}`</remarks>
        /// <param name="purl">The <see cref="PackageURL"/> of the package to download.</param>
        /// <param name="doExtract">If the contents of the .nupkg should be extracted into a directory.</param>
        /// <param name="cached">If the downloaded contents should be retrieved from the cache if they exist there.</param>
        /// <returns>An IEnumerable list of the path(s) the contents were downloaded to.</returns>
        public override async Task<IEnumerable<string>> DownloadVersion(PackageURL purl, bool doExtract, bool cached = false)
        {
            Logger.Trace("DownloadVersion {0}", purl.ToString());

            string? packageName = purl.Name;
            string? packageVersion = purl.Version;
            List<string> downloadedPaths = new();

            if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(packageVersion))
            {
                Logger.Debug("Unable to download [{0} {1}]. Both must be defined.", packageName, packageVersion);
                return downloadedPaths;
            }

            try
            {
                string targetName = $"nuget-{packageName}@{packageVersion}";
                string extractionPath = Path.Combine(TopLevelExtractionDirectory, targetName);
                if (doExtract && Directory.Exists(extractionPath) && cached)
                {
                    downloadedPaths.Add(extractionPath);
                    return downloadedPaths;
                }

                string? downloaded =
                    await Provider.DownloadAsync(this, purl, extractionPath, doExtract, cached: cached);

                if (!string.IsNullOrWhiteSpace(downloaded))
                {
                    downloadedPaths.Add(downloaded);
                }

                return downloadedPaths;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Error downloading NuGet package: {0}", ex.Message);
            }
            return downloadedPaths;
        }

        /// <inheritdoc />
        public override async Task<bool> PackageExists(PackageURL purl, bool useCache = true)
        {
            Logger.Trace("PackageExists {0}", purl?.ToString());
            if (string.IsNullOrEmpty(purl?.Name))
            {
                Logger.Trace("Provided PackageURL was null.");
                return false;
            }

            return await Provider.DoesPackageExistAsync(purl, useCache: useCache);
        }

        /// <inheritdoc />
        public override async Task<IEnumerable<string>> EnumerateVersions(PackageURL purl, bool useCache = true)
        {
            Logger.Trace("EnumerateVersions {0}", purl.ToString());

            if (purl.Name is null)
            {
                return new List<string>();
            }

            try
            {
                IEnumerable<string> versions = await Provider.GetAllVersionsAsync(purl, useCache: useCache);

                // Sort versions, highest first, lowest last.
                return SortVersions(versions);
            }
            catch (Exception ex)
            {
                Logger.Debug("Unable to enumerate versions: {0}", ex.Message);
                throw;
            }
        }

        /// <inheritdoc />
        public override async Task<string?> GetMetadata(PackageURL purl, bool useCache = true)
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
                    string latestVersion = (await EnumerateVersions(purl, useCache)).First();
                    packageVersion = latestVersion;
                }

                // Construct a new PackageURL that's guaranteed to have a  version.
                PackageURL purlWithVersion = new (purl.Type, purl.Namespace, packageName, packageVersion, purl.Qualifiers, purl.Subpath);
                
                NuGetMetadata? packageVersionMetadata =
                    await Provider.GetMetadataAsync(purlWithVersion, useCache: useCache) as NuGetMetadata;
                
                return packageVersionMetadata?.ToJson();
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
        public override async Task<PackageMetadata> GetPackageMetadata(PackageURL purl, bool useCache = true)
        {
            string latestVersion = (await EnumerateVersions(purl, useCache)).First();

            // Construct a new PackageURL that's guaranteed to have a version, the latest version is used if no version was provided.
            PackageURL purlWithVersion = !string.IsNullOrWhiteSpace(purl.Version) ? 
                purl : new PackageURL(purl.Type, purl.Namespace, purl.Name, latestVersion, purl.Qualifiers, purl.Subpath);

            NuGetMetadata packageVersionMetadata =
                await Provider.GetMetadataAsync(purlWithVersion, useCache: useCache) as NuGetMetadata ?? throw new InvalidOperationException($"The metadata for {purlWithVersion} was null.");

            PackageMetadata metadata = new();

            metadata.Name = packageVersionMetadata.Name;
            metadata.Description = packageVersionMetadata.Description;

            metadata.PackageManagerUri = ENV_NUGET_ENDPOINT_API;
            metadata.Platform = "NUGET";
            metadata.Language = "C#";
            metadata.PackageUri = $"{ENV_NUGET_HOMEPAGE}/{packageVersionMetadata.Name.ToLowerInvariant()}";
            metadata.ApiPackageUri = $"{RegistrationEndpoint}{packageVersionMetadata.Name.ToLowerInvariant()}/index.json";

            metadata.PackageVersion = purl.Version ?? latestVersion;
            metadata.LatestPackageVersion = latestVersion;

            // Get the metadata for either the specified package version, or the latest package version
            await UpdateVersionMetadata(metadata, packageVersionMetadata);

            return metadata;
        }

        /// <summary>
        /// Updates the package version specific values in <see cref="PackageMetadata"/>.
        /// </summary>
        /// <param name="metadata">The <see cref="PackageMetadata"/> object to update with the values for this version.</param>
        /// <param name="packageVersionMetadata">The <see cref="NuGetMetadata"/> representing this version.</param>
        private async Task UpdateVersionMetadata(PackageMetadata metadata, NuGetMetadata packageVersionMetadata)
        {
            if (metadata.PackageVersion is null)
            {
                return;
            }

            string nameLowercase = packageVersionMetadata.Name.ToLowerInvariant();

            // Set the version specific URI values.
            metadata.VersionUri = $"{metadata.PackageManagerUri}/packages/{nameLowercase}/{metadata.PackageVersion}";
            metadata.ApiVersionUri = packageVersionMetadata.CatalogUri.ToString();
            
            // Construct the artifact contents url.
            metadata.VersionDownloadUri = GetNupkgUrl(packageVersionMetadata.Name, metadata.PackageVersion);

            // TODO: size and hash

            // Homepage url
            metadata.Homepage = packageVersionMetadata.ProjectUrl?.ToString();

            // Authors and Maintainers
            UpdateMetadataAuthorsAndMaintainers(metadata, packageVersionMetadata);

            // Repository
            await UpdateMetadataRepository(metadata);

            // Dependencies
            IList<PackageDependencyGroup> dependencyGroups = packageVersionMetadata.DependencySets.ToList();
            if (dependencyGroups.Any())
            {
                metadata.Dependencies ??= new List<Dependency>();

                foreach (PackageDependencyGroup dependencyGroup in dependencyGroups)
                {
                    dependencyGroup.Packages.ToList().ForEach((dependency) => metadata.Dependencies.Add(new Dependency() { Package = dependency.ToString(), Framework = dependencyGroup.TargetFramework?.ToString()}));
                }
            }

            // Keywords
            metadata.Keywords = new List<string>(packageVersionMetadata.Tags.Split(", "));

            // Licenses
            if (packageVersionMetadata.LicenseMetadata is not null)
            {
                metadata.Licenses ??= new List<License>();
                metadata.Licenses.Add(new License()
                {
                    Name = packageVersionMetadata.LicenseMetadata.License,
                    Url = packageVersionMetadata.LicenseMetadata.LicenseUrl.ToString()
                });
            }

            // publishing info
            metadata.UploadTime = packageVersionMetadata.Published?.ToString("MM/dd/yy HH:mm:ss zz");
        }

        /// <summary>
        /// Updates the author(s) and maintainer(s) in <see cref="PackageMetadata"/> for this package version.
        /// </summary>
        /// <param name="metadata">The <see cref="PackageMetadata"/> object to set the author(s) and maintainer(s) for this version.</param>
        /// <param name="packageVersionMetadata">The <see cref="NuGetMetadata"/> representing this version.</param>
        private static void UpdateMetadataAuthorsAndMaintainers(PackageMetadata metadata, NuGetMetadata packageVersionMetadata)
        {
            // Author(s)
            string? authors = packageVersionMetadata.Authors;
            if (authors is not null)
            {
                metadata.Authors ??= new List<User>();
                authors.Split(", ").ToList()
                    .ForEach(author => metadata.Authors.Add(new User() { Name = author }));
            }

            // TODO: Maintainers
        }

        /// <summary>
        /// Updates the <see cref="Repository"/> for this package version in the <see cref="PackageMetadata"/>.
        /// </summary>
        /// <param name="metadata">The <see cref="PackageMetadata"/> object to update with the values for this version.</param>
        private async Task UpdateMetadataRepository(PackageMetadata metadata)
        {
            NuspecReader? nuspecReader = GetNuspec(metadata.Name!, metadata.PackageVersion!);
            RepositoryMetadata? repositoryMetadata = nuspecReader?.GetRepositoryMetadata();

            if (repositoryMetadata != null && GitHubProjectManager.IsGitHubRepoUrl(repositoryMetadata.Url, out PackageURL? githubPurl))
            {
                Repository ghRepository = new()
                {
                    Type = "github"
                };
                
                await ghRepository.ExtractRepositoryMetadata(githubPurl!);

                metadata.Repository ??= new List<Repository>();
                metadata.Repository.Add(ghRepository);
            }
        }

        /// <summary>
        /// Helper method to get the URL to download a NuGet package's .nupkg.
        /// </summary>
        /// <param name="id">The id/name of the package to get the .nupkg for.</param>
        /// <param name="version">The version of the package to get the .nupkg for.</param>
        /// <returns>The URL for the nupkg file.</returns>
        private static string GetNupkgUrl(string id, string version)
        {
            string lowerId = id.ToLowerInvariant();
            string lowerVersion = NuGetVersion.Parse(version).ToNormalizedString().ToLowerInvariant();
            string url = $"{NUGET_DEFAULT_CONTENT_ENDPOINT.TrimEnd('/')}/{lowerId}/{lowerVersion}/{lowerId}.{lowerVersion}.nupkg";
            return url;
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
                    version = (await EnumerateVersions(purl)).First();
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