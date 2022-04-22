// Copyright (c) Microsoft Corporation. Licensed under the MIT License.

namespace Microsoft.CST.OpenSource.Model.Metadata;

using Newtonsoft.Json;
using System;
using System.Collections.Generic;

public abstract record BasePackageVersionMetadata
{
    public string? Namespace { get; protected internal set; }
    public string Name { get; protected internal set; }
    public string Version { get; protected internal set; }
    public string Protocol { get; protected internal set; }
    public Uri RepositoryUrl { get; protected internal set; }
    public string? Publisher { get; protected internal set; }
    public string? Description { get; protected internal set; }
    public IEnumerable<string>? Keywords { get; protected internal set; }
    public Uri? Homepage { get; protected internal set; }
    public object? Licenses { get; protected internal set; }
    public string LatestPackageVersion { get; protected internal set; }
    public IEnumerable<Digest>? PublisherSignature { get; protected internal set; }
    public IEnumerable<Digest>? Digests { get; protected internal set; }
    public float? PackedSize { get; protected internal set; }
    public float? UnpackedSize { get; protected internal set; }
    public IEnumerable<object>? Dependencies { get; protected internal set; }
    public Uri PackageUri { get; protected internal set; }
    public Uri PackageMetadataUri { get; protected internal set; }
    public DateTimeOffset PublishTime { get; protected internal set; }
    public Uri? SourceArtifactUri { get; protected internal set; }
    public IEnumerable<Uri>? BinaryArtifactUris { get; protected internal set; }
    public Uri? PackageVersionUri { get; protected internal set; }
    public Uri? PackageVersionMetadataUri { get; protected internal set; }
    public string? Deprecated { get; protected internal set; }
    public object? SourceCodeRepository { get; protected internal set; }
    public object? Downloads { get; protected internal set; }
    
    public enum ArtifactType
    {
        Unknown = 0,
        Binary,
    }
    
    // construct the json format for the metadata
    public override string ToString()
    {
        return JsonConvert.SerializeObject(this, Formatting.Indented);
    }
        
    /// <summary>
    /// Converts the json/ToString() output of BasePackageVersionMetadata into a BasePackageVersionMetadata object.
    /// </summary>
    /// <param name="json">The JSON representation of a <see cref="BasePackageVersionMetadata"/> object.</param>
    /// <returns>The <see cref="BasePackageVersionMetadata"/> object constructed from the json.</returns>
    public static BasePackageVersionMetadata? FromJson(string json)
    {
        return JsonConvert.DeserializeObject<BasePackageVersionMetadata>(json);
    }
}