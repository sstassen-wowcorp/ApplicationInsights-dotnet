﻿namespace Microsoft.ApplicationInsights.Extensibility.Implementation.Metrics
{
    using System;
    using System.Collections.Generic;
    using Microsoft.ApplicationInsights.Channel;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.Metrics;
    using Microsoft.ApplicationInsights.Metrics.Extensibility;
    using static System.FormattableString;

    /// <summary>
    /// An instance of this class is contained within the <see cref="AutocollectedMetricsExtractor"/> telemetry processor.
    /// It extracts auto-collected, pre-aggregated (aka. "standard") metrics from DependencyTelemetry objects which represent
    /// invocations of remote dependencies performed by the monitored service.
    /// </summary>
    internal class DependencyMetricsExtractor : ISpecificAutocollectedMetricsExtractor
    {
        /// <summary>
        /// The default value for the <see cref="MaxDependencyTypesToDiscover"/> property.        
        /// </summary>
        public const int MaxDependencyTypesToDiscoverDefault = 15;

        /// <summary>
        /// The default value for the <see cref="MaxCloudRoleInstanceValuesToDiscover"/> property.
        /// </summary>
        public const int MaxTargetValuesToDiscoverDefault = 50;

        /// <summary>
        /// The default value for the <see cref="MaxCloudRoleInstanceValuesToDiscover"/> property.
        /// </summary>
        public const int MaxCloudRoleInstanceValuesToDiscoverDefault = 2;

        /// <summary>
        /// The default value for the <see cref="MaxCloudRoleInstanceValuesToDiscover"/> property.
        /// </summary>
        public const int MaxCloudRoleNameValuesToDiscoverDefault = 2;

        /// <summary>
        /// Extracted metric.
        /// </summary>
        private Metric dependencyCallDurationMetric = null;

        private List<IDimensionExtractor> dimensionExtractors = new List<IDimensionExtractor>();

        /// <summary>
        /// Initializes a new instance of the <see cref="DependencyMetricsExtractor" /> class.
        /// </summary>
        public DependencyMetricsExtractor()
        {
            dimensionExtractors.Add(new IdDimensionExtractor());
            dimensionExtractors.Add(new SuccessDimensionExtractor());
            dimensionExtractors.Add(new SyntheticDimensionExtractor());
            dimensionExtractors.Add(new TypeDimensionExtractor() { MaxValues = MaxDependencyTypesToDiscover });
            dimensionExtractors.Add(new TargetDimensionExtractor() { MaxValues = MaxDependencyTargetValuesToDiscover });
            dimensionExtractors.Add(new CloudRoleInstanceDimensionExtractor() { MaxValues = MaxCloudRoleInstanceValuesToDiscover });
            dimensionExtractors.Add(new CloudRoleNameDimensionExtractor() { MaxValues = MaxCloudRoleNameValuesToDiscover });
        }

        /// <summary>
        /// Gets the name of this extractor.
        /// All telemetry that has been processed by this extractor will be tagged by adding the
        /// string "<c>(Name: {ExtractorName}, Ver:{ExtractorVersion})</c>" to the <c>xxx.ProcessedByExtractors</c> property.
        /// The respective logic is in the <see cref="AutocollectedMetricsExtractor"/>-class.
        /// </summary>
        public string ExtractorName { get; } = "Dependencies";

        /// <summary>
        /// Gets the version of this extractor.
        /// All telemetry that has been processed by this extractor will be tagged by adding the
        /// string "<c>(Name: {ExtractorName}, Ver:{ExtractorVersion})</c>" to the <c>xxx.ProcessedByExtractors</c> property.
        /// The respective logic is in the <see cref="AutocollectedMetricsExtractor"/>-class.
        /// </summary>
        public string ExtractorVersion { get; } = "1.1";

        /// <summary>
        /// Gets or sets the maximum number of auto-discovered dependency types.
        /// </summary>
        public int MaxDependencyTypesToDiscover { get; set; } = MaxDependencyTypesToDiscoverDefault;

        /// <summary>
        /// Gets or sets the maximum number of auto-discovered Dependency Target values.
        /// </summary>
        public int MaxDependencyTargetValuesToDiscover { get; set; } = MaxTargetValuesToDiscoverDefault;
        
        /// <summary>
        /// Gets or sets the maximum number of auto-discovered Cloud RoleInstance values.
        /// </summary>
        public int MaxCloudRoleInstanceValuesToDiscover { get; set; } = MaxCloudRoleInstanceValuesToDiscoverDefault;

        /// <summary>
        /// Gets or sets the maximum number of auto-discovered Cloud RoleName values.
        /// </summary>
        public int MaxCloudRoleNameValuesToDiscover { get; set; } = MaxCloudRoleNameValuesToDiscoverDefault;

        /// <summary>
        /// Pre-initialize this extractor.
        /// </summary>
        /// <param name="metricTelemetryClient">The <c>TelemetryClient</c> to be used for sending extracted metrics.</param>
        public void InitializeExtractor(TelemetryClient metricTelemetryClient)
        {
            int seriesCountLimit = 1;
            int[] valuesPerDimensionLimit = new int[this.dimensionExtractors.Count];
            int i = 0;

            foreach (var dim in this.dimensionExtractors)
            {
                int dimLimit = 1;
                if (dim.MaxValues == 0)
                {
                    dimLimit = 1;
                }
                else
                {
                    dimLimit = dim.MaxValues;
                }

                seriesCountLimit = seriesCountLimit * (1 + dimLimit);
                valuesPerDimensionLimit[i++] = dimLimit;
            }

            MetricConfiguration config = new MetricConfigurationForMeasurement(
                                                            seriesCountLimit,
                                                            valuesPerDimensionLimit,
                                                            new MetricSeriesConfigurationForMeasurement(restrictToUInt32Values: false));
            config.ApplyDimensionCapping = true;
            config.DimensionCappedString = MetricTerms.Autocollection.Common.PropertyValues.DimensionCapFallbackValue;

            IList<string> dimensionNames = new List<string>(this.dimensionExtractors.Count);
            for (i = 0; i < this.dimensionExtractors.Count; i++)
            {
                dimensionNames.Add(this.dimensionExtractors[i].Name);
            }

            MetricIdentifier metricIdentifier = new MetricIdentifier(MetricIdentifier.DefaultMetricNamespace,
                        MetricTerms.Autocollection.Metric.DependencyCallDuration.Name,
                        dimensionNames);

            this.dependencyCallDurationMetric = metricTelemetryClient.GetMetric(
                                                        metricIdentifier: metricIdentifier,
                                                        metricConfiguration: config,
                                                        aggregationScope: MetricAggregationScope.TelemetryClient);
        }

        /// <summary>
        /// Extracts appropriate data points for auto-collected, pre-aggregated metrics from a single <c>DependencyTelemetry</c> item.
        /// </summary>
        /// <param name="fromItem">The telemetry item from which to extract the metric data points.</param>
        /// <param name="isItemProcessed">Whether of not the specified item was processed (aka not ignored) by this extractor.</param>
        public void ExtractMetrics(ITelemetry fromItem, out bool isItemProcessed)
        {
            //// If this item is not a DependencyTelemetry, we will not process it:
            DependencyTelemetry dependencyCall = fromItem as DependencyTelemetry;
            if (dependencyCall == null)
            {
                isItemProcessed = false; 
                return;
            }

            //// If there is no Metric, then this extractor has not been properly initialized yet:
            if (this.dependencyCallDurationMetric == null)
            {
                //// This should be caught and properly logged by the base class:
                throw new InvalidOperationException(Invariant($"Cannot execute {nameof(this.ExtractMetrics)}.")
                                                  + Invariant($" There is no {nameof(this.dependencyCallDurationMetric)}.")
                                                  + Invariant($" Either this metrics extractor has not been initialized, or it has been disposed."));
            }

            int i = 0;
            string[] dimValues = new string[this.dimensionExtractors.Count];
            foreach (var dim in this.dimensionExtractors)
            {
                dimValues[i] = dim.ExtractDimension(dependencyCall);
                if (string.IsNullOrEmpty(dimValues[i]))
                {
                    dimValues[i] = dim.DefaultValue;
                }

                i++;
            }

            CommonHelper.TrackValueHelper(this.dependencyCallDurationMetric, dependencyCall.Duration.TotalMilliseconds, dimValues);
            isItemProcessed = true;
        }
    }
}
