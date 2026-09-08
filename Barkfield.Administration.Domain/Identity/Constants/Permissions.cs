using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Domain.Identity.Constants
{
    public static class Permissions
    {
        public static class Users
        {
            public const string Create = "user:create";
            public const string Delete = "user:delete";
        }

        public static class Square
        {
            public const string CreateCart = "square:createcart";
            
        }

        public static class Customers
        {
            public const string Create = "customer:create";
        }

    }
}
