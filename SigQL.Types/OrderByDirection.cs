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

    public class OrderByRelation : IOrderBy
    {

        public OrderByRelation(string viaRelationPath) : this(viaRelationPath, OrderByDirection.Ascending)
        {
        }

        public OrderByRelation(string viaRelationPath, OrderByDirection direction)
        {
            ViaRelationPath = viaRelationPath;
            Direction = direction;
        }

        public string Table { get; internal set; }
        public string Column { get; internal set; }
        internal string ViaRelationPath { get; }
        public OrderByDirection Direction { get; set; }
    }
}
