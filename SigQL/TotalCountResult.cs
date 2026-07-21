using SigQL.Types;

namespace SigQL
{
    internal class TotalCountResult<TResult> : ITotalCountResult<TResult>
    {
        public int TotalCount { get; set; }
        public TResult Result { get; set; }
    }
}
