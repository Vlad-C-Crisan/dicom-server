// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using FellowOakDicom;
using Microsoft.Health.Dicom.Client;
using Microsoft.Health.Dicom.Core.Features.Workitem;
using Microsoft.Health.Dicom.Tests.Common;
using Xunit;

namespace Microsoft.Health.Dicom.Web.Tests.E2E.Rest;

public partial class WorkItemTransactionTests : IClassFixture<HttpIntegrationTestFixture<Startup>>
{
    private readonly IDicomWebClient _client;
    private readonly HttpIntegrationTestFixture<Startup> _fixture;

    public WorkItemTransactionTests(HttpIntegrationTestFixture<Startup> fixture)
    {
        EnsureArg.IsNotNull(fixture, nameof(fixture));
        _fixture = fixture;
        _client = fixture.GetDicomWebClient();
    }

    [Fact]
    [Trait("Category", "bvt-fe")]
    public async Task GivenWorkitemTransaction_WhenWorkitemIsFound_TheServerShouldExecuteWorkitemE2EWorkflowSuccessfully()
    {
        // Create
        string workitemUid = TestUidGenerator.Generate();
        await CreateWorkItemAndValidate(workitemUid)
            .ConfigureAwait(false);

        // Update Workitem Transaction when the Workitem has not been claimed.
        string newWorklistLabel = "WORKLIST-SCHEDULED";
        var updateDicomDataset = new DicomDataset
        {
            { DicomTag.WorklistLabel, newWorklistLabel },
        };
        await UpdateWorkitemAndValidate(workitemUid, newWorklistLabel, updateDicomDataset)
            .ConfigureAwait(false);

        // Change Workitem State to In-Progress
        string transactionUid = TestUidGenerator.Generate();
        await ChangeWorkitemStateAndValidate(workitemUid, transactionUid, ProcedureStepStateConstants.InProgress)
            .ConfigureAwait(false);

        // Update Workitem Transaction when the Workitem has been claimed.
        newWorklistLabel = "WORKLIST-IN-PROGRESS";
        updateDicomDataset = new DicomDataset
        {
            { DicomTag.WorklistLabel, newWorklistLabel },
        };

        // Setting following attributes for upcoming calls that will change the state to Completed.
        updateDicomDataset.AddOrUpdate(DicomTag.UnifiedProcedureStepPerformedProcedureSequence, new DicomDataset
        {
            new DicomSequence(DicomTag.ActualHumanPerformersSequence, new DicomDataset
            {
                new DicomSequence(DicomTag.HumanPerformerCodeSequence, new DicomDataset()),
                { DicomTag.HumanPerformerName, @"Samples-TestFixture" }
            }),
            new DicomSequence(DicomTag.PerformedStationNameCodeSequence, new DicomDataset()),
            { DicomTag.PerformedProcedureStepStartDateTime, DateTime.UtcNow },
            new DicomSequence(DicomTag.PerformedWorkitemCodeSequence, new DicomDataset()),
            { DicomTag.PerformedProcedureStepEndDateTime, DateTime.UtcNow },
            new DicomSequence(DicomTag.OutputInformationSequence, new DicomDataset())
        });

        await UpdateWorkitemAndValidate(workitemUid, newWorklistLabel, updateDicomDataset, transactionUid)
            .ConfigureAwait(false);

        // Change Workitem State to Completed
        await ChangeWorkitemStateAndValidate(workitemUid, transactionUid, ProcedureStepStateConstants.Completed)
            .ConfigureAwait(false);
    }

    [Fact]
    [Trait("Category", "bvt-fe")]
    public async Task GivenWorkitemTransaction_WhenWorkitemIsFound_TheServerShouldExecuteWorkitemCancelE2EWorkflowSuccessfully()
    {
        // Create
        string workitemUid = TestUidGenerator.Generate();
        await CreateWorkItemAndValidate(workitemUid)
            .ConfigureAwait(false);

        // Cancel Workitem
        await CancelWorkitemAndValidate(workitemUid)
            .ConfigureAwait(false);
    }

    private async Task CreateWorkItemAndValidate(string workitemUid)
    {
        var dicomDataset = Samples.CreateRandomWorkitemInstanceDataset(workitemUid);

        using var addResponse = await _client.AddWorkitemAsync(Enumerable.Repeat(dicomDataset, 1), workitemUid);
        Assert.True(addResponse.IsSuccessStatusCode);
    }

    private async Task UpdateWorkitemAndValidate(string workitemUid, string newWorklistLabel, DicomDataset updateDicomDataset, string transactionUid = default)
    {
        using var updateWorkitemScheduledResponse = await _client.UpdateWorkitemAsync(Enumerable.Repeat(updateDicomDataset, 1), workitemUid, transactionUid)
            .ConfigureAwait(false);
        Assert.True(updateWorkitemScheduledResponse.IsSuccessStatusCode);

        var dataset = await RetrieveWorkitemAndValidate(workitemUid)
            .ConfigureAwait(false);
        Assert.Equal(newWorklistLabel, dataset.GetString(DicomTag.WorklistLabel));
    }

    private async Task<DicomDataset> RetrieveWorkitemAndValidate(string workitemUid)
    {
        using var retrieveResponse = await _client.RetrieveWorkitemAsync(workitemUid)
            .ConfigureAwait(false);
        Assert.True(retrieveResponse.IsSuccessStatusCode);

        var dataset = await retrieveResponse.GetValueAsync().ConfigureAwait(false);
        Assert.NotNull(dataset);

        return dataset;
    }

    private async Task ChangeWorkitemStateAndValidate(string workitemUid, string transactionUid, string procedureStepState)
    {
        var changeStateDicomDataset = new DicomDataset
        {
            { DicomTag.TransactionUID, transactionUid },
            { DicomTag.ProcedureStepState, procedureStepState },
        };

        using var changeStateInProgressResponse = await _client.ChangeWorkitemStateAsync(Enumerable.Repeat(changeStateDicomDataset, 1), workitemUid)
            .ConfigureAwait(false);

        Assert.True(changeStateInProgressResponse.IsSuccessStatusCode);

        var dataset = await RetrieveWorkitemAndValidate(workitemUid)
            .ConfigureAwait(false);
        Assert.Equal(ProcedureStepStateExtensions.GetProcedureStepState(procedureStepState), dataset.GetProcedureStepState());
    }

    private async Task CancelWorkitemAndValidate(string workitemUid)
    {
        var cancelDicomDataset = Samples.CreateWorkitemCancelRequestDataset(@"Test Cancel");

        using var cancelResponse = await _client
            .CancelWorkitemAsync(Enumerable.Repeat(cancelDicomDataset, 1), workitemUid)
            .ConfigureAwait(false);
        Assert.True(cancelResponse.IsSuccessStatusCode);

        var dataset = await RetrieveWorkitemAndValidate(workitemUid)
            .ConfigureAwait(false);
        Assert.Equal(ProcedureStepStateExtensions.GetProcedureStepState(ProcedureStepStateConstants.Canceled), dataset.GetProcedureStepState());
    }
}
