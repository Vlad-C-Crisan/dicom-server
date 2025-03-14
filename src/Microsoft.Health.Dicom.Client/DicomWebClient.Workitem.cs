﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using FellowOakDicom;

namespace Microsoft.Health.Dicom.Client;

public partial class DicomWebClient : IDicomWebClient
{
    public async Task<DicomWebResponse> AddWorkitemAsync(IEnumerable<DicomDataset> dicomDatasets, string workitemUid, string partitionName, CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(dicomDatasets, nameof(dicomDatasets));
        EnsureArg.IsNotEmptyOrWhiteSpace(workitemUid, nameof(workitemUid));

        var uri = GenerateWorkitemAddRequestUri(workitemUid, partitionName);

        return await Request(uri, dicomDatasets, HttpMethod.Post, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DicomWebResponse> CancelWorkitemAsync(IEnumerable<DicomDataset> dicomDatasets, string workitemUid, string partitionName = default, CancellationToken cancellationToken = default)
    {
        EnsureArg.IsNotNull(dicomDatasets, nameof(dicomDatasets));
        EnsureArg.IsNotEmptyOrWhiteSpace(workitemUid, nameof(workitemUid));

        var uri = GenerateWorkitemCancelRequestUri(workitemUid, partitionName);

        return await Request(uri, dicomDatasets, HttpMethod.Post, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DicomWebResponse<DicomDataset>> RetrieveWorkitemAsync(string workitemUid, string partitionName = default, CancellationToken cancellationToken = default)
    {
        EnsureArg.IsNotEmptyOrWhiteSpace(workitemUid, nameof(workitemUid));

        var requestUri = GenerateWorkitemRetrieveRequestUri(workitemUid, partitionName);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        request.Headers.Accept.Add(DicomWebConstants.MediaTypeApplicationDicomJson);

        var response = await HttpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessStatusCodeAsync(response)
            .ConfigureAwait(false);

        var contentValueFactory = new Func<HttpContent, Task<DicomDataset>>(
            content => ValueFactory<DicomDataset>(content));

        return new DicomWebResponse<DicomDataset>(response, contentValueFactory);
    }

    public async Task<DicomWebResponse> ChangeWorkitemStateAsync(
        IEnumerable<DicomDataset> dicomDatasets,
        string workitemUid,
        string partitionName = default,
        CancellationToken cancellationToken = default)
    {
        EnsureArg.IsNotNull(dicomDatasets, nameof(dicomDatasets));
        EnsureArg.IsNotEmptyOrWhiteSpace(workitemUid, nameof(workitemUid));

        var uri = GenerateChangeWorkitemStateRequestUri(workitemUid, partitionName);

        return await Request(uri, dicomDatasets, HttpMethod.Put, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DicomWebAsyncEnumerableResponse<DicomDataset>> QueryWorkitemAsync(string queryString, string partitionName = default, CancellationToken cancellationToken = default)
    {
        EnsureArg.IsNotEmptyOrWhiteSpace(queryString, nameof(queryString));

        var requestUri = GenerateRequestUri(DicomWebConstants.WorkitemUriString + GetQueryParamUriString(queryString), partitionName);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        request.Headers.Accept.Add(DicomWebConstants.MediaTypeApplicationDicomJson);

        var response = await HttpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessStatusCodeAsync(response)
            .ConfigureAwait(false);

        return new DicomWebAsyncEnumerableResponse<DicomDataset>(
            response,
            DeserializeAsAsyncEnumerable<DicomDataset>(response.Content));
    }

    public async Task<DicomWebResponse> UpdateWorkitemAsync(IEnumerable<DicomDataset> dicomDatasets, string workitemUid, string transactionUid = default, string partitionName = default, CancellationToken cancellationToken = default)
    {
        EnsureArg.IsNotNull(dicomDatasets, nameof(dicomDatasets));
        EnsureArg.IsNotEmptyOrWhiteSpace(workitemUid, nameof(workitemUid));

        var uri = GenerateWorkitemUpdateRequestUri(workitemUid, transactionUid, partitionName);

        return await Request(uri, dicomDatasets, HttpMethod.Post, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DicomWebResponse> Request<TContent>(
        Uri uri,
        TContent requestContent,
        HttpMethod httpMethod,
        CancellationToken cancellationToken = default) where TContent : class
    {
        EnsureArg.IsNotNull(uri, nameof(uri));
        EnsureArg.IsNotNull(requestContent, nameof(requestContent));

        string jsonString = JsonSerializer.Serialize(requestContent, JsonSerializerOptions);
        using var request = new HttpRequestMessage(httpMethod, uri);
        {
            request.Content = new StringContent(jsonString);
            request.Content.Headers.ContentType = DicomWebConstants.MediaTypeApplicationDicomJson;
        }

        request.Headers.Accept.Add(DicomWebConstants.MediaTypeApplicationDicomJson);

        var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        await EnsureSuccessStatusCodeAsync(response).ConfigureAwait(false);

        return new DicomWebResponse(response);
    }
}
