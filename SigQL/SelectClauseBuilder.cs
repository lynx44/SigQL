﻿using System;
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

        public SelectClauseBuilder(IDatabaseConfiguration databaseConfiguration, IPluralizationHelper pluralizationHelper)
        {
            databaseResolver = new DatabaseResolver(databaseConfiguration, pluralizationHelper);
        }

        internal SelectClauseBuilder(DatabaseResolver databaseResolver)
        {
            this.databaseResolver = databaseResolver;
        }

        public ResolvedSelectClause Build(Type outputType)
        {
            var projectionType = OutputFactory.UnwrapType(outputType);

            var fromClauseRelations = this.databaseResolver.BuildTableRelations(this.databaseResolver.DetectTable(outputType), new TypeArgument(outputType, databaseResolver), TableRelationsColumnSource.ReturnType, new ConcurrentDictionary<string, IEnumerable<string>>());

            return Build(fromClauseRelations, new ConcurrentDictionary<string, IEnumerable<string>>());
        }

        internal ResolvedSelectClause Build(TableRelations projectionTableRelations, ConcurrentDictionary<string, IEnumerable<string>> primaryKeyDefinitions)
        {
            var tableRelations = projectionTableRelations.Mask(TableRelationsColumnSource.ReturnType, ColumnFilters.SelectClause);
            var tablePrimaryKeyDefinitions = primaryKeyDefinitions;

            var selectClauseAst = BuildSelectClause(tableRelations, tablePrimaryKeyDefinitions);
            var result = new ResolvedSelectClause(new SqlStatementBuilder())
            {
                Ast = selectClauseAst,
                TableKeyDefinitions = tablePrimaryKeyDefinitions,
                FromClauseRelations = tableRelations
            };
            return result;
        }

        public ResolvedSelectClause Build<T>()
        {
            return this.Build(typeof(T));
        }

        private SelectClause BuildSelectClause(TableRelations fromClauseRelations,
            ConcurrentDictionary<string, IEnumerable<string>> tablePrimaryKeyDefinitions)
        {
            var columnAliasForeignKeyDefinitions = this.databaseResolver.FindAllForeignKeys(fromClauseRelations)
                .ToColumnAliasForeignKeyDefinitions().ToList();
            var columnDefinitions = databaseResolver.ResolveColumnsForSelectStatement(fromClauseRelations, 
                columnAliasForeignKeyDefinitions, tablePrimaryKeyDefinitions);
            
            var selectClause = new SelectClause()
                .SetArgs(
                    columnDefinitions.Select(column =>
                        new Alias() { Label = column.Alias }
                            .SetArgs(
                                new ColumnIdentifier()
                                    .SetArgs(
                                        new RelationalTable()
                                        {
                                            Label = column.ColumnDefinition.TableAlias
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
        public IDictionary<string, IEnumerable<string>> TableKeyDefinitions { get; internal set; }
        public string AsText => this.statementBuilder.Build(Ast);
        internal TableRelations FromClauseRelations { get; set; }
    }
}
