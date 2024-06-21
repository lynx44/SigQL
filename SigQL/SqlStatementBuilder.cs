using System;
using System.Collections.Generic;
using System.Linq;

namespace SigQL
{
    public class SqlStatementBuilder
    {
        public string Build(AstNode arg)
        {
            return Walk(arg);
        }

        private string Walk(AstNode arg)
        {
            return Walk(new[] {arg}).Trim();
        }

        private string Walk(IEnumerable<AstNode> args)
        {
            var sql = new List<string>();
            if(args != null) {
                foreach (var node in args)
                {
                    if(node != null)
                    switch (node)
                    {
                        case LogicalGrouping a: 
                            sql.Add($"{(a.Args != null ? $"({Walk(a.Args.SingleOrDefault())})" : string.Empty)}".Trim());
                            break;
                        case InPredicate a: 
                            sql.Add($"({Walk(a.LeftComparison)} {a.Keyword} ({string.Join(", ", a.Args?.Select(a => Walk(a)) ?? new string[0])}))".Trim());
                            break;
                        case Exists a: 
                            sql.Add($"exists ({Walk(a.Args.SingleOrDefault())})".Trim());
                            break;
                        case NotExists a: 
                            sql.Add($"not exists ({Walk(a.Args.SingleOrDefault())})".Trim());
                            break;
                        case Alias a: 
                            sql.Add($"{(a.Args != null ? $"{Walk(a.Args.SingleOrDefault())}" : string.Empty)} \"{a.Label}\"".Trim());
                            break;
                        case Function a: 
                            sql.Add($"{a.Name}({string.Join(",", (a.Args?.Select(a => Walk(a))) ?? new string[0])})");
                            break;
                        case TableAliasDefinition a: 
                            sql.Add($"{a.Alias} ({string.Join(",", a.Args.Select(a => Walk(a)))})".Trim());
                            break;
                        case SubqueryAlias a: 
                            sql.Add($"({string.Join(",", a.Args.Select(a => Walk(a)))}) {a.Alias}".Trim());
                            break;
                        case ObjectIdentifier a:
                            sql.Add(string.Join(".", a.Args.Select(a => Walk(a))));
                            break;
                        case DatabaseCatalogObject a:
                            sql.Add($"\"{a.Label}\"");
                            break;
                        case SetEqualOperator a:
                            sql.Add($"{string.Join($" {a.Label} ", a.Args.Select(a => Walk(a)))}");
                            break;
                        case LogicalOperator a:
                            sql.Add($"({string.Join($" {a.Label} ", a.Args.Select(a => Walk(a)))})");
                            break;
                        case NamedParameterIdentifier a:
                            sql.Add($"@{a.Name}");
                            break;
                        case Literal a:
                            sql.Add(a.Value);
                            break;
                        case OrderByIdentifier a:
                            sql.Add($"{Walk(a.Args.Single())} {a.Direction}".Trim());
                            break;
                        case OffsetClause a:
                            sql.Add($"offset {Walk(a.OffsetCount)} rows {Walk(a.Fetch)}".Trim());
                            break;
                        case FetchClause a:
                            sql.Add($"fetch next {Walk(a.FetchCount)} rows only".Trim());
                            break;
                        case OverClause a:
                            sql.Add($"{Walk(a.Function)} over({Walk(a.Args)})".Trim());
                            break;
                        case WhereClause a:
                            sql.Add($"{a.Keyword} {string.Join(" ", a.Args.Select(a => Walk(a)))}".Trim());
                            break;
                        case OrderByClause a:
                            if (a.Args != null && a.Args.Any())
                            {
                                sql.Add($"{a.Keyword} {string.Join(", ", a.Args.Select(a => Walk(a)))} {Walk(a.Offset)}".Trim());
                            }
                            break;
                        case IntoClause a:
                            sql.Add($"into {Walk(a.Object)}({string.Join(", ", a.Args.Select(a => Walk(a)))})".Trim());
                            break;
                        case ValuesList a:
                            sql.Add($"({string.Join(", ", a.Args.Select(a => Walk(a)))})".Trim());
                            break;
                        case ValuesListClause a:
                            sql.Add($"values{string.Join(", ", a.Args.Select(a => Walk(a)))}".Trim());
                            break;
                        case SelectClause a:
                            sql.Add($"{a.Keyword}{a.Modifier?.PadLeft(a.Modifier.Length + 1)} {string.Join(", ", a.Args.Select(a => Walk(a)))}".Trim());
                            break;
                        case OutputClause a:
                            sql.Add($"output {string.Join(", ", a.Args.Select(a => Walk(a)))}{$" {Walk(a.Into)}".TrimEnd()}".Trim());
                            break;
                        case MergeUsing a: 
                            sql.Add($"using ({Walk(a.Values)}){(a.As != null ? $" as {Walk(a.As)}" : string.Empty)}".Trim());
                            break;
                        case WhenNotMatched a: 
                            sql.Add($"when not matched then\n");
                            sql.Add(Walk(a.Insert).Trim());
                            break;
                        case Clause a:
                            sql.Add($"{a.Keyword} {string.Join(", ", a.Args.Select(a => Walk(a)))}".Trim());
                            break;
                        case Join a:
                            sql.Add($"{a.JoinType} join {string.Join(" ",Walk(a.RightNode))} on {string.Join(" ", a.Args.Select(a => Walk(a)))}".Trim());
                            break;
                        case Select a:
                            sql.Add(Walk(new AstNode[] { a.SelectClause }));
                            sql.Add(Walk(new AstNode[] { a.FromClause }));
                            sql.Add(Walk(new AstNode[] { a.WhereClause }));
                            sql.Add(Walk(new AstNode[] { a.GroupByClause }));
                            sql.Add(Walk(new AstNode[] { a.OrderByClause }));
                            break;
                        case Insert a:
                            sql.Add($"insert {Walk(a.Object)}({string.Join(", ", a.ColumnList.Select(a => Walk(a)))}){$" {Walk(a.Args)}".TrimEnd()}{$" {Walk(a.Output)}".TrimEnd()} {string.Join(", ", Walk(a.ValuesList))}".Trim());
                            break;
                        case Update a:
                            sql.Add($"update {string.Join(" ", Walk(a.Args))} set {string.Join(", ", a.SetClause.Select(a => Walk(a)))}".Trim());
                            sql.Add(Walk(new AstNode[] { a.FromClause }));
                            sql.Add(Walk(new AstNode[] { a.WhereClause }));
                            sql.Add(";\n");
                                break;
                        case Delete a:
                            sql.Add($"delete {string.Join(" ", a.Args?.Select(a => Walk(a)) ?? new string[0])}".Trim());
                            sql.Add(Walk(new AstNode[] { a.FromClause }));
                            sql.Add(Walk(new AstNode[] { a.WhereClause }));
                            break;
                        case Merge a:
                            sql.Add($"merge {Walk(a.Table)} {Walk(a.Using)} on {Walk(a.On)}\n");
                            sql.Add((a.WhenNotMatched != null ? Walk(a.WhenNotMatched) : string.Empty).Trim());
                            sql.Add(";");
                            break;
                        case MergeInsert a:
                            sql.Add($"insert ({string.Join(", ", a.ColumnList.Select(a => Walk(a)))}) {string.Join(", ", Walk(a.ValuesList))}{$" {Walk(a.Output)}".TrimEnd()}".Trim());
                            break;
                        case DeclareStatement a:
                            sql.Add($"declare {Walk(a.Parameter)} {Walk(a.DataType)}".Trim());
                            break;
                        case DataType a:
                            sql.Add($"{Walk(a.Type)}{((a.Args?.Any()).GetValueOrDefault(false) ? $"({string.Join(", ", a.Args.Select(a => Walk(a)))})" : null)}".Trim());
                            break;
                        case ColumnDeclaration a:
                            sql.Add($"{string.Join(" ", a.Args.Select(a => Walk(a)))}".Trim());
                            break;
                        case FromClauseNode a:
                            sql.Add($"{string.Join(" ", a.Args.Select(a => Walk(a)))}".Trim());
                            break;
                        case If a:
                            sql.Add($"if {Walk(a.Condition)}");
                            sql.Add($"begin");
                            sql.Add(Walk(a.Args));
                            sql.Add("end");
                            break;
                        case SetParameter a:
                            sql.Add($"set {Walk(a.Parameter)} = {Walk(a.Value)};");
                            break;
                        case Predicate a: 
                            sql.Add($"({Walk(a.Args.Take(1))} {a.Keyword} {Walk(a.Args.Skip(1).Single())})".Trim());
                            break;
                        case Placeholder a:
                            sql.Add(Walk(a.Args.SingleOrDefault())?.Trim());
                            break;
                        case CommonTableExpression a:
                            sql.Add($"; with {a.Name} as (\r\n" +
                                    Walk(a.Definition)?.Trim() +
                                    "\r\n)\r\n" +
                                    Walk(a.Args));
                            break;
                        default:
                            throw new ArgumentException($"Node type {node.GetType()} not supported");
                    }
                }
            }

            return string.Join(" ", sql.Where(v => !String.IsNullOrEmpty(v))).Replace(" ;", ";").Trim();
        }
    }
}
