using System;
using System.Collections.Concurrent;

namespace OpenBI.Connectors.PowerBI
{
    /// <summary>
    /// In-memory, multi-tenant cache for Power BI API responses to reduce throttling when many calls are made.
    /// Access via <see cref="Instance"/>. TTL is configurable via <see cref="Configure"/>.
    /// Cached values are the raw API response objects (same type as the Power BI SDK returns); use .Value.Value to access the list.
    /// </summary>
    public class PowerBIApiCache
    {
        private static readonly Lazy<PowerBIApiCache> _instance = new Lazy<PowerBIApiCache>(() => new PowerBIApiCache());
        private static TimeSpan _defaultMaxAge = TimeSpan.FromMinutes(30);

        private readonly ConcurrentDictionary<string, CachedEntry> _store = new();
        private TimeSpan _maxAge;

        private const string KeyReports = "ReportsInGroupAsAdmin";
        private const string KeyDatasets = "DatasetsInGroupAsAdmin";

        private PowerBIApiCache()
        {
            _maxAge = _defaultMaxAge;
        }

        /// <summary>
        /// Static instance of the cache. Lazy-initialized.
        /// </summary>
        public static PowerBIApiCache Instance => _instance.Value;

        /// <summary>
        /// Configures the default TTL for cache entries. Call before first use (e.g. from host via reflection).
        /// </summary>
        public static void Configure(TimeSpan maxAge)
        {
            _defaultMaxAge = maxAge;
            if (_instance.IsValueCreated)
                _instance.Value._maxAge = maxAge;
        }

        /// <summary>
        /// Sets the TTL for this instance (e.g. after Configure was called before Instance was created).
        /// </summary>
        public void SetMaxAge(TimeSpan maxAge)
        {
            _maxAge = maxAge;
        }

        private static string Key(string tenantId, string operation, Guid groupId) => $"{tenantId}|{operation}|{groupId}";

        private bool TryGet(string key, out object? value)
        {
            value = null;
            if (!_store.TryGetValue(key, out var entry))
                return false;
            if (entry.UtcCachedAt < DateTime.UtcNow - _maxAge)
            {
                _store.TryRemove(key, out _);
                return false;
            }
            value = entry.Response;
            return true;
        }

        private void Set(string key, object value)
        {
            _store[key] = new CachedEntry(DateTime.UtcNow, value);
        }

        /// <summary>
        /// Tries to get cached reports for the given tenant and group. Returns true if a valid (non-expired) entry exists.
        /// The out value is the same type as Reports.GetReportsInGroupAsAdmin returns (use .Value.Value for the list).
        /// </summary>
        public bool TryGetReportsInGroup(string tenantId, Guid groupId, out object? value)
        {
            return TryGet(Key(tenantId, KeyReports, groupId), out value);
        }

        /// <summary>
        /// Caches the reports response for the given tenant and group.
        /// </summary>
        public void SetReportsInGroup(string tenantId, Guid groupId, object value)
        {
            Set(Key(tenantId, KeyReports, groupId), value);
        }

        /// <summary>
        /// Tries to get cached datasets for the given tenant and group. Returns true if a valid (non-expired) entry exists.
        /// The out value is the same type as Datasets.GetDatasetsInGroupAsAdmin returns (use .Value.Value for the list).
        /// </summary>
        public bool TryGetDatasetsInGroup(string tenantId, Guid groupId, out object? value)
        {
            return TryGet(Key(tenantId, KeyDatasets, groupId), out value);
        }

        /// <summary>
        /// Caches the datasets response for the given tenant and group.
        /// </summary>
        public void SetDatasetsInGroup(string tenantId, Guid groupId, object value)
        {
            Set(Key(tenantId, KeyDatasets, groupId), value);
        }

        /// <summary>
        /// Removes all cached entries for the given tenant and workspace, forcing a fresh fetch on next access.
        /// Call after any upload that mutates workspace contents.
        /// </summary>
        public void InvalidateWorkspace(string tenantId, Guid groupId)
        {
            _store.TryRemove(Key(tenantId, KeyReports, groupId), out _);
            _store.TryRemove(Key(tenantId, KeyDatasets, groupId), out _);
        }

        private sealed class CachedEntry
        {
            public DateTime UtcCachedAt { get; }
            public object Response { get; }

            public CachedEntry(DateTime utcCachedAt, object response)
            {
                UtcCachedAt = utcCachedAt;
                Response = response;
            }
        }
    }
}
