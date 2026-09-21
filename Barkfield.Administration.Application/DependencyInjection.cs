using Barkfield.Administration.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            services.AddScoped<CustomerService>();
            services.AddScoped<UserService>();

            return services;
        }
    }
}
