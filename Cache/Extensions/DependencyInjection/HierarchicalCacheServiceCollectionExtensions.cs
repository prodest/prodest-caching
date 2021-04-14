using Microsoft.Extensions.DependencyInjection;
using Prodest.Cache.Extensions.Caching.Hierarchical;
using System;


namespace Prodest.Cache.Extensions.DependencyInjection
{
    public static class HierarchicalCacheServiceCollectionExtensions
    {
        public static IServiceCollection AddHierarchicalCache(this IServiceCollection services, string redisConfiguration)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            if (redisConfiguration == null ) throw new ArgumentNullException(nameof(redisConfiguration));

            services.AddMemoryCache();

            //services.AddStackExchangeRedisCache(options =>
            //{
            //    options.Configuration = redisConfiguration;
            //});

            //using Microsoft.Extensions.Caching.Redis 
            //services.AddDistributedRedisCache(options =>
            //{
            //    options.Configuration = redisConfiguration;
            //});

            Action<HierarchicalCacheOptions> setupAction = options =>
            {
                options.RedisConfiguration = redisConfiguration;
            };

            services.AddOptions();

            services.Configure(setupAction);

            services.Add(ServiceDescriptor.Singleton<IHierarchicalCache, HierarchicalCache>());

            return services;
        }
    }
}