using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Text;

namespace Barkfield.Administration.Infrastructure.Connections.Database
{

    public class SqlConnectionFactory(string connectionString) : ISqlConnectionFactory
    {
        public IDbConnection CreateConnection()
        {
            return new SqlConnection(connectionString);
        }

    }
}
