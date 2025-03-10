﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Mime;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Blob.Configs;
using Microsoft.Health.Dicom.Blob.Features.Export;
using Microsoft.Health.Dicom.Core.Extensions;
using Microsoft.Health.Dicom.Core.Features.Common;
using Microsoft.Health.Dicom.Core.Features.Export;
using Microsoft.Health.Dicom.Core.Models;
using Microsoft.Health.Dicom.Core.Models.Export;
using Microsoft.Health.Operations;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Dicom.Blob.UnitTests.Features.Export;

public class AzureBlobExportSinkProviderTests
{
    private readonly ISecretStore _secretStore;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly AzureBlobExportSinkProvider _sinkProvider;
    private readonly IExportIdentityProvider _identityProvider;
    private readonly IServiceProvider _serviceProvider;

    public AzureBlobExportSinkProviderTests()
    {
        _secretStore = Substitute.For<ISecretStore>();
        _serializerOptions = new JsonSerializerOptions();
        _serializerOptions.ConfigureDefaultDicomSettings();
        _sinkProvider = new AzureBlobExportSinkProvider(_secretStore, Options.Create(_serializerOptions), NullLogger<AzureBlobExportSinkProvider>.Instance);
        _identityProvider = Substitute.For<IExportIdentityProvider>();

        var services = new ServiceCollection();
        services.AddScoped(p => Substitute.For<IFileStore>());
        services.AddSingleton(_identityProvider);
        services.AddOptions<AzureBlobClientOptions>("Export");
        services.Configure<BlobOperationOptions>(o => o.Upload = new StorageTransferOptions());
        services.Configure<JsonSerializerOptions>(o => o.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

        _serviceProvider = services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task GivenCompletedOperation_WhenCleaningUp_SkipIfNoWork(bool configureStore, bool addSecret, bool expectDelete)
    {
        using var tokenSource = new CancellationTokenSource();

        var provider = configureStore
            ? new AzureBlobExportSinkProvider(_secretStore, Options.Create(_serializerOptions), NullLogger<AzureBlobExportSinkProvider>.Instance)
            : new AzureBlobExportSinkProvider(Options.Create(_serializerOptions), NullLogger<AzureBlobExportSinkProvider>.Instance);

        var options = new AzureBlobExportOptions
        {
            Secret = addSecret ? new SecretKey { Name = "secret" } : null
        };

        await provider.CompleteCopyAsync(options, tokenSource.Token);

        if (expectDelete)
            await _secretStore.Received(1).DeleteSecretAsync("secret", tokenSource.Token);
        else
            await _secretStore.DidNotReceiveWithAnyArgs().DeleteSecretAsync(default, default);
    }

    [Fact]
    public async Task GivenNoSecretStore_WhenCreatingSinkWithSecret_ThenThrow()
    {
        var containerUri = new Uri("https://unit-test.blob.core.windows.net/mycontainer?sv=2020-08-04&ss=b", UriKind.Absolute);
        var errorHref = new Uri($"https://unit-test.blob.core.windows.net/mycontainer/{Guid.NewGuid()}/Errors.log", UriKind.Absolute);
        var options = new AzureBlobExportOptions
        {
            Secret = new SecretKey { Name = "foo", Version = "bar" },
        };

        var provider = new AzureBlobExportSinkProvider(Options.Create(_serializerOptions), NullLogger<AzureBlobExportSinkProvider>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CreateAsync(_serviceProvider, options, Guid.NewGuid()));
        await _secretStore.DidNotReceiveWithAnyArgs().GetSecretAsync(default, default, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenBlobContainerUri_WhenCreatingSink_ThenCreateFromServiceContainer(bool useManagedIdentity)
    {
        const string version = "1";
        var operationId = Guid.NewGuid();
        var containerUri = new Uri("https://unit-test.blob.core.windows.net/mycontainer?sv=2020-08-04&ss=b", UriKind.Absolute);
        var errorHref = new Uri($"https://unit-test.blob.core.windows.net/mycontainer/{operationId.ToString(OperationId.FormatSpecifier)}/Errors.log", UriKind.Absolute);
        var options = new AzureBlobExportOptions
        {
            Secret = new SecretKey
            {
                Name = operationId.ToString(OperationId.FormatSpecifier),
                Version = version,
            },
            UseManagedIdentity = useManagedIdentity,
        };

        using var tokenSource = new CancellationTokenSource();

        _secretStore
            .GetSecretAsync(operationId.ToString(OperationId.FormatSpecifier), version, tokenSource.Token)
            .Returns(GetJson(containerUri));

        if (useManagedIdentity)
            _identityProvider.GetCredentialAsync(tokenSource.Token).Returns(new DefaultAzureCredential());

        IExportSink sink = await _sinkProvider.CreateAsync(_serviceProvider, options, operationId, tokenSource.Token);

        Assert.IsType<AzureBlobExportSink>(sink);
        Assert.Equal(errorHref, sink.ErrorHref);
        await _secretStore
            .Received(1)
            .GetSecretAsync(operationId.ToString(OperationId.FormatSpecifier), version, tokenSource.Token);

        if (useManagedIdentity)
            await _identityProvider.Received(1).GetCredentialAsync(tokenSource.Token);
        else
            await _identityProvider.DidNotReceiveWithAnyArgs().GetCredentialAsync(default);
    }

    [Fact]
    public async Task GivenConnectionString_WhenCreatingSink_ThenCreateFromServiceContainer()
    {
        const string version = "1";
        var operationId = Guid.NewGuid();
        var connectionString = "BlobEndpoint=https://unit-test.blob.core.windows.net/;SharedAccessSignature=sastoken";
        var errorHref = new Uri($"https://unit-test.blob.core.windows.net/mycontainer/{operationId.ToString(OperationId.FormatSpecifier)}/Errors.log", UriKind.Absolute);
        var options = new AzureBlobExportOptions
        {
            BlobContainerName = "mycontainer",
            Secret = new SecretKey
            {
                Name = operationId.ToString(OperationId.FormatSpecifier),
                Version = version,
            },
        };

        using var tokenSource = new CancellationTokenSource();

        _secretStore
            .GetSecretAsync(operationId.ToString(OperationId.FormatSpecifier), version, tokenSource.Token)
            .Returns(GetJson(connectionString));

        IExportSink sink = await _sinkProvider.CreateAsync(_serviceProvider, options, operationId, tokenSource.Token);

        Assert.IsType<AzureBlobExportSink>(sink);
        Assert.Equal(errorHref, sink.ErrorHref);
        await _secretStore
            .Received(1)
            .GetSecretAsync(operationId.ToString(OperationId.FormatSpecifier), version, tokenSource.Token);
    }

    [Theory]
    [InlineData("BlobEndpoint=https://unit-test.blob.core.windows.net/;SharedAccessSignature=sastoken", "export-e2e-test", null)]
    [InlineData("BlobEndpoint=https://unit-test.blob.core.windows.net/;AccountKey=secret", "export-e2e-test", null)]
    [InlineData(null, null, "https://dcmcipermanpmlxszw4sayty.blob.core.windows.net/export-e2e-test?sig=sastoken")]
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Cannot use inline Uri")]
    public async Task GivenSasTokenWithNoStore_WhenSecuringInfo_ThenThrow(string connectionString, string blobContainerName, string blobContainerUri)
    {
        var operationId = Guid.NewGuid();
        var options = new AzureBlobExportOptions
        {
            BlobContainerName = blobContainerName,
            BlobContainerUri = blobContainerUri != null ? new Uri(blobContainerUri, UriKind.Absolute) : null,
            ConnectionString = connectionString,
        };

        var provider = new AzureBlobExportSinkProvider(Options.Create(_serializerOptions), NullLogger<AzureBlobExportSinkProvider>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SecureSensitiveInfoAsync(options, operationId));
        await _secretStore.DidNotReceiveWithAnyArgs().SetSecretAsync(default, default, default, default);
    }

    [Theory]
    [InlineData("BlobEndpoint=https://unit-test.blob.core.windows.net/;", "export-e2e-test", null)]
    [InlineData(null, null, "https://dcmcipermanpmlxszw4sayty.blob.core.windows.net/export-e2e-test")]
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Cannot use inline Uri")]
    public async Task GivenNoSasToken_WhenSecuringInfo_ThenSkip(string connectionString, string blobContainerName, string blobContainerUri)
    {
        var options = new AzureBlobExportOptions
        {
            BlobContainerName = blobContainerName,
            BlobContainerUri = blobContainerUri != null ? new Uri(blobContainerUri, UriKind.Absolute) : null,
            ConnectionString = connectionString,
        };

        var provider = new AzureBlobExportSinkProvider(Options.Create(_serializerOptions), NullLogger<AzureBlobExportSinkProvider>.Instance);
        var actual = (AzureBlobExportOptions)await provider.SecureSensitiveInfoAsync(options, Guid.NewGuid());

        await _secretStore.DidNotReceiveWithAnyArgs().SetSecretAsync(default, default, default, default);

        Assert.Null(actual.Secret);
        Assert.Equal(blobContainerName, actual.BlobContainerName);
        Assert.Equal(blobContainerUri, actual.BlobContainerUri?.AbsoluteUri);
        Assert.Equal(connectionString, actual.ConnectionString);
    }

    [Theory]
    [InlineData("BlobEndpoint=https://unit-test.blob.core.windows.net/;SharedAccessSignature=sastoken", "export-e2e-test", null)]
    [InlineData("BlobEndpoint=https://unit-test.blob.core.windows.net/;AccountKey=secret", "export-e2e-test", null)]
    [InlineData(null, null, "https://dcmcipermanpmlxszw4sayty.blob.core.windows.net/export-e2e-test?sig=sastoken")]
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Cannot use inline Uri")]
    public async Task GivenSasToken_WhenSecuringInfo_ThenStoreSecrets(string connectionString, string blobContainerName, string blobContainerUri)
    {
        const string version = "1";
        var operationId = Guid.NewGuid();
        var options = new AzureBlobExportOptions
        {
            BlobContainerName = blobContainerName,
            BlobContainerUri = blobContainerUri != null ? new Uri(blobContainerUri, UriKind.Absolute) : null,
            ConnectionString = connectionString,
        };
        string secretJson = blobContainerUri != null ? GetJson(options.BlobContainerUri) : GetJson(connectionString);

        using var tokenSource = new CancellationTokenSource();

        _secretStore
            .SetSecretAsync(operationId.ToString(OperationId.FormatSpecifier), secretJson, MediaTypeNames.Application.Json, tokenSource.Token)
            .Returns(version);

        var actual = (AzureBlobExportOptions)await _sinkProvider.SecureSensitiveInfoAsync(options, operationId, tokenSource.Token);

        await _secretStore
            .Received(1)
            .SetSecretAsync(operationId.ToString(OperationId.FormatSpecifier), secretJson, MediaTypeNames.Application.Json, tokenSource.Token);

        Assert.Equal(operationId.ToString(OperationId.FormatSpecifier), actual.Secret.Name);
        Assert.Equal(version, actual.Secret.Version);
        Assert.Null(options.BlobContainerUri);
        Assert.Null(options.ConnectionString);
    }

    private static string GetJson(Uri blobContainerUri)
        => $"{{\"blobContainerUri\":\"{JavaScriptEncoder.Default.Encode(blobContainerUri.AbsoluteUri)}\"}}";

    private static string GetJson(string connectionString)
        => $"{{\"connectionString\":\"{JavaScriptEncoder.Default.Encode(connectionString)}\"}}";
}
