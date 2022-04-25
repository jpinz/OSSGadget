// Copyright (c) Microsoft Corporation. Licensed under the MIT License.

namespace Microsoft.CST.OpenSource.PackageManagers
{
    using Extensions;
    using Helpers;
    using Microsoft.CST.OpenSource.Model;
    using Model.Metadata;
    using PackageUrl;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Utilities;
    using Version = SemanticVersioning.Version;

    public class NPMProjectManager : TypedProjectManager<NpmPackageVersionMetadata>
    {
        /// <summary>
        /// The type of the project manager from the package-url type specifications.
        /// </summary>
        /// <seealso href="https://www.github.com/package-url/purl-spec/blob/master/PURL-TYPES.rst"/>
        public const string Type = "npm";

        public override string ManagerType => Type;

        public static string ENV_NPM_API_ENDPOINT { get; set; } = "https://registry.npmjs.org";
        public static string ENV_NPM_ENDPOINT { get; set; } = "https://www.npmjs.com";

        public NPMProjectManager(IHttpClientFactory httpClientFactory, string destinationDirectory) : base( httpClientFactory, directory: destinationDirectory)
        {
        }

        /// <inheritdoc />
        public override IEnumerable<ArtifactUri<Enum>> GetArtifactDownloadUris(PackageURL purl)
        {
            string feedUrl = (purl.Qualifiers?["repository_url"] ?? ENV_NPM_API_ENDPOINT).EnsureTrailingSlash();

            string artifactUri = purl.HasNamespace() ? 
                $"{feedUrl}{purl.GetNamespaceFormatted()}/{purl.Name}/-/{purl.Name}-{purl.Version}.tgz" : // If there's a namespace.
                $"{feedUrl}{purl.Name}/-/{purl.Name}-{purl.Version}.tgz"; // If there isn't a namespace.
            yield return new ArtifactUri<Enum>(NpmPackageVersionMetadata.ArtifactType.Tarball, artifactUri);
        }

        /// <summary>
        /// Download one NPM package and extract it to the target directory.
        /// </summary>
        /// <param name="purl">Package URL of the package to download.</param>
        /// <returns>n/a</returns>
        public override async Task<IEnumerable<string>> DownloadVersionAsync(PackageURL purl, bool doExtract, bool cached = false)
        {
            Logger.Trace("DownloadVersion {0}", purl?.ToString());

            string? packageName = purl?.Name;
            string? packageVersion = purl?.Version;
            List<string> downloadedPaths = new();

            // shouldn't happen here, but check
            if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(packageVersion))
            {
                Logger.Debug("Unable to download [{0} {1}]. Both must be defined.", packageName, packageVersion);
                return downloadedPaths;
            }

            try
            {
                HttpClient httpClient = CreateHttpClient();
                JsonDocument doc = await GetJsonCache(httpClient, $"{ENV_NPM_API_ENDPOINT}/{packageName}");
                string? tarball = doc.RootElement.GetProperty("versions").GetProperty(packageVersion).GetProperty("dist").GetProperty("tarball").GetString();
                HttpResponseMessage result = await httpClient.GetAsync(tarball);
                result.EnsureSuccessStatusCode();
                Logger.Debug("Downloading {0}...", purl?.ToString());
                string targetName = $"npm-{packageName}@{packageVersion}";
                string extractionPath = Path.Combine(TopLevelExtractionDirectory, targetName);
                if (doExtract && Directory.Exists(extractionPath) && cached == true)
                {
                    downloadedPaths.Add(extractionPath);
                    return downloadedPaths;
                }
                if (doExtract)
                {
                    downloadedPaths.Add(await ArchiveHelper.ExtractArchiveAsync(TopLevelExtractionDirectory, targetName, await result.Content.ReadAsStreamAsync(), cached));
                }
                else
                {
                    extractionPath += Path.GetExtension(tarball) ?? "";
                    await File.WriteAllBytesAsync(extractionPath, await result.Content.ReadAsByteArrayAsync());
                    downloadedPaths.Add(extractionPath);
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Error downloading NPM package: {0}", ex.Message);
            }
            return downloadedPaths;
        }

        /// <inheritdoc />
        public override async Task<bool> PackageExistsAsync(PackageURL purl, bool useCache = true)
        {
            Logger.Trace("PackageExists {0}", purl?.ToString());
            if (string.IsNullOrEmpty(purl?.Name))
            {
                Logger.Trace("Provided PackageURL was null.");
                return false;
            }
            string packageName = purl.GetFullName();
            HttpClient httpClient = CreateHttpClient();

            return await CheckJsonCacheForPackage(httpClient, $"{ENV_NPM_API_ENDPOINT}/{packageName}", useCache);
        }

        /// <inheritdoc />
        public override async Task<IEnumerable<string>> EnumerateVersionsAsync(PackageURL purl, bool useCache = true, bool includePrerelease = true)
        {
            Logger.Trace("EnumerateVersions {0}", purl?.ToString());
            if (purl?.Name is null)
            {
                return new List<string>();
            }

            try
            {
                string packageName = purl.GetFullName();
                HttpClient httpClient = CreateHttpClient();

                JsonDocument doc = await GetJsonCache(httpClient, $"{ENV_NPM_API_ENDPOINT}/{purl.GetFullName(encoded: true)}", useCache);

                List<string> versionList = new();

                foreach (JsonProperty versionKey in doc.RootElement.GetProperty("versions").EnumerateObject())
                {
                    Logger.Debug("Identified {0} version {1}.", packageName, versionKey.Name);
                    versionList.Add(versionKey.Name);
                }

                string? latestVersion = doc.RootElement.GetProperty("dist-tags").GetProperty("latest").GetString();
                
                // If there was no "latest" property for some reason.
                if (string.IsNullOrWhiteSpace(latestVersion))
                {
                    return SortVersions(versionList.Distinct());
                }

                Logger.Debug("Identified {0} latest version as {1}.", packageName, latestVersion);

                // Remove the latest version from the list of versions, so we can add it after sorting.
                versionList.Remove(latestVersion);
                
                // Sort the list of distinct versions.
                List<string> sortedList = SortVersions(versionList.Distinct()).ToList();
                
                // Insert the latest version at the beginning of the list.
                sortedList.Insert(0, latestVersion);

                return sortedList;
            }
            catch (Exception ex)
            {
                Logger.Debug("Unable to enumerate versions: {0}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Gets the latest version of the package
        /// </summary>
        /// <param name="contentJSON"></param>
        /// <returns></returns>
        public JsonElement? GetLatestVersionElement(JsonDocument contentJSON)
        {
            List<Version> versions = GetVersions(contentJSON);
            Version? maxVersion = GetLatestVersion(versions);
            if (maxVersion is null) { return null; }
            return GetVersionElement(contentJSON, maxVersion);
        }

        /// <inheritdoc />
        public override async Task<string?> GetMetadataAsync(PackageURL purl, bool useCache = true)
        {
            try
            {
                string? packageName = purl.HasNamespace() ? $"{purl.GetNamespaceFormatted()}/{purl.Name}" : purl.Name;
                HttpClient httpClient = CreateHttpClient();

                string? content = await GetHttpStringCache(httpClient, $"{ENV_NPM_API_ENDPOINT}/{packageName}", useCache);
                return content;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, $"Error fetching NPM metadata: {ex.Message}");
                return null;
            }
        }

        public override Uri GetPackageAbsoluteUri(PackageURL purl)
        {
            return new Uri(ENV_NPM_API_ENDPOINT.EnsureTrailingSlash() + (purl.HasNamespace() ? $"{purl.GetNamespaceFormatted()}/{purl.Name}" : purl.Name));
        }

        /// <inheritdoc />
        public override async Task<BasePackageVersionMetadata?> GetPackageMetadataAsync(PackageURL purl, bool useCache = true)
        {
            NpmPackageVersionMetadata metadata = new();
            string? content = await GetMetadataAsync(purl, useCache);
            if (string.IsNullOrEmpty(content)) { return null; }

            // convert NPM package data to normalized form
            JsonDocument contentJSON = JsonDocument.Parse(content);
            JsonElement root = contentJSON.RootElement;

            metadata.Name = root.GetProperty("name").GetString() ?? throw new InvalidOperationException();
            metadata.Description = OssUtilities.GetJSONPropertyStringIfExists(root, "description");

            metadata.RepositoryUrl = new Uri(ENV_NPM_API_ENDPOINT);
            // metadata.Platform = "NPM";
            // metadata.Language = "JavaScript";
            metadata.PackageUri = new Uri($"{ENV_NPM_ENDPOINT}/package/{metadata.Name}");
            metadata.PackageMetadataUri = new Uri($"{ENV_NPM_API_ENDPOINT}/{metadata.Name}");

            List<Version> versions = GetVersions(contentJSON);
            Version? latestVersion = GetLatestVersion(versions);

            if (purl.Version != null)
            {
                // find the version object from the collection
                metadata.Version = purl.Version;
            }
            else
            {
                metadata.Version = latestVersion is null ? "Unknown Version" : latestVersion.ToString(); // TODO: Handle this better.
            }

            // if we found any version at all, get the specifics.
            if (metadata.Version.IsNotBlank())
            {
                Version versionToGet = new(metadata.Version);
                JsonElement? versionElement = GetVersionElement(contentJSON, versionToGet);
                if (versionElement != null)
                {
                    // Set the version specific values.
                    metadata.PackageVersionUri = new Uri($"{ENV_NPM_ENDPOINT}/package/{metadata.Name}/v/{metadata.Version}");
                    metadata.PackageVersionMetadataUri = new Uri($"{ENV_NPM_API_ENDPOINT}/{metadata.Name}/{metadata.Version}");

                    // Prioritize the version level description
                    if (OssUtilities.GetJSONPropertyStringIfExists(versionElement, "description") is string description)
                    {
                        metadata.Description = description;
                    }
                    
                    JsonElement? distElement = OssUtilities.GetJSONPropertyIfExists(versionElement, "dist");
                    if (OssUtilities.GetJSONPropertyIfExists(distElement, "tarball") is JsonElement tarballElement)
                    {
                        string tarballUrl = tarballElement.ToString().IsBlank()
                            ? $"{ENV_NPM_API_ENDPOINT}/{metadata.Name}/-/{metadata.Name}-{metadata.Version}.tgz"
                            : tarballElement.ToString();
                        metadata.SourceArtifactUri = new Uri(tarballUrl);
                    }

                    if (OssUtilities.GetJSONPropertyIfExists(distElement, "integrity") is JsonElement integrityElement &&
                        integrityElement.ToString() is string integrity &&
                        integrity.Split('-') is string[] pair &&
                        pair.Length == 2)
                    {
                        metadata.PublisherSignature ??= new List<Digest>()
                        {
                            new Digest(Algorithm: pair[0], Signature: pair[1]),
                        };
                    }
                    
                    // size
                    if (OssUtilities.GetJSONPropertyIfExists(distElement, "unpackedSize") is JsonElement sizeElement &&
                        sizeElement.GetInt64() is long size)
                    {
                        metadata.UnpackedSize = size;
                    }

                    // check for typescript
                    /*List<string>? devDependencies = OssUtilities.ConvertJSONToList(OssUtilities.GetJSONPropertyIfExists(versionElement, "devDependencies"));
                    if (devDependencies is not null && devDependencies.Count > 0 && devDependencies.Any(stringToCheck => stringToCheck.Contains("\"typescript\":")))
                    {
                        metadata.Language = "TypeScript";
                    }*/

                    // homepage
                    if (OssUtilities.GetJSONPropertyStringIfExists(versionElement, "homepage") is string homepage &&
                        !string.IsNullOrWhiteSpace(homepage))
                    {
                        metadata.Homepage = new Uri(homepage);
                    }
                    
                    // commit id
                    /*if (OssUtilities.GetJSONPropertyStringIfExists(versionElement, "gitHead") is string gitHead &&
                        !string.IsNullOrWhiteSpace(gitHead))
                    {
                        metadata.CommitId = gitHead;
                    }*/

                    // install scripts
                    List<string>? scripts = OssUtilities.ConvertJSONToList(OssUtilities.GetJSONPropertyIfExists(versionElement, "scripts"));
                    if (scripts is not null && scripts.Count > 0)
                    {
                        List<NpmPackageVersionMetadata.NpmScript> npmScripts = new();
                        
                        scripts.ForEach((element) =>
                        {
                            string[] script = element.Split(":");
                            npmScripts.Add(new NpmPackageVersionMetadata.NpmScript(script[0], script[1]));
                        });
                        metadata.Scripts = npmScripts;
                    }

                    // dependencies
                    List<string>? dependencies = OssUtilities.ConvertJSONToList(OssUtilities.GetJSONPropertyIfExists(versionElement, "dependencies"));
                    if (dependencies is not null && dependencies.Count > 0)
                    {
                        List<Dependency> dependenciesList = new();
                        dependencies.ForEach((dependency) => dependenciesList.Add(new Dependency() { Package = dependency }));
                        metadata.Dependencies = dependenciesList;
                    }

                    // publisher
                    JsonElement? publisherElement = OssUtilities.GetJSONPropertyIfExists(versionElement, "_npmUser");
                    if (publisherElement is not null)
                    {
                        User publisher = new()
                        {
                            Name = OssUtilities.GetJSONPropertyStringIfExists(publisherElement, "name"),
                            Email = OssUtilities.GetJSONPropertyStringIfExists(publisherElement, "email"),
                            Url = OssUtilities.GetJSONPropertyStringIfExists(publisherElement, "url")
                        };

                        // TODO: Publisher as user object?
                        metadata.Publisher = publisher.ToString();
                    }

                    // TODO: Not yet implemented.
                    // maintainers
                    /*JsonElement? maintainersElement = OssUtilities.GetJSONPropertyIfExists(versionElement, "maintainers");
                    if (maintainersElement?.EnumerateArray() is JsonElement.ArrayEnumerator maintainerEnumerator)
                    {
                        metadata.Maintainers ??= new List<User>();
                        maintainerEnumerator.ToList().ForEach((element) =>
                        {
                            metadata.Maintainers.Add(
                                new User
                                {
                                    Name = OssUtilities.GetJSONPropertyStringIfExists(element, "name"),
                                    Email = OssUtilities.GetJSONPropertyStringIfExists(element, "email"),
                                    Url = OssUtilities.GetJSONPropertyStringIfExists(element, "url")
                                });
                        });
                    }*/

                    // repository
                    Dictionary<PackageURL, double> repoMappings = await SearchRepoUrlsInPackageMetadata(purl, content);
                    foreach (KeyValuePair<PackageURL, double> repoMapping in repoMappings)
                    {
                        Repository repository = new()
                        {
                            Rank = repoMapping.Value,
                            Type = repoMapping.Key.Type
                        };
                        await repository.ExtractRepositoryMetadata(repoMapping.Key);

                        metadata.SourceCodeRepository = repository;
                    }

                    // keywords
                    metadata.Keywords = OssUtilities.ConvertJSONToList(OssUtilities.GetJSONPropertyIfExists(versionElement, "keywords"));

                    // licenses
                    {
                        if (OssUtilities.GetJSONEnumerator(OssUtilities.GetJSONPropertyIfExists(versionElement, "licenses"))
                                is JsonElement.ArrayEnumerator enumeratorElement &&
                            enumeratorElement.ToList() is List<JsonElement> enumerator &&
                            enumerator.Any())
                        {
                            // TODO: Idk if this is right?
                            metadata.Licenses ??= new List<License>();
                            List<License> licenses = new();
                            // TODO: Convert/append SPIX_ID values?
                            enumerator.ForEach((license) =>
                            {
                                licenses.Add(new License()
                                {
                                    Name = OssUtilities.GetJSONPropertyStringIfExists(license, "type"),
                                    Url = OssUtilities.GetJSONPropertyStringIfExists(license, "url")
                                });
                            });
                            metadata.Licenses = ((List<License>)metadata.Licenses).Concat(licenses);
                        }
                    }
                }
            }

            if (latestVersion is not null)
            {
                metadata.LatestPackageVersion = latestVersion.ToString();
            }

            return metadata;
        }

        public override JsonElement? GetVersionElement(JsonDocument? contentJSON, Version version)
        {
            if (contentJSON is null) { return null; }
            JsonElement root = contentJSON.RootElement;

            try
            {
                JsonElement versionsJSON = root.GetProperty("versions");
                foreach (JsonProperty versionProperty in versionsJSON.EnumerateObject())
                {
                    if (string.Equals(versionProperty.Name, version.ToString(), StringComparison.InvariantCultureIgnoreCase))
                    {
                        return versionsJSON.GetProperty(version.ToString());
                    }
                }
            }
            catch (KeyNotFoundException) { return null; }
            catch (InvalidOperationException) { return null; }

            return null;
        }

        public override List<Version> GetVersions(JsonDocument? contentJSON)
        {
            List<Version> allVersions = new();
            if (contentJSON is null) { return allVersions; }

            JsonElement root = contentJSON.RootElement;
            try
            {
                JsonElement versions = root.GetProperty("versions");
                foreach (JsonProperty version in versions.EnumerateObject())
                {
                    allVersions.Add(new Version(version.Name));
                }
            }
            catch (KeyNotFoundException) { return allVersions; }
            catch (InvalidOperationException) { return allVersions; }

            return allVersions;
        }

        /// <summary>
        /// Searches the package manager metadata to figure out the source code repository
        /// </summary>
        /// <param name="purl">the package for which we need to find the source code repository</param>
        /// <returns>
        /// A dictionary, mapping each possible repo source entry to its probability/empty dictionary
        /// </returns>

        protected override async Task<Dictionary<PackageURL, double>> SearchRepoUrlsInPackageMetadata(PackageURL purl,
            string metadata)
        {
            if (string.IsNullOrEmpty(metadata))
            {
                return new Dictionary<PackageURL, double>();
            }
            JsonDocument contentJSON = JsonDocument.Parse(metadata);
            return await SearchRepoUrlsInPackageMetadata(purl, contentJSON);
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        protected async Task<Dictionary<PackageURL, double>> SearchRepoUrlsInPackageMetadata(PackageURL purl,
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
            JsonDocument contentJSON)
        {
            Dictionary<PackageURL, double>? mapping = new();
            if (purl.Name is string purlName && (purlName.StartsWith('_') || npm_internal_modules.Contains(purlName)))
            {
                // url = 'https://github.com/nodejs/node/tree/master/lib' + package.name,

                mapping.Add(new PackageURL(purl.Type, purl.Namespace, purl.Name,
                    null, null, "node/tree/master/lib"), 1.0F);
                return mapping;
            }

            // if a version is provided, search that JSONElement, otherwise, just search the latest
            // version, which is more likely best maintained
            // TODO: If the latest version JSONElement doesnt have the repo infor, should we search all elements
            // on that chance that one of them might have it?
            JsonElement? versionJSON = string.IsNullOrEmpty(purl?.Version) ? GetLatestVersionElement(contentJSON) :
                GetVersionElement(contentJSON, new Version(purl.Version));

            if (versionJSON is JsonElement notNullVersionJSON)
            {
                try
                {
                    if (!notNullVersionJSON.TryGetProperty("repository", out JsonElement repository))
                    {
                        return mapping;
                    }
                    if (repository.ValueKind == JsonValueKind.Object)
                    {
                        string? repoType = OssUtilities.GetJSONPropertyStringIfExists(repository, "type")?.ToLower();
                        string? repoURL = OssUtilities.GetJSONPropertyStringIfExists(repository, "url");

                        // right now we deal with only github repos
                        if (repoType == "git" && repoURL is not null)
                        {
                            PackageURL gitPURL = GitHubProjectManager.ParseUri(new Uri(repoURL));
                            // we got a repository value the author specified in the metadata - so no
                            // further processing needed
                            mapping.Add(gitPURL, 1.0F);
                            return mapping;
                        }
                    }
                }
                catch (KeyNotFoundException) { /* continue onwards */ }
                catch (UriFormatException) {  /* the uri specified in the metadata invalid */ }
            }

            return mapping;
        }

        /// <summary>
        /// Internal Node.js modules that should be ignored when searching metadata.
        /// </summary>
        private static readonly List<string> npm_internal_modules = new()
        {
            "assert",
            "async_hooks",
            "buffer",
            "child_process",
            "cluster",
            "console",
            "constants",
            "crypto",
            "dgram",
            "dns",
            "domain",
            "events",
            "fs",
            "http",
            "http2",
            "https",
            "inspector",
            "module",
            "net",
            "os",
            "path",
            "perf_hooks",
            "process",
            "punycode",
            "querystring",
            "readline",
            "repl",
            "stream",
            "string_decoder",
            "timers",
            "tls",
            "trace_events",
            "tty",
            "url",
            "util",
            "v8",
            "vm",
            "zlib"
        };
    }
}