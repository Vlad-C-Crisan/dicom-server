﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Dicom.Core.Features.ExtendedQueryTag;
using Microsoft.Health.Dicom.Core.Features.Model;
using Microsoft.Health.Dicom.Core.Models;
using Microsoft.Health.Dicom.Functions.Indexing.Models;
using Microsoft.Health.Dicom.Tests.Common;
using Microsoft.Health.Operations;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Dicom.Functions.UnitTests.Indexing;

public partial class ReindexDurableFunctionTests
{
    [Fact]
    public async Task GivenTagKeys_WhenAssigningReindexingOperation_ThenShouldPassArguments()
    {
        Guid operationId = Guid.NewGuid();
        var expectedInput = new List<int> { 1, 2, 3, 4, 5 };
        var expectedOutput = new List<ExtendedQueryTagStoreEntry>
        {
            new ExtendedQueryTagStoreEntry(1, "01010101", "AS", null, QueryTagLevel.Instance, ExtendedQueryTagStatus.Adding, QueryStatus.Enabled, 0)
        };

        // Arrange input
        IDurableActivityContext context = Substitute.For<IDurableActivityContext>();
        context.InstanceId.Returns(operationId.ToString(OperationId.FormatSpecifier));
        context.GetInput<IReadOnlyList<int>>().Returns(expectedInput);

        _extendedQueryTagStore
            .AssignReindexingOperationAsync(expectedInput, operationId, false, CancellationToken.None)
            .Returns(expectedOutput);

        // Call the activity
        IReadOnlyList<ExtendedQueryTagStoreEntry> actual = await _reindexDurableFunction.AssignReindexingOperationAsync(
            context,
            NullLogger.Instance);

        // Assert behavior
        Assert.Same(expectedOutput, actual);
        context.Received(1).GetInput<IReadOnlyList<int>>();
        await _extendedQueryTagStore
            .Received(1)
            .AssignReindexingOperationAsync(expectedInput, operationId, false, CancellationToken.None);
    }

    [Fact]
    public async Task GivenTagKeys_WhenGettingExtentendedQueryTags_ThenShouldPassArguments()
    {
        Guid operationId = Guid.NewGuid();
        var expectedOutput = new List<ExtendedQueryTagStoreEntry>
        {
            new ExtendedQueryTagStoreEntry(1, "01010101", "AS", null, QueryTagLevel.Instance, ExtendedQueryTagStatus.Adding, QueryStatus.Enabled, 0)
        };

        // Arrange input
        IDurableActivityContext context = Substitute.For<IDurableActivityContext>();
        context.InstanceId.Returns(operationId.ToString(OperationId.FormatSpecifier));

        _extendedQueryTagStore
            .GetExtendedQueryTagsAsync(operationId, CancellationToken.None)
            .Returns(expectedOutput);

        // Call the activity
        IReadOnlyList<ExtendedQueryTagStoreEntry> actual = await _reindexDurableFunction.GetQueryTagsAsync(
            context,
            NullLogger.Instance);

        // Assert behavior
        Assert.Same(expectedOutput, actual);
        await _extendedQueryTagStore
            .Received(1)
            .GetExtendedQueryTagsAsync(operationId, CancellationToken.None);
    }

    [Fact]
    [Obsolete]
    public async Task GivenLegacyActivityAndNoWatermark_WhenGettingInstanceBatches_ThenShouldInvokeCorrectMethod()
    {
        IReadOnlyList<WatermarkRange> expected = new List<WatermarkRange> { new WatermarkRange(12345, 678910) };
        _instanceStore
            .GetInstanceBatchesAsync(_options.BatchSize, _options.MaxParallelBatches, IndexStatus.Created, null, CancellationToken.None)
            .Returns(expected);

        IReadOnlyList<WatermarkRange> actual = await _reindexDurableFunction.GetInstanceBatchesAsync(
            null,
            NullLogger.Instance);

        Assert.Same(expected, actual);
        await _instanceStore
            .Received(1)
            .GetInstanceBatchesAsync(_options.BatchSize, _options.MaxParallelBatches, IndexStatus.Created, null, CancellationToken.None);
    }

    [Fact]
    [Obsolete]
    public async Task GivenLegacyActivityAndWatermark_WhenGettingInstanceBatches_ThenShouldInvokeCorrectMethod()
    {
        IReadOnlyList<WatermarkRange> expected = new List<WatermarkRange> { new WatermarkRange(10, 1000) };
        _instanceStore
            .GetInstanceBatchesAsync(_options.BatchSize, _options.MaxParallelBatches, IndexStatus.Created, 12345L, CancellationToken.None)
            .Returns(expected);

        IReadOnlyList<WatermarkRange> actual = await _reindexDurableFunction.GetInstanceBatchesAsync(
            12345L,
            NullLogger.Instance);

        Assert.Same(expected, actual);
        await _instanceStore
            .Received(1)
            .GetInstanceBatchesAsync(_options.BatchSize, _options.MaxParallelBatches, IndexStatus.Created, 12345L, CancellationToken.None);
    }

    [Fact]
    public async Task GivenNoWatermark_WhenGettingInstanceBatches_ThenShouldInvokeCorrectMethod()
    {
        const int batchSize = 100;
        const int maxParallelBatches = 3;

        IReadOnlyList<WatermarkRange> expected = new List<WatermarkRange> { new WatermarkRange(12345, 678910) };
        _instanceStore
            .GetInstanceBatchesAsync(batchSize, maxParallelBatches, IndexStatus.Created, null, CancellationToken.None)
            .Returns(expected);

        IReadOnlyList<WatermarkRange> actual = await _reindexDurableFunction.GetInstanceBatchesV2Async(
            new BatchCreationArguments(null, batchSize, maxParallelBatches),
            NullLogger.Instance);

        Assert.Same(expected, actual);
        await _instanceStore
            .Received(1)
            .GetInstanceBatchesAsync(batchSize, maxParallelBatches, IndexStatus.Created, null, CancellationToken.None);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    public async Task GivenWatermark_WhenGettingInstanceBatches_ThenShouldInvokeCorrectMethod(long max)
    {
        const int batchSize = 100;
        const int maxParallelBatches = 3;

        IReadOnlyList<WatermarkRange> expected = new List<WatermarkRange> { new WatermarkRange(1, 2) }; // watermarks don't matter
        _instanceStore
            .GetInstanceBatchesAsync(batchSize, maxParallelBatches, IndexStatus.Created, max, CancellationToken.None)
            .Returns(expected);

        IReadOnlyList<WatermarkRange> actual = await _reindexDurableFunction.GetInstanceBatchesV2Async(
            new BatchCreationArguments(max, batchSize, maxParallelBatches),
            NullLogger.Instance);

        Assert.Same(expected, actual);
        await _instanceStore
            .Received(1)
            .GetInstanceBatchesAsync(batchSize, maxParallelBatches, IndexStatus.Created, max, CancellationToken.None);
    }

    [Fact]
    [Obsolete]
    public async Task GivenLegacyActivity_WhenReindexing_ThenShouldReindexEachInstance()
    {
        var batch = new ReindexBatch
        {
            QueryTags = new List<ExtendedQueryTagStoreEntry>
            {
                new ExtendedQueryTagStoreEntry(1, "01", "DT", "foo", QueryTagLevel.Instance, ExtendedQueryTagStatus.Adding, QueryStatus.Enabled, 0),
                new ExtendedQueryTagStoreEntry(2, "02", "DT", null, QueryTagLevel.Series, ExtendedQueryTagStatus.Adding, QueryStatus.Enabled, 0),
                new ExtendedQueryTagStoreEntry(3, "03", "AS", "bar", QueryTagLevel.Study, ExtendedQueryTagStatus.Adding, QueryStatus.Enabled, 0),
            },
            WatermarkRange = new WatermarkRange(3, 10),
        };

        var expected = new List<VersionedInstanceIdentifier>
        {
            new VersionedInstanceIdentifier(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate(), 3),
            new VersionedInstanceIdentifier(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate(), 4),
            new VersionedInstanceIdentifier(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate(), 5),
            new VersionedInstanceIdentifier(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate(), 6),
            new VersionedInstanceIdentifier(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate(), 7),
            new VersionedInstanceIdentifier(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate(), 8),
            new VersionedInstanceIdentifier(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate(), 9),
        };

        // Arrange input
        // Note: Parallel.ForEachAsync uses its own CancellationTokenSource
        _instanceStore
            .GetInstanceIdentifiersByWatermarkRangeAsync(batch.WatermarkRange, IndexStatus.Created, Arg.Any<CancellationToken>())
            .Returns(expected);

        foreach (VersionedInstanceIdentifier identifier in expected)
        {
            _instanceReindexer.ReindexInstanceAsync(batch.QueryTags, identifier).Returns(true);
        }

        // Call the activity
        await _reindexDurableFunction.ReindexBatchAsync(batch, NullLogger.Instance);

        // Assert behavior
        await _instanceStore
            .Received(1)
            .GetInstanceIdentifiersByWatermarkRangeAsync(batch.WatermarkRange, IndexStatus.Created, CancellationToken.None);

        foreach (VersionedInstanceIdentifier identifier in expected)
        {
            await _instanceReindexer.Received(1).ReindexInstanceAsync(batch.QueryTags, identifier, Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task GivenBatch_WhenReindexing_ThenShouldReindexEachInstance()
    {
        var args = new ReindexBatchArguments(
            new List<ExtendedQueryTagStoreEntry>
            {
                new ExtendedQueryTagStoreEntry(1, "01", "DT", "foo", QueryTagLevel.Instance, ExtendedQueryTagStatus.Adding, QueryStatus.Enabled, 0),
                new ExtendedQueryTagStoreEntry(2, "02", "DT", null, QueryTagLevel.Series, ExtendedQueryTagStatus.Adding, QueryStatus.Enabled, 0),
                new ExtendedQueryTagStoreEntry(3, "03", "AS", "bar", QueryTagLevel.Study, ExtendedQueryTagStatus.Adding, QueryStatus.Enabled, 0),
            },
            new WatermarkRange(3, 10));

        var expected = new List<VersionedInstanceIdentifier>
        {
            new VersionedInstanceIdentifier(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate(), 3),
            new VersionedInstanceIdentifier(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate(), 4),
            new VersionedInstanceIdentifier(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate(), 5),
            new VersionedInstanceIdentifier(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate(), 6),
            new VersionedInstanceIdentifier(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate(), 7),
            new VersionedInstanceIdentifier(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate(), 8),
            new VersionedInstanceIdentifier(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate(), 9),
        };

        // Arrange input
        // Note: Parallel.ForEachAsync uses its own CancellationTokenSource
        _instanceStore
            .GetInstanceIdentifiersByWatermarkRangeAsync(args.WatermarkRange, IndexStatus.Created, Arg.Any<CancellationToken>())
            .Returns(expected);

        foreach (VersionedInstanceIdentifier identifier in expected)
        {
            _instanceReindexer.ReindexInstanceAsync(args.QueryTags, identifier).Returns(true);
        }

        // Call the activity
        await _reindexDurableFunction.ReindexBatchV2Async(args, NullLogger.Instance);

        // Assert behavior
        await _instanceStore
            .Received(1)
            .GetInstanceIdentifiersByWatermarkRangeAsync(args.WatermarkRange, IndexStatus.Created, CancellationToken.None);

        foreach (VersionedInstanceIdentifier identifier in expected)
        {
            await _instanceReindexer.Received(1).ReindexInstanceAsync(args.QueryTags, identifier, Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task GivenTagKeys_WhenCompletingReindexing_ThenShouldPassArguments()
    {
        string operationId = Guid.NewGuid().ToString();
        var expectedInput = new List<int> { 1, 2, 3, 4, 5 };
        var expectedOutput = new List<int> { 1, 2, 4, 5 };

        // Arrange input
        IDurableActivityContext context = Substitute.For<IDurableActivityContext>();
        context.InstanceId.Returns(operationId);
        context.GetInput<IReadOnlyList<int>>().Returns(expectedInput);

        _extendedQueryTagStore
            .CompleteReindexingAsync(expectedInput, CancellationToken.None)
            .Returns(expectedOutput);

        // Call the activity
        IReadOnlyList<int> actual = await _reindexDurableFunction.CompleteReindexingAsync(
            context,
            NullLogger.Instance);

        // Assert behavior
        Assert.Same(expectedOutput, actual);
        context.Received(1).GetInput<IReadOnlyList<int>>();
        await _extendedQueryTagStore
            .Received(1)
            .CompleteReindexingAsync(expectedInput, CancellationToken.None);
    }
}
