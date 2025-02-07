﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Microsoft.Azure.Devices.Provisioning.Client.Transport
{
    [SuppressMessage("Microsoft.Performance", "CA1812", Justification = "Is instantiated by json convertor")]
    internal class ProvisioningErrorDetailsMqtt : ProvisioningErrorDetails
    {
        private const string RetryAfterHeader = "Retry-After";

        /// <summary>
        /// The time to wait before trying again if this error is transient
        /// </summary>
        internal TimeSpan? RetryAfter { get; set; }

        public static TimeSpan? GetRetryAfterFromTopic(string topic, TimeSpan defaultPoolingInterval)
        {
            string[] topicAndQueryString = topic.Split('?');
            if (topicAndQueryString.Length > 1)
            {
                string[] queryPairs = topicAndQueryString[1].Split('&');
                for (int queryPairIndex = 0; queryPairIndex < queryPairs.Length; queryPairIndex++)
                {
                    string[] queryKeyAndValue = queryPairs[queryPairIndex].Split('=');
                    if (queryKeyAndValue.Length == 2 && queryKeyAndValue[0].Equals(RetryAfterHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        int secondsToWait;
                        if (int.TryParse(queryKeyAndValue[1], out secondsToWait))
                        {
                            var serviceRecommendedDelay = TimeSpan.FromSeconds(secondsToWait);

                            if (serviceRecommendedDelay.TotalSeconds < defaultPoolingInterval.TotalSeconds)
                            {
                                return defaultPoolingInterval;
                            }
                            else
                            {
                                return serviceRecommendedDelay;
                            }
                        }
                    }
                }
            }

            return null;
        }
    }
}
