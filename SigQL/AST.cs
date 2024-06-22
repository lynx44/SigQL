using System.Collections.Generic;
using System.Linq;

namespace SigQL
{
    public class Clause : AstNode
    {
        public string Keyword { get; set; }
    }

    public class SelectClause : Clause
    {
        public SelectClause()
        {
            this.Keyword = "select";
        }

        public string Modifier { get; set; }
    }

    public class FromClause : Clause
    {
        public FromClause()
        {
            this.Keyword = "from";
        }
    }

    public class WhereClause : Clause
    {
        public WhereClause()
        {
            this.Keyword = "where";
        }
    }

    public class OffsetClause : Clause
    {
        public FetchClause Fetch { get; set; }
        public AstNode OffsetCount { get; set; }
    }

    public class FetchClause : Clause
    {
        public AstNode FetchCount { get; set; }
    }

    public class OverClause : Clause
    {
        public OverClause()
        {
            this.Keyword = "over";
        }

        public Function Function { get; set; }
    }

    public class OrderByClause : Clause
    {
        public OrderByClause()
        {
            this.Keyword = "order by";
        }

        public OffsetClause Offset { get; set; }
    }

    public class PartitionByClause : Clause
    {
        public PartitionByClause()
        {
            this.Keyword = "partition by";
        }
    }

    public class GroupByClause : Clause
    {
        public GroupByClause()
        {
            this.Keyword = "group by";
        }
    }

    public class OrderByIdentifier : AstNode
    {
        public string Direction { get; set; }
    }

    public class If : AstNode
    {
        public AstNode Condition { get; set; }
    }

    public class Statement : AstNode
    {
        public IEnumerable<Clause> Where { get; set; }
    }

    public class DataManipulationStatement : Statement
    {
        
    }

    public class DeclareStatement : AstNode
    {
        public NamedParameterIdentifier Parameter { get; set; }
        public DataType DataType { get; set; }
    }

    public class SetParameter : AstNode
    {
        public NamedParameterIdentifier Parameter { get; set; }
        public AstNode Value { get; set; }
    }

    public class DataType : AstNode
    {
        public Literal Type { get; set; }
    }

    public class ColumnDeclaration : AstNode
    {

    }

    public class FromClauseNode : AstNode
    {

    }

    public class Select : DataManipulationStatement
    {
        public SelectClause SelectClause { get; set; }
        public FromClause FromClause { get; set; }
        public WhereClause WhereClause { get; set; }
        public GroupByClause GroupByClause { get; set; }
        public OrderByClause OrderByClause { get; set; }
    }

    public class Delete : DataManipulationStatement
    {
        public FromClause FromClause { get; set; }
        public WhereClause WhereClause { get; set; }
    }

    public class Update : DataManipulationStatement
    {
        public IEnumerable<SetEqualOperator> SetClause;
        public FromClause FromClause { get; set; }
        public WhereClause WhereClause { get; set; }
    }

    public class Insert : DataManipulationStatement
    {
        public AstNode Object { get; set; }
        public IEnumerable<AstNode> ColumnList { get; set; }
        public OutputClause Output { get; set; }
        public ValuesListClause ValuesList { get; set; }
    }

    public class IntoClause : Clause
    {
        public AstNode Object;
    }

    public class OutputClause : Clause
    {
        public IntoClause Into { get; set; }
    }

    public class ValuesListClause : Clause
    {

    }

    public class ValuesList : AstNode
    {

    }

    public class Merge : AstNode
    {
        public TableIdentifier Table { get; set; }
        public MergeUsing Using { get; set; }
        public AstNode On { get; set; }
        public WhenNotMatched WhenNotMatched { get; set; }
    }

    public class MergeUsing : AstNode
    {
        public AstNode Values { get; set; }
        public TableAliasDefinition As { get; set; }
    }

    public class WhenNotMatched : AstNode
    {
        public MergeInsert Insert { get; set; }
    }

    public class MergeInsert : AstNode
    {
        public IEnumerable<AstNode> ColumnList { get; set; }
        public OutputClause Output { get; set; }
        public ValuesListClause ValuesList { get; set; }
    }

    public class TableAliasDefinition : AstNode
    {
        public string Alias { get; set; }
    }

    public class SubqueryAlias : AstNode
    {
        public string Alias { get; set; }
    }

    public class AstNode
    {
        public IEnumerable<AstNode> Args { get; set; }
    }

    public static class AstNodeExtensions
    {
        public static T SetArgs<T>(this T node, params AstNode[] args) where T : AstNode
        {
            node.Args = args;
            return node;
        }
        public static T AppendArgs<T>(this T node, params AstNode[] args) where T : AstNode
        {
            return node.AppendArgs((IEnumerable<AstNode>)args);
        }
        public static T SetArgs<T>(this T node, IEnumerable<AstNode> args) where T : AstNode
        {
            node.Args = args.ToList();
            return node;
        }
        public static T AppendArgs<T>(this T node, IEnumerable<AstNode> args) where T : AstNode
        {
            if (node.Args == null)
            {
                return node.SetArgs(args);
            }
            node.Args = node.Args.Concat(args).ToList();
            return node;
        }
    }

    public class ObjectIdentifier : AstNode
    {
        public string Label { get; set; }
    }

    public class ColumnIdentifier : ObjectIdentifier
    {
    }

    public class TableIdentifier : ObjectIdentifier
    {

    }

    public class SchemaIdentifier : ObjectIdentifier
    {

    }

    public class NamedParameterIdentifier : AstNode
    {
        public string Name { get; set; }
    }

    public class DatabaseCatalogObject : AstNode
    {
        public string Label { get; set; }
    }

    public class RelationalColumn : DatabaseCatalogObject
    {

    }

    public class RelationalTable : DatabaseCatalogObject
    {

    }

    public class RelationalSchema : DatabaseCatalogObject
    {

    }

    public class Placeholder : AstNode
    {
    }

    public class Function : AstNode
    {
        public string Name { get; set; }
    }

    public class AggregateFunction : Function
    {

    }

    public class Sum : AggregateFunction
    {
        public Sum()
        {
            this.Name = "sum";
        }
    }

    public class Count : AggregateFunction
    {
        public Count()
        {
            this.Name = "count";
        }
    }

    // how would this work?
    // public class UserFunctionIdentifier : ObjectIdentifier
    // {
    // }

    public class LogicalGrouping : AstNode
    {
    }

    public class Exists : AstNode
    {
    }

    public class NotExists : AstNode
    {
    }

    public class Operator : AstNode
    {
        public string Label { get; set; }
    }

    public class LogicalOperator : Operator
    {

    }

    public class ConditionalOperator : LogicalOperator
    {

    }

    public class AndOperator : ConditionalOperator
    {
        public AndOperator()
        {
            this.Label = "and";
        }
    }

    public class OrOperator : ConditionalOperator
    {
        public OrOperator()
        {
            this.Label = "or";
        }
    }

    public class IsOperator : LogicalOperator
    {
        public IsOperator()
        {
            this.Label = "is";
        }
    }

    public class IsNotOperator : LogicalOperator
    {
        public IsNotOperator()
        {
            this.Label = "is not";
        }
    }

    public class EqualsOperator : LogicalOperator
    {
        public EqualsOperator()
        {
            this.Label = "=";
        }
    }

    public class NotEqualOperator : LogicalOperator
    {
        public NotEqualOperator()
        {
            this.Label = "!=";
        }
    }

    // for update statements update blah set --> x = 'y' <--
    public class SetEqualOperator : LogicalOperator
    {
        public SetEqualOperator()
        {
            this.Label = "=";
        }
    }

    public class GreaterThanOperator : LogicalOperator
    {
        public GreaterThanOperator()
        {
            this.Label = ">";
        }
    }

    public class GreaterThanOrEqualToOperator : LogicalOperator
    {
        public GreaterThanOrEqualToOperator()
        {
            this.Label = ">=";
        }
    }

    public class LessThanOperator : LogicalOperator
    {
        public LessThanOperator()
        {
            this.Label = "<";
        }
    }

    public class LessThanOrEqualToOperator : LogicalOperator
    {
        public LessThanOrEqualToOperator()
        {
            this.Label = "<=";
        }
    }

    public class LikePredicate : Predicate
    {
        public override string Keyword => "like";
    }

    public class NotLikePredicate : LikePredicate
    {
        public override string Keyword => "not like";
    }

    public class Alias : Operator
    {
    }

    public class Literal : AstNode
    {
        public string Value { get; set; }
    }

    public class NullLiteral : Literal
    {
        public NullLiteral()
        {
            this.Value = "null";
        }
    }

    public abstract class Predicate : AstNode
    {
        public abstract string Keyword { get; }
    }

    public class Join : Predicate
    {
        public string JoinType { get; set; }
        public AstNode RightNode { get; set; }

        public override string Keyword => this.JoinType;
    }

    // public class CrossJoin : Join
    // {
    // }

    public class OuterJoin : Join
    {
    }

    public class InnerJoin : Join
    {
        public InnerJoin()
        {
            this.JoinType = "inner";
        }
    }

    public class LeftOuterJoin : OuterJoin
    {
        public LeftOuterJoin()
        {
            this.JoinType = "left outer";
        }
    }

    public class InPredicate : Predicate
    {
        public AstNode LeftComparison { get; set; }
        public override string Keyword => "in";
    }

    public class NotInPredicate : InPredicate
    {
        public override string Keyword => "not in";
    }

    // public class RightOuterJoin : OuterJoin
    // {
    // }
    //
    // public class FullOuterJoin : OuterJoin
    // {
    // }

    // public class JoinOn : Operator
    // {
    //     public JoinOn()
    //     {
    //         this.Label = "on";
    //     }
    // }

    public class CommonTableExpression : AstNode
    {
        public string Name { get; set; }
        public AstNode Definition { get; set; }
    }
}
