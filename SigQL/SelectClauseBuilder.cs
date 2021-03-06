using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SigQL.Schema;
using SigQL.Sql;

namespace SigQL
{
    public class SelectClauseBuilder
    {
        private DatabaseResolver databaseResolver;

        public SelectClauseBuilder(IDatabaseConfiguration databaseConfiguration)
        {
            databaseResolver = new DatabaseResolver(databaseConfiguration);
        }

        internal SelectClauseBuilder(DatabaseResolver databaseResolver)
        {
            this.databaseResolver = databaseResolver;
        }

        public ResolvedSelectClause Build(Type outputType)
        {
            var projectionType = OutputFactory.UnwrapType(outputType);

            var fromClauseRelations = this.databaseResolver.BuildTableRelationsFromType(projectionType);

            var tablePrimaryKeyDefinitions = new ConcurrentDictionary<string, ITableKeyDefinition>();

            var selectClauseAst = BuildSelectClause(fromClauseRelations, tablePrimaryKeyDefinitions);
            var result = new ResolvedSelectClause(new SqlStatementBuilder())
            {
                Ast = selectClauseAst,
                TableKeyDefinitions = tablePrimaryKeyDefinitions,
                FromClauseRelations = fromClauseRelations
            };
            return result;
        }

        public ResolvedSelectClause Build<T>()
        {
            return this.Build(typeof(T));
        }

        private SelectClause BuildSelectClause(TableRelations fromClauseRelations,
            ConcurrentDictionary<string, ITableKeyDefinition> tablePrimaryKeyDefinitions)
        {
            var columnAliasForeignKeyDefinitions = this.databaseResolver.FindAllForeignKeys(fromClauseRelations)
                .ToColumnAliasForeignKeyDefinitions().ToList();
            var columnDefinitions = databaseResolver.ResolveColumnsForSelectStatement(fromClauseRelations, new List<string>(),
                columnAliasForeignKeyDefinitions, tablePrimaryKeyDefinitions);
            foreach (var columnDefinition in columnDefinitions)
            {
                columnDefinition.ColumnDefinition.Alias =
                    string.Join(".", columnDefinition.PropertyPath.PropertyPaths.Select(p => p));
            }

            var selectClause = new SelectClause()
                .SetArgs(
                    columnDefinitions.Select(column =>
                        new Alias() { Label = column.ColumnDefinition.Alias }
                            .SetArgs(
                                new ColumnIdentifier()
                                    .SetArgs(
                                        new RelationalTable()
                                        {
                                            Label = column.ColumnDefinition.Table.Name
                                        },
                                        new RelationalColumn()
                                        {
                                            Label = column.ColumnDefinition.Name
                                        }
                                    )
                            )
                    )
                );
            return selectClause;
        }
    }

    public class ResolvedSelectClause
    {
        private readonly SqlStatementBuilder statementBuilder;

        public ResolvedSelectClause(SqlStatementBuilder statementBuilder)
        {
            this.statementBuilder = statementBuilder;
        }
        public SelectClause Ast { get; set; }
        public IDictionary<string, ITableKeyDefinition> TableKeyDefinitions { get; internal set; }
        public string AsText => this.statementBuilder.Build(Ast);
        internal TableRelations FromClauseRelations { get; set; }
    }
}
