using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Prodest.Cache.Extensions.Caching.Hierarchical
{
    public sealed class HierarchicalCache : IHierarchicalCache, IDisposable
    {
        private volatile ConnectionMultiplexer Connection;

        private IDatabase DistributedCache;

        private readonly HierarchicalCacheOptions Options;

        private readonly IMemoryCache MemoryCache;

        private readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            PreserveReferencesHandling = PreserveReferencesHandling.Objects,
            NullValueHandling = NullValueHandling.Ignore,
            TypeNameHandling = TypeNameHandling.All,
            ReferenceLoopHandling = ReferenceLoopHandling.Serialize
        };

        private readonly SemaphoreSlim ConnectionLock = new SemaphoreSlim(initialCount: 1, maxCount: 1);

        public HierarchicalCache(IMemoryCache memoryCache, IOptions<HierarchicalCacheOptions> optionsAccessor)
        {
            if (optionsAccessor == null) throw new ArgumentNullException(nameof(optionsAccessor));

            MemoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            Options = optionsAccessor.Value;

            Connect();

            ISubscriber sub = Connection.GetSubscriber();

            sub.Subscribe("deleteKeys", (channel, message) =>
            {
                if (Options.UseMemoryCache)
                    DeleteMemoryKeys();
            });


            sub.Subscribe("deleteKey", (channel, message) =>
            {
                if (Options.UseMemoryCache)
                    MemoryCache.Remove((string)message);
            });
        }

        public void Dispose()
        {
            if (Connection != null)
            {
                Connection.Close();
            }
        }

        public async Task<int> FlushAllDatabasesAsync()
        {
            int amountKeys = 0;

            if (Options.UseDistributedCache)
            {
                ConnectAsync();

                IServer server = null;

                EndPoint[] endPoints = Connection.GetEndPoints();
                if (endPoints != null && endPoints.Any())
                {
                    foreach (var endPoint in endPoints)
                    {
                        server = Connection.GetServer(endPoint);
                        if (server.IsConnected)
                            break;
                    }
                }

                if (server != null && server.IsConnected)
                {
                    IEnumerable<RedisKey> allKeys = server.Keys();
                    if (allKeys != null)
                    {
                        amountKeys = allKeys.Count();

                        await DistributedCacheDelete("keys");

                        foreach (RedisKey key in allKeys)
                        {
                            if (!((string)key).Equals("keys") && !((string)key).Equals("systemOff"))
                                await DistributedCacheDelete(key);

                            await DistributedCacheAddSet(key);
                        }
                    }
                }

                await DistributedCachePublish("deleteKeys", "deleteKeys");

            }

            return amountKeys;
        }



        public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan absoluteExpiration, Func<Task<T>> factory)
        {
            T retorno = default;

            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("O parâmetro key deve possuir valor.", nameof(key));

            if (factory == null) throw new ArgumentNullException(nameof(factory));

            retorno = await GetOrCreateInternalAsync(key, absoluteExpiration, factory);

            return retorno;
        }

        public async Task<T> GetAsync<T>(string key)
        {
            T retorno = default;

            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("O parâmetro key deve possuir valor.", nameof(key));

            retorno = await GetInternalAsync<T>(key);

            return retorno;
        }

        public async Task<string> GetStringAsync(string key)
        {
            string retorno = default;

            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("O parâmetro key deve possuir valor.", nameof(key));

            retorno = await DistributedCacheGet(key);

            return retorno;
        }

        public async Task CreateAsync(string key, object value, TimeSpan? absoluteExpiration = null)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("O parâmetro key deve possuir valor.", nameof(key));

            await CreateInternalAsync(key, value, absoluteExpiration);
        }

        public async Task<ICollection<KeyValuePair<string, long>>> ListKeysLength()
        {
            ICollection<KeyValuePair<string, long>> valuePairs = new List<KeyValuePair<string, long>>();

            if (Options.UseDistributedCache)
            {
                await ConnectAsync();

                IServer server = null;

                EndPoint[] endPoints = Connection.GetEndPoints();
                if (endPoints != null && endPoints.Any())
                {
                    foreach (var endPoint in endPoints)
                    {
                        server = Connection.GetServer(endPoint);
                        if (server.IsConnected)
                            break;
                    }
                }

                if (server != null && server.IsConnected)
                {
                    IEnumerable<RedisKey> allKeys = server.Keys();

                    if (allKeys != null)
                    {
                        foreach (RedisKey key in allKeys)
                        {
                            long? stringLength = await DistributedCacheGetLength(key);

                            if (stringLength.HasValue)
                                valuePairs.Add(new KeyValuePair<string, long>(key, stringLength.Value));
                        }

                        valuePairs = valuePairs
                            .OrderByDescending(vp => vp.Value)
                            .ToList();
                    }
                }
            }

            return valuePairs;
        }



        public async Task<ICollection<T>> ListSetMembers<T>(string key)
        {
            await ConnectAsync();

            ICollection<T> list = null;
            RedisValue[] members = await DistributedCacheGetMembers(key);

            if (members != null && members.Any())
            {
                list = new List<T>();

                foreach (RedisValue member in members)
                {
                    T memberList = default(T);
                    try
                    {
                        memberList = JsonConvert.DeserializeObject<T>(member, JsonSettings);
                    }
                    catch { }

                    if (memberList != null)
                        list.Add(memberList);
                }
            }

            return list;
        }



        public async Task PubAsync(string channel, string message)
        {
            if (Options.UseDistributedCache)
            {
                await ConnectAsync();
                await DistributedCachePublish(channel, message);
            }
        }

     

        public async Task RemoveKeyAsync(string key)
        {
            if (Options.UseDistributedCache)
            {
                await ConnectAsync();
                await DistributedCacheDelete(key);

                await PubAsync("deleteKey", key);
            }
            else if (Options.UseMemoryCache)
                MemoryCache.Remove(key);
        }


        public async Task RemoveListAsync(string key)
        {
            await ConnectAsync();

            await DistributedCacheDelete(key);
        }

        private async Task ConnectAsync(CancellationToken token = default(CancellationToken))
        {
            token.ThrowIfCancellationRequested();

            if (DistributedCache != null)
            {
                return;
            }

            await ConnectionLock.WaitAsync(token);
            try
            {
                if (DistributedCache == null)
                {
                    Connection = await ConnectionMultiplexer.ConnectAsync(Options.RedisConfiguration);

                    DistributedCache = Connection.GetDatabase();
                }
            }
            finally
            {
                ConnectionLock.Release();
            }
        }

        private void Connect()
        {
            if (DistributedCache != null)
            {
                return;
            }

            ConnectionLock.Wait();
            try
            {
                if (DistributedCache == null)
                {

                    Connection = ConnectionMultiplexer.Connect(Options.RedisConfiguration);

                    DistributedCache = Connection.GetDatabase();
                }
            }
            finally
            {
                ConnectionLock.Release();
            }
        }

        private async Task<T> GetOrCreateInternalAsync<T>(string key, TimeSpan absoluteExpiration, Func<Task<T>> factory)
        {
            T cachedObject = default(T);

            if (Options.UseMemoryCache)
            {
                cachedObject = await MemoryCache.GetOrCreateAsync(key, async c =>
                {
                    c.SetAbsoluteExpiration(absoluteExpiration);

                    T returnObject = default(T);

                    returnObject = await GetOrCreateInternalAsync<T>(key, absoluteExpiration, factory, Options.UseDistributedCache);

                    return returnObject;
                });

                if (cachedObject == null)
                    MemoryCache.Remove(key);
            }
            else
            {
                cachedObject = await GetOrCreateInternalAsync<T>(key, absoluteExpiration, factory, Options.UseDistributedCache);
            }

            return cachedObject;
        }

        private async Task<T> GetOrCreateInternalAsync<T>(string key, TimeSpan absoluteExpiration, Func<Task<T>> factory, bool useDistributedCache)
        {
            T returnObject = default(T);

            if (useDistributedCache)
            {
                await ConnectAsync();
                string serializedObject = await DistributedCacheGet(key);

                if (!string.IsNullOrWhiteSpace(serializedObject))
                {
                    try
                    {
                        returnObject = JsonConvert.DeserializeObject<T>(serializedObject, JsonSettings);
                    }
                    catch { }
                }

                if (returnObject == null)
                {
                    returnObject = await factory();
                    if (returnObject != null)
                    {
                        string serializedReturnObject = JsonConvert.SerializeObject(returnObject, JsonSettings);
                        await DistributedCacheAdd(key, absoluteExpiration, serializedReturnObject);
                    }
                }
            }
            else
                returnObject = await factory();

            return returnObject;
        }



        #region Métodos de acesso ao Distributed Cache

        private async Task DistributedCachePublish(string channel, string message)
        {
            try
            {
                ISubscriber sub = Connection.GetSubscriber();
                await sub.PublishAsync(channel, message);
            }
            catch (Exception e)
            {
            }
        }
       
        private async Task DistributedCacheDelete(string key)
        {
            try
            {
                await DistributedCache.KeyDeleteAsync(key);
            }
            catch (Exception e)
            {

            }
        }
        private async Task<RedisValue[]> DistributedCacheGetMembers(string key)
        {
            RedisValue[] redisValues = null;
            try
            {
                redisValues = await DistributedCache.SetMembersAsync(key);
            }
            catch (Exception e)
            {

            }
            return redisValues;
        }

        private async Task DistributedCacheAddSet(RedisKey key)
        {
            try
            {
                await DistributedCache.SetAddAsync("keys", (string)key);
            }
            catch (Exception e)
            {

            }
        }
        private async Task DistributedCacheAdd(string key, TimeSpan? absoluteExpiration, string serializedReturnObject)
        {
            try
            {
                await DistributedCache.StringSetAsync(key, serializedReturnObject, absoluteExpiration);
            }
            catch (Exception e)
            {

            }
        }

        private async Task<string> DistributedCacheGet(string key)
        {
            string serializedObject = null;
            try
            {
                await ConnectAsync();
                RedisValue redisValue = RedisValue.Null;
                redisValue = await DistributedCache.StringGetAsync(key);
                serializedObject = redisValue.ToString();
            }
            catch (Exception e)
            {

            }
            return serializedObject;
        }

        private async Task<long?> DistributedCacheGetLength(RedisKey key)
        {
            long? stringLength = null;
            try
            {
                await ConnectAsync();
                RedisValue redisValue = RedisValue.Null;
                redisValue = await DistributedCache.StringGetAsync(key);
                if (redisValue != RedisValue.Null)
                    stringLength = redisValue.Length();
            }
            catch (Exception)
            {

            }

            return stringLength;
        }
        #endregion


        private async Task<T> GetInternalAsync<T>(string key)
        {
            T cachedObject = default(T);

            if (Options.UseMemoryCache)
            {
                cachedObject = MemoryCache.Get<T>(key);
                if (cachedObject == null)
                {
                    if (Options.UseDistributedCache)
                        cachedObject = await GetDistributedAsync<T>(key);

                    if (cachedObject != null)
                        MemoryCache.Set(key, cachedObject);
                    else
                        MemoryCache.Remove(key);
                }
            }
            else if (Options.UseDistributedCache)
                cachedObject = await GetDistributedAsync<T>(key);

            return cachedObject;
        }



        private async Task<T> GetDistributedAsync<T>(string key)
        {
            await ConnectAsync();

            T returnObject = default(T);

            string serializedObject = await DistributedCacheGet(key);

            if (!string.IsNullOrWhiteSpace(serializedObject))
            {
                try
                {
                    returnObject = JsonConvert.DeserializeObject<T>(serializedObject, JsonSettings);
                }
                catch { }
            }

            return returnObject;
        }

        private async Task CreateInternalAsync(string key, object value, TimeSpan? absoluteExpiration)
        {
            if (Options.UseMemoryCache)
            {
                if (Options.UseDistributedCache)
                    await CreateDistributedAsync(key, value, absoluteExpiration);

                if (absoluteExpiration.HasValue)
                    MemoryCache.Set(key, value, absoluteExpiration.Value);
                else
                    MemoryCache.Set(key, value);
            }
            else if (Options.UseDistributedCache)
                await CreateDistributedAsync(key, value, absoluteExpiration);
        }

        private async Task CreateDistributedAsync(string key, object value, TimeSpan? absoluteExpiration)
        {
            await ConnectAsync();

            string serializedReturnObject = null;

            if (!value.GetType().Equals(typeof(string)))
                serializedReturnObject = JsonConvert.SerializeObject(value, JsonSettings);
            else
                serializedReturnObject = (string)value;

            await DistributedCacheAdd(key, absoluteExpiration, serializedReturnObject);
        }

        private void DeleteMemoryKeys()
        {
            Connect();

            RedisValue[] keys = DistributedCacheGetMembers("keys").Result;

            foreach (RedisValue key in keys)
            {
                MemoryCache.Remove((string)key);
            }
        }
    }
}