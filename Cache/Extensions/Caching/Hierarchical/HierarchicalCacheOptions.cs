using Microsoft.Extensions.Options;

namespace Prodest.Cache.Extensions.Caching.Hierarchical
{
    public class HierarchicalCacheOptions : IOptions<HierarchicalCacheOptions>
    {
        public string RedisConfiguration { get; set; }
        public bool UseMemoryCache { get; set; } = true;
        public bool UseDistributedCache { get; set; } = true;

        HierarchicalCacheOptions IOptions<HierarchicalCacheOptions>.Value
        {
            get { return this; }
        }
    }
}