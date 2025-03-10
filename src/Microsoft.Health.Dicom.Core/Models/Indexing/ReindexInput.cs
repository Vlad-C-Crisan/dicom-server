﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Dicom.Core.Models.Operations;

namespace Microsoft.Health.Dicom.Core.Models.Indexing;

internal class ReindexInput
{
    public IReadOnlyCollection<int> QueryTagKeys { get; set; }

    public BatchingOptions Batching { get; set; }
}
