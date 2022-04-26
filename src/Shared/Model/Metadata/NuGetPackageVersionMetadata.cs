// Copyright (c) Microsoft Corporation. Licensed under the MIT License.

namespace Microsoft.CST.OpenSource.Model.Metadata;

using Newtonsoft.Json;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.ComponentModel;

/// <summary>
/// A class to represent Package Metadata for a NuGet package version.
/// </summary>
public record NuGetPackageVersionMetadata : BasePackageVersionMetadata
{
    [JsonProperty(PropertyName = JsonProperties.Authors)]
    [JsonConverter(typeof(MetadataFieldConverter))]
    public string Authors { get; protected internal set; }

    [JsonProperty(PropertyName = JsonProperties.DependencyGroups, ItemConverterType = typeof(PackageDependencyGroupConverter))]
    public IEnumerable<PackageDependencyGroup> DependencySetsInternal { get; protected internal set; }

    [JsonIgnore]
    public IEnumerable<PackageDependencyGroup> DependencySets => DependencySetsInternal;

    [JsonProperty(PropertyName = JsonProperties.Description)]
    public new string Description { get; protected internal set; }

    [JsonProperty(PropertyName = JsonProperties.DownloadCount)]
    public long? DownloadCount { get; protected internal set; }

    [JsonProperty(PropertyName = JsonProperties.IconUrl)]
    [JsonConverter(typeof(SafeUriConverter))]
    public Uri IconUrl { get; protected internal set; }

    [JsonIgnore]
    public PackageIdentity Identity => new(Name, new NuGetVersion(Version));

    [JsonProperty(PropertyName = JsonProperties.Version)]
    public new string Version { get; protected internal set; }

    [JsonProperty(PropertyName = JsonProperties.LicenseUrl)]
    [JsonConverter(typeof(SafeUriConverter))]
    public Uri LicenseUrl { get; protected internal set; }

    [JsonProperty(PropertyName = JsonProperties.ProjectUrl)]
    [JsonConverter(typeof(SafeUriConverter))]
    public new Uri Homepage { get; protected internal set; }

    [JsonProperty(PropertyName = JsonProperties.ReadmeUrl)]
    [JsonConverter(typeof(SafeUriConverter))]
    public Uri ReadmeUrl { get; protected internal set; }

    [JsonIgnore]
    public Uri ReportAbuseUrl { get; }

    [JsonProperty(PropertyName = "packageDetailsUrl")]
    [JsonConverter(typeof(SafeUriConverter))]
    public new Uri PackageVersionUri { get; protected internal set; }

    [JsonProperty(PropertyName = JsonProperties.Published)]
    public new DateTimeOffset? PublishTime { get; protected internal set; }

    [JsonProperty(PropertyName = JsonProperties.Owners)]
    [JsonConverter(typeof(MetadataFieldConverter))]
    public string Owners { get; protected internal set; }

    [JsonProperty(PropertyName = JsonProperties.PackageId)]
    public new string Name { get; protected internal set; }

    [JsonProperty(PropertyName = JsonProperties.RequireLicenseAcceptance, DefaultValueHandling = DefaultValueHandling.Populate)]
    [DefaultValue(false)]
    [JsonConverter(typeof(SafeBoolConverter))]
    public bool RequireLicenseAcceptance { get; protected internal set; }

    [JsonProperty(PropertyName = JsonProperties.Summary)]
    public string Summary { get; protected internal set; }

    [JsonProperty(PropertyName = JsonProperties.Tags)]
    [JsonConverter(typeof(MetadataFieldConverter))]
    private string Tags;

    /// <summary>
    /// Map the CSV <see cref="Tags"/> to <see cref="Keywords"/>.
    /// </summary>
    public new IEnumerable<string> Keywords => new List<string>(Tags.Split(", "));

    [JsonProperty(PropertyName = JsonProperties.Title)]
    public string Title { get; protected internal set; }

    [JsonProperty(PropertyName = JsonProperties.Listed)]
    public bool IsListed { get; protected internal set; } // Not listed, but doesn't mean deprecated.
    
    public new string Deprecated { get; protected internal set; }
    
    [JsonProperty(PropertyName = JsonProperties.Deprecation)]
    public PackageDeprecationMetadata Deprecation { get; protected internal set; }

    [JsonProperty(PropertyName = JsonProperties.PrefixReserved)]
    public bool PrefixReserved { get; protected internal set; }

    [JsonIgnore]
    public LicenseMetadata LicenseMetadata { get; }

    [JsonProperty(PropertyName = JsonProperties.Vulnerabilities)]
    public IEnumerable<PackageVulnerabilityMetadata> Vulnerabilities { get; protected internal set; }

    [JsonProperty(PropertyName = JsonProperties.SubjectId)]
    public new Uri PackageVersionMetadataUri { get; protected internal set; }

    public new enum ArtifactType
    {
        Unknown = 0,
        Nupkg,
        Nuspec,
    }

    /// <summary>
    /// protected internal serialize an instance of <see cref="NuGetPackageVersionMetadata"/> using the <see cref="JsonConstructorAttribute"/>.
    /// </summary>
    /// <remarks>Necessary for unit test implementation of json serialization and deserialization.</remarks>
    [JsonConstructor]
#pragma warning disable CS8618
    public NuGetPackageVersionMetadata()
#pragma warning restore CS8618
    {}

    /// <summary>
    /// protected internal serialize an instance of <see cref="NuGetPackageVersionMetadata"/> using values from a <see cref="PackageSearchMetadataRegistration"/>.
    /// </summary>
    /// <param name="registration">The <see cref="PackageSearchMetadataRegistration"/> to get the values from.</param>
    public NuGetPackageVersionMetadata(PackageSearchMetadataRegistration registration)
    {
        Authors = registration.Authors;
        DependencySetsInternal = registration.DependencySets;
        Description = registration.Description;
        DownloadCount = registration.DownloadCount;
        IconUrl = registration.IconUrl;
        Name = registration.PackageId;
        Version = registration.Version.ToString();
        LicenseUrl = registration.LicenseUrl;
        Homepage = registration.ProjectUrl;
        ReadmeUrl = registration.ReadmeUrl;
        ReportAbuseUrl = registration.ReportAbuseUrl;
        PackageVersionUri = registration.PackageDetailsUrl;
        PublishTime = registration.Published;
        Owners = registration.Owners;
        RequireLicenseAcceptance = registration.RequireLicenseAcceptance;
        Summary = registration.Summary;
        Tags = registration.Tags;
        Title = registration.Title;
        IsListed = registration.IsListed;
        PrefixReserved = registration.PrefixReserved;
        LicenseMetadata = registration.LicenseMetadata;
        Vulnerabilities = registration.Vulnerabilities;
        PackageVersionMetadataUri = registration.CatalogUri;
        Deprecated = registration.DeprecationMetadata.Message;
        Deprecation = registration.DeprecationMetadata;
    }
} 