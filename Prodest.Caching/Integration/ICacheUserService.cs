using Prodest.Caching.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Prodest.Caching.Integration
{
    public interface ICacheUserService
    {
        Task<IEnumerable<User>> GetCachedUser();
        Task ClearCache();
    }
}
