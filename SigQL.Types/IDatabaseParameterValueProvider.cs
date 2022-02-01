namespace SigQL.Types
{
    public interface IDatabaseParameterValueProvider
    {
        object SqlValue { get; }
    }

    public interface IWhereClauseFilterParameter : IDatabaseParameterValueProvider
    {

    }
}