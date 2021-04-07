using System.Collections.Generic;
using System.Threading.Tasks;
using Prodest.Caching.Integration;
using Prodest.Caching.Models;

namespace Prodest.Caching.Services
{    
    public class UserService : IUserService
    {
        private readonly IHttpClient _httpClient;

        public UserService(IHttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public Task<IEnumerable<User>> GetUsersAsync()
        {
            return _httpClient.Get();
        }
    }
}