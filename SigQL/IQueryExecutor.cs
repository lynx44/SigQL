using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace SigQL
{
    public interface IQueryExecutor
    {
        //IDataReader ExecuteReader(string commandText);
        Task<IDataReader> ExecuteReaderAsync(string commandText);
        //IDataReader ExecuteReader(string commandText, IDictionary<string, object> parameters);
        Task<IDataReader> ExecuteReaderAsync(string commandText, IDictionary<string, object> parameters);
        Task<int> ExecuteNonQueryAsync(string commandText, IDictionary<string, object> parameters);
    }
}
