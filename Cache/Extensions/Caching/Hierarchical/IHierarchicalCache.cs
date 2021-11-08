using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Prodest.Cache.Extensions.Caching.Hierarchical
{
    public interface IHierarchicalCache
    {
        Task<int> FlushAllDatabasesAsync();
        Task<T> GetOrCreateAsync<T>(string key, TimeSpan absoluteExpiration, Func<Task<T>> factory);
        Task<T> GetAsync<T>(string key, TimeSpan? absoluteExpiration = null);
        Task<string> GetStringAsync(string key);
        Task CreateAsync(string key, object value, TimeSpan? absoluteExpiration = null);
        Task<ICollection<KeyValuePair<string, long>>> ListKeysLength();
        Task<ICollection<T>> ListSetMembers<T>(string key);
        Task PubAsync(string channel, string message);
        Task RemoveListAsync(string key);
        Task RemoveKeyAsync(string key);
    }
}