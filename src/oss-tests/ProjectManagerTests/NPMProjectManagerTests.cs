// Copyright (c) Microsoft Corporation. Licensed under the MIT License.


namespace Microsoft.CST.OpenSource.Tests.ProjectManagerTests
{
    using Extensions;
    using Model;
    using Moq;
    using OpenSource.Helpers;
    using oss;
    using PackageManagers;
    using PackageUrl;
    using RichardSzalay.MockHttp;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class NPMProjectManagerTests
    {
        private readonly IDictionary<string, string> _packages = new Dictionary<string, string>()
        {
            { "https://registry.npmjs.org/lodash", Resources.lodash_json },
            { "https://registry.npmjs.org/%40angular/core", Resources.angular_core_json },
            { "https://registry.npmjs.org/ds-modal", Resources.ds_modal_json },
            { "https://registry.npmjs.org/monorepolint", Resources.monorepolint_json },
            { "https://registry.npmjs.org/rly-cli", Resources.rly_cli_json },
            { "https://registry.npmjs.org/example", Resources.minimum_json_json },
        }.ToImmutableDictionary();

        private readonly NPMProjectManager _projectManager;
        
        public NPMProjectManagerTests()
        {
            Mock<IHttpClientFactory> mockFactory = new();
            
            MockHttpMessageHandler mockHttp = new();

            foreach ((string url, string json) in _packages)
            {
                MockHttpFetchResponse(HttpStatusCode.OK, url, json, mockHttp);
            }

 
            mockFactory.Setup(_ => _.CreateClient(It.IsAny<string>())).Returns(mockHttp.ToHttpClient());
            
            _projectManager = new NPMProjectManager(mockFactory.Object, ".");
        }

        [DataTestMethod]
        [DataRow("pkg:npm/lodash@4.17.15", "Lodash modular utilities.")] // Normal package
        [DataRow("pkg:npm/%40angular/core@13.2.5", "Angular - the core framework")] // Scoped package
        [DataRow("pkg:npm/ds-modal@0.0.2", "")] // No Description at package level, and empty string description on version level
        [DataRow("pkg:npm/monorepolint@0.4.0")] // No Author property, and No Description
        [DataRow("pkg:npm/example@0.0.0")] // Pretty much only name, and version
        [DataRow("pkg:npm/rly-cli@0.0.2", "RLY CLI allows you to setup fungilble SPL tokens and call Rally token programs from the command line.")] // Author property is an empty string
        public async Task MetadataSucceeds(string purlString, string? description = null)
        {
            PackageURL purl = new(purlString);
            PackageMetadata? metadata = await _projectManager.GetPackageMetadataAsync(purl, useCache: false);

            Assert.IsNotNull(metadata);
            Assert.AreEqual(purl.GetFullName(), metadata.Name);
            Assert.AreEqual(purl.Version, metadata.PackageVersion);
            Assert.AreEqual(description, metadata.Description);
        }
        
        [DataTestMethod]
        [DataRow("pkg:npm/lodash@4.17.15", 114, "4.17.21")]
        [DataRow("pkg:npm/%40angular/core@13.2.5", 566, "13.2.6")]
        [DataRow("pkg:npm/ds-modal@0.0.2", 3, "0.0.2")]
        [DataRow("pkg:npm/monorepolint@0.4.0", 88, "0.4.0")]
        [DataRow("pkg:npm/example@0.0.0", 1, "0.0.0")]
        [DataRow("pkg:npm/rly-cli@0.0.2", 4, "0.0.4")]
        public async Task EnumerateVersionsSucceeds(string purlString, int count, string latestVersion)
        {
            PackageURL purl = new(purlString);
            List<string> versions = (await _projectManager.EnumerateVersionsAsync(purl, useCache: false)).ToList();

            Assert.AreEqual(count, versions.Count);
            Assert.AreEqual(latestVersion, versions.First());
        }
        
        [DataTestMethod]
        [DataRow("pkg:npm/lodash@4.17.15", "https://registry.npmjs.org/lodash/-/lodash-4.17.15.tgz")]
        [DataRow("pkg:npm/%40angular/core@13.2.5", "https://registry.npmjs.org/%40angular/core/-/core-13.2.5.tgz")]
        [DataRow("pkg:npm/ds-modal@0.0.2", "https://registry.npmjs.org/ds-modal/-/ds-modal-0.0.2.tgz")]
        [DataRow("pkg:npm/monorepolint@0.4.0", "https://registry.npmjs.org/monorepolint/-/monorepolint-0.4.0.tgz")]
        [DataRow("pkg:npm/example@0.0.0", "https://registry.npmjs.org/example/-/example-0.0.0.tgz")]
        [DataRow("pkg:npm/rly-cli@0.0.2", "https://registry.npmjs.org/rly-cli/-/rly-cli-0.0.2.tgz")]
        public async Task GetArtifactDownloadUrisSucceeds(string purlString, string expectedUri)
        {
            PackageURL purl = new(purlString);
            List<ArtifactUri> uris = await _projectManager.GetArtifactDownloadUrisAsync(purl).ToListAsync();

            Assert.AreEqual(expectedUri, uris.First().Uri.AbsoluteUri);
            Assert.AreEqual(".tgz", uris.First().Extension);
            Assert.AreEqual(NPMProjectManager.NPMArtifactType.Tarball, uris.First().Type);
        }
        
        private static void MockHttpFetchResponse(
            HttpStatusCode statusCode,
            string url,
            string content,
            MockHttpMessageHandler httpMock)
        {
            httpMock
                .When(HttpMethod.Get, url)
                .Respond(statusCode, "application/json", content);
            httpMock.When(HttpMethod.Get, $"{url}/*.tgz").Respond(statusCode);

        }
    }
}
