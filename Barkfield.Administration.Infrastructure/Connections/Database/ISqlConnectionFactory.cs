using System.Data;

namespace Barkfield.Administration.Infrastructure.Connections.Database
{
    public interface ISqlConnectionFactory
    {
        IDbConnection CreateConnection();
    }
}