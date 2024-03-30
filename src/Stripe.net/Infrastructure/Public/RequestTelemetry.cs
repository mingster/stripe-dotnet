namespace Stripe
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using Newtonsoft.Json;
    using Stripe.Infrastructure;

    /// <summary>
    /// Helper class used by <see cref="SystemNetHttpClient"/> to manage request telemetry.
    /// </summary>
    public class RequestTelemetry
    {
        private readonly ConcurrentQueue<RequestMetrics> prevRequestMetrics
            = new ConcurrentQueue<RequestMetrics>();

        private static long MaxRequestMetricsQueueSize => 100;

        /// <summary>
        /// If telemetry is enabled and there is at least one metrics item in the queue, then add
        /// a <c>X-Stripe-Client-Telemetry</c> header with the item; otherwise, do nothing.
        /// </summary>
        /// <param name="headers">The request headers.</param>
        public void MaybeAddTelemetryHeader(IDictionary<string, string> headers)
        {
            if (headers.ContainsKey("X-Stripe-Client-Telemetry"))
            {
                return;
            }

            RequestMetrics requestMetrics;
            if (!this.prevRequestMetrics.TryDequeue(out requestMetrics))
            {
                return;
            }

            var payload = new ClientTelemetryPayload { LastRequestMetrics = requestMetrics };

            headers.Add("X-Stripe-Client-Telemetry", JsonUtils.SerializeObject(payload, Formatting.None));
        }

        /// <summary>
        /// If telemetry is enabled and the queue is not full, then enqueue a new metrics item;
        /// otherwise, do nothing.
        /// </summary>
        /// <param name="response">The HTTP response message.</param>
        /// <param name="duration">The request duration.</param>
        public void MaybeEnqueueMetrics(HttpResponseMessage response, TimeSpan duration)
        {
            this.MaybeEnqueueMetrics(response, duration, null);
        }

        /// <summary>
        /// If telemetry is enabled and the queue is not full, then enqueue a new metrics item;
        /// otherwise, do nothing.
        /// </summary>
        /// <param name="response">The HTTP response message.</param>
        /// <param name="duration">The request duration.</param>
        /// <param name="usage">Tracked behaviors.</param>
        public void MaybeEnqueueMetrics(HttpResponseMessage response, TimeSpan duration, List<string> usage)
        {
            if (!response.Headers.Contains("Request-Id"))
            {
                return;
            }

            if (this.prevRequestMetrics.Count >= MaxRequestMetricsQueueSize)
            {
                return;
            }

            var requestId = response.Headers.GetValues("Request-Id").First();

            var metrics = new RequestMetrics
            {
                RequestId = requestId,
                RequestDurationMs = (long)duration.TotalMilliseconds,
            };

            if (usage != null && usage.Count > 0)
            {
                metrics.Usage = usage;
            }

            this.prevRequestMetrics.Enqueue(metrics);
        }

        private class ClientTelemetryPayload
        {
            [JsonProperty("last_request_metrics")]
            public RequestMetrics LastRequestMetrics { get; set; }
        }

        private class RequestMetrics
        {
            [JsonProperty("request_id")]
            public string RequestId { get; set; }

            [JsonProperty("request_duration_ms")]
            public long RequestDurationMs { get; set; }

            [JsonProperty("usage")]
            public List<string> Usage { get; set; }
        }
    }
}
