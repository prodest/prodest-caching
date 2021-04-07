using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Caching.Distributed;
using Prodest.Caching.Infrastructure;
using Prodest.Caching.Services;
using Prodest.Caching.Integration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WebCache
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            //services.AddStackExchangeRedisCache(options =>
            //    options.Configuration = Configuration.GetConnectionString("RedisConnection")

            //);

            //services.Add(ServiceDescriptor.Singleton<IDistributedCache, RedisCache>());

            //services.AddControllersWithViews();

            services.AddControllersWithViews();
            services.AddHttpClient();
            services.AddHttpClient<HttpClient>();

            // Register the RedisCache service
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = Configuration.GetConnectionString("RedisConnection");


            });

            // Services from our previos tutorial
            services.AddScoped<UserService>();
            services.AddScoped<IUserService, CachedUserService>();
            services.AddScoped<ICacheUserService, CacheUserService>();
            services.AddScoped<IHttpClient, HttpClient>();
            services.AddScoped<ICacheProvider, CacheProvider>();


        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
