using Prodest.Caching.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Prodest.Caching.Integration
{
    public interface IHttpClient
    {
        Task<IEnumerable<User>> Get();
    }
}
