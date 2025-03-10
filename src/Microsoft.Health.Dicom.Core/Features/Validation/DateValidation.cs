﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using FellowOakDicom;
using Microsoft.Health.Dicom.Core.Exceptions;
using Microsoft.Health.Dicom.Core.Extensions;

namespace Microsoft.Health.Dicom.Core.Features.Validation;

internal class DateValidation : IElementValidation
{
    private const string DateFormatDA = "yyyyMMdd";

    public void Validate(DicomElement dicomElement)
    {
        string value = dicomElement.GetFirstValueOrDefault<string>();
        string name = dicomElement.Tag.GetFriendlyName();
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (!DateTime.TryParseExact(value, DateFormatDA, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out _))
        {
            throw new ElementValidationException(name, DicomVR.DA, value, ValidationErrorCode.DateIsInvalid);
        }
    }
}
