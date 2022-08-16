using System;
using System.Collections.Generic;
using System.Text;

namespace SigQL.Types
{
    public interface ICountResult<TResult>
    {
        int Count { get; }
    }
}
