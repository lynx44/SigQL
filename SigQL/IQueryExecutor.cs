﻿using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace SigQL
{
    public interface IQueryExecutor
    {
        Task<IDataReader> ExecuteReaderAsync(string commandText, IDictionary<string, object> parameters);
        IDataReader ExecuteReader(string commandText, IDictionary<string, object> parameters);
        Task<int> ExecuteNonQueryAsync(string commandText, IDictionary<string, object> parameters);
        int ExecuteNonQuery(string commandText, IDictionary<string, object> parameters);
    }
}
