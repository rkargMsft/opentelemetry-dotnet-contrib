// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using OpenTelemetry.Trace;

namespace OpenTelemetry.Sampler.Adaptive
{
    /// <summary>
    /// This sampler provides a statistical upper bound on the number of messages per refresh period.
    /// This will at most send 32 messages in a given refresh period, though in practice it will be far fewer.
    /// <remarks>
    /// Examples:
    /// Events in a refresh period --> sampled events
    ///       1 --> 1
    ///      10 --> 4
    ///     100 --> 7
    ///   1_000 --> 10
    ///  10_000 --> 14
    /// 100_000 --> 17
    /// If refreshed every 1 minute, then this will limit to the above number of sampled event/min/operation
    /// </remarks>
    /// </summary>
    public sealed class PerOperationAdaptiveSampler : Trace.Sampler, IDisposable
    {
        private readonly PerOperationAdaptiveSamplerOptions options;
        private readonly ConcurrentDictionary<string, DecayingSampler> operationDictionary = new();
        private readonly PeriodicTimer timer;

        /// <summary>
        /// Creates new sampler
        /// </summary>
        /// <param name="options">Configuration for the sampler</param>
        public PerOperationAdaptiveSampler(PerOperationAdaptiveSamplerOptions options)
        {
            this.options = options;
            this.timer = new PeriodicTimer(options.RefreshPeriod);

            _ = Task.Run(
                async () => {
                    await this.timer.WaitForNextTickAsync();
                    foreach (var entry in this.operationDictionary)
                    {
                        entry.Value.RefreshSamplingThreshold();
                    }
                }
            );
        }
        
        /// <inheritdoc />
        public void Dispose()
        {
            this.timer?.Dispose();
        }

        /// <inheritdoc />
        public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
        {
            var sampler = GetSampler(samplingParameters, this.operationDictionary, this.options);

            return sampler.ShouldSample(samplingParameters);
        }

        internal static DecayingSampler GetSampler(SamplingParameters samplingParameters,
            ConcurrentDictionary<string, DecayingSampler> dictionary,
            PerOperationAdaptiveSamplerOptions options)
        {
            var sampler = dictionary.GetOrAdd(
                samplingParameters.Name,
                static _ => new DecayingSampler()
            );

            if (dictionary.Count > options.MaxOperationsToTrack)
            {
                foreach (var item in dictionary)
                {
                    // remove any entry but skip over the one we just added if it happens to be first
                    if (item.Key == samplingParameters.Name)
                    {
                        continue;
                    }

                    dictionary.TryRemove(item);
                    break;
                }
            }

            return sampler;
        }

        internal sealed class DecayingSampler : Trace.Sampler
        {
            private static readonly SamplingResult DropResult = new(SamplingDecision.Drop);
            private static readonly SamplingResult SampleResult = new(SamplingDecision.RecordAndSample);

            internal UInt32 SamplingThreshold { get; private set; } = uint.MaxValue;

            public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
            {
                if (this.SamplingThreshold == 0)
                {
                    return DropResult;
                }
                if ((UInt32)Random.Shared.NextInt64(UInt32.MinValue, UInt32.MaxValue) <= this.SamplingThreshold)
                {
                    this.SamplingThreshold >>= 1;
                    return SampleResult;
                }
                else
                {
                    return DropResult;
                }
            }

            public void RefreshSamplingThreshold()
            {
                this.SamplingThreshold = uint.MaxValue;
            }
        }
    }
}
