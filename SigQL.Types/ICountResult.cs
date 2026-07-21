using System;
using System.Collections.Generic;
using System.Text;

namespace SigQL.Types
{
    public interface ICountResult<TResult>
    {
        int Count { get; }
    }

    public interface ITotalCount<T>
    {
        int TotalCount { get; }
    }

    public interface ITotalCountResult<TResult>
    {
        int TotalCount { get; }
        TResult Result { get; }
    }
}
