using System.Collections.Generic;
using System.Data;

namespace SigQL
{
    public interface IQueryExecutor
    {
        IDataReader ExecuteReader(string commandText);
        IDataReader ExecuteReader(string commandText, IDictionary<string, object> parameters);
        void ExecuteNonQuery(string commandText, IDictionary<string, object> parameters);
    }
}
