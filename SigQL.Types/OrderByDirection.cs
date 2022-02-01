namespace SigQL.Types
{
    public enum OrderByDirection
    {
        Ascending,
        Descending
    }

    public interface IOrderBy
    {
        string Table { get; }
        string Column { get; }
        OrderByDirection Direction { get; }
    }

    public class OrderBy : IOrderBy
    {
        public OrderBy(string table, string column, OrderByDirection direction)
        {
            Table = table;
            Column = column;
            Direction = direction;
        }

        public OrderBy(string table, string column)
        {
            Table = table;
            Column = column;
            Direction = OrderByDirection.Ascending;
        }

        public string Table { get; }
        public string Column { get; }
        public OrderByDirection Direction { get; }
    }
}
