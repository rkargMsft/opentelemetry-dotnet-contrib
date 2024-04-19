// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System;

namespace OpenTelemetry.Sampler.Adaptive;

public sealed class PerOperationAdaptiveSamplerOptions
{
    public int MaxOperationsToTrack { get; set; } = 500;
    public TimeSpan RefreshPeriod { get; set; } = TimeSpan.FromMinutes(1);
}
