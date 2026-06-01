using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace OpenBI.Connectors.PowerBI.Http
{
    /// <summary>
    /// DelegatingHandler that retries requests when the server returns 429 (Too Many Requests),
    /// waiting for the duration specified in the Retry-After header before retrying.
    /// </summary>
    public class RetryAfterDelegatingHandler : DelegatingHandler
    {
        private const int DefaultRetryAfterSeconds = 60;
        private const int MaxRetryAfterSeconds = 3600;

        private readonly ILogger? _logger;
        private readonly int _maxRetries;
        private readonly int _defaultRetryAfterSeconds;

        public RetryAfterDelegatingHandler(
            ILogger? logger = null,
            int maxRetries = 5,
            int defaultRetryAfterSeconds = DefaultRetryAfterSeconds)
        {
            _logger = logger;
            _maxRetries = maxRetries;
            _defaultRetryAfterSeconds = defaultRetryAfterSeconds;
        }

        public RetryAfterDelegatingHandler(HttpMessageHandler innerHandler, ILogger? logger = null, int maxRetries = 5, int defaultRetryAfterSeconds = DefaultRetryAfterSeconds)
            : base(innerHandler)
        {
            _logger = logger;
            _maxRetries = maxRetries;
            _defaultRetryAfterSeconds = defaultRetryAfterSeconds;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var attempt = 0;
            HttpResponseMessage? response = null;

            while (true)
            {
                response?.Dispose();
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.TooManyRequests)
                    return response;

                attempt++;
                if (attempt > _maxRetries)
                {
                    _logger?.LogWarning("Received 429 (Too Many Requests) after {MaxRetries} retries. Returning last response.", _maxRetries);
                    return response;
                }

                var delaySeconds = ParseRetryAfter(response);
                _logger?.LogInformation(
                    "Received 429 (Too Many Requests). Retry-After: {DelaySeconds}s. Waiting before retry (attempt {Attempt}/{MaxRetries}).",
                    delaySeconds, attempt, _maxRetries);

                response.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Parses the Retry-After header. Supports integer seconds or HTTP-date (RFC 7231).
        /// </summary>
        private int ParseRetryAfter(HttpResponseMessage response)
        {
            if (response.Headers.RetryAfter?.Delta.HasValue == true)
                return (int)Math.Min(MaxRetryAfterSeconds, response.Headers.RetryAfter.Delta.Value.TotalSeconds);

            if (response.Headers.RetryAfter?.Date.HasValue == true)
            {
                var delay = (response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds;
                return (int)Math.Clamp(delay, 0, MaxRetryAfterSeconds);
            }

            if (!response.Headers.TryGetValues("Retry-After", out var values))
            {
                _logger?.LogDebug("Retry-After header missing, using default {Default}s.", _defaultRetryAfterSeconds);
                return _defaultRetryAfterSeconds;
            }

            var raw = string.Join(",", values).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                _logger?.LogDebug("Retry-After header empty, using default {Default}s.", _defaultRetryAfterSeconds);
                return _defaultRetryAfterSeconds;
            }
            if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
                return Math.Min(MaxRetryAfterSeconds, Math.Max(0, seconds));

            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            {
                var delay = (date - DateTimeOffset.UtcNow).TotalSeconds;
                return (int)Math.Clamp(delay, 0, MaxRetryAfterSeconds);
            }

            _logger?.LogDebug("Retry-After header value '{Value}' could not be parsed, using default {Default}s.", raw, _defaultRetryAfterSeconds);
            return _defaultRetryAfterSeconds;
        }
    }
}
