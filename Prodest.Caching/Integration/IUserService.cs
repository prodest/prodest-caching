using Prodest.Caching.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Prodest.Caching.Integration
{
    public interface IUserService
    {
        Task<IEnumerable<User>> GetUsersAsync();
    }
}
