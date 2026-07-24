using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace SigQL
{
    public interface IQueryExecutor
    {
        Task<IDataReader> ExecuteReaderAsync(string commandText, IDictionary<string, object> parameters, int? commandTimeout = null);
        IDataReader ExecuteReader(string commandText, IDictionary<string, object> parameters, int? commandTimeout = null);
        Task<int> ExecuteNonQueryAsync(string commandText, IDictionary<string, object> parameters, int? commandTimeout = null);
        int ExecuteNonQuery(string commandText, IDictionary<string, object> parameters, int? commandTimeout = null);
    }
}
