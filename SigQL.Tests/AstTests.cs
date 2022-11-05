using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SigQL.Tests
{
    [TestClass]
    public class AstTests
    {
        private SqlStatementBuilder builder = new SqlStatementBuilder();

        [TestMethod]
        public void BasicSelect()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                }
            };

            var statement = Build(selectStatement);
             Assert.AreEqual("select \"name\" from \"person\"", statement);
        }

        [TestMethod]
        public void BasicSelectWithMultipleColumns()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "birthdate"}}}}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                }
            };

            var statement = Build(selectStatement);
             Assert.AreEqual("select \"name\", \"birthdate\" from \"person\"", statement);
        }

        [TestMethod]
        public void BasicSelectWithColumnAlias()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new Alias() { Label = "fullname", Args = new [] { new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}} }} }
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                }
            };

            var statement = Build(selectStatement);
             Assert.AreEqual("select \"name\" \"fullname\" from \"person\"", statement);
        }

        [TestMethod]
        public void BasicSelectWithAggregateFunction()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new Alias() { Label = "fullname", Args = new [] { new Sum() {Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}}}} }} }
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                }
            };

            var statement = Build(selectStatement);
             Assert.AreEqual("select sum(\"name\") \"fullname\" from \"person\"", statement);
        }

        [TestMethod]
        public void BasicSelectWithMultipleFrom()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}, new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "employer" } }}}
                }
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\", \"employer\"", statement);
        }

        [TestMethod]
        public void BasicSelectWithDistinct()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Modifier = "distinct",
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                }
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select distinct \"name\" from \"person\"", statement);
        }

        [TestMethod]
        public void LogicalGrouping_SurroundsWithParenthesis()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause().SetArgs(
                    new LogicalGrouping().SetArgs(
                        new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } } })
                    )
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from (\"person\")", statement);
        }

        [TestMethod]
        public void BasicWhere()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause().SetArgs(
                                new ColumnIdentifier().SetArgs(new RelationalColumn() {Label = "name"})),
                FromClause = new FromClause().SetArgs(
                                new TableIdentifier().SetArgs(new RelationalTable() { Label = "person" })),
                WhereClause = new WhereClause().SetArgs(
                                new EqualsOperator().SetArgs(
                                    new ColumnIdentifier().SetArgs(new RelationalColumn() {Label = "name"}), 
                                    new NamedParameterIdentifier() { Name = "nameToFind" }))
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" where (\"name\" = @nameToFind)", statement);
        }

        [TestMethod]
        public void BasicWhereWithAnd()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                },
                WhereClause = new WhereClause()
                {
                    Args = new AstNode[] {new AndOperator() { Args = new AstNode[] { 
                        new EqualsOperator() { Args = new AstNode[] { new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}}, new NamedParameterIdentifier() { Name = "nameToFind" }} }, 
                        new EqualsOperator() { Args = new AstNode[] { new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "id"}}}, new NamedParameterIdentifier() { Name = "idToFind" }} } }}}
                }
            };
        
            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" where ((\"name\" = @nameToFind) and (\"id\" = @idToFind))", statement);
        }

        [TestMethod]
        public void BasicWhereWithOr()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                },
                WhereClause = new WhereClause()
                {
                    Args = new AstNode[] {new OrOperator() { Args = new AstNode[] { 
                        new EqualsOperator() { Args = new AstNode[] { new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}}, new NamedParameterIdentifier() { Name = "nameToFind" }} }, 
                        new EqualsOperator() { Args = new AstNode[] { new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "id"}}}, new NamedParameterIdentifier() { Name = "idToFind" }} } }}}
                }
            };
        
            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" where ((\"name\" = @nameToFind) or (\"id\" = @idToFind))", statement);
        }

        [TestMethod]
        public void NestedOrWithAnd()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                },
                WhereClause = new WhereClause()
                {
                    Args = new AstNode[] {
                        new AndOperator() { Args = new AstNode[] { 
                            new OrOperator()
                            {
                                Args = new AstNode[]
                                {
                                    new IsOperator()
                                    {
                                        Args = new AstNode[]
                                        {
                                            new NamedParameterIdentifier()
                                            {
                                                Name = "nameToFind"
                                            },
                                            new NullLiteral()
                                        }
                                    }, 
                                    new EqualsOperator()
                                    {
                                        Args = new AstNode[]
                                        {
                                            new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}}, 
                                            new NamedParameterIdentifier() { Name = "nameToFind" }
                                        }
                                    }
                                }
                            },
                            new OrOperator()
                            {
                                Args = new AstNode[]
                                {
                                    new IsOperator()
                                    {
                                        Args = new AstNode[]
                                        {
                                            new NamedParameterIdentifier()
                                            {
                                                Name = "idToFind"
                                            },
                                            new NullLiteral()
                                        }
                                    }, 
                                    new EqualsOperator()
                                    {
                                        Args = new AstNode[]
                                        {
                                            new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "id"}}}, 
                                            new NamedParameterIdentifier() { Name = "idToFind" }
                                        }
                                    }
                                }
                            }
                        }
                    }}
                }
            };
        
            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" where (((@nameToFind is null) or (\"name\" = @nameToFind)) and ((@idToFind is null) or (\"id\" = @idToFind)))", statement);
        }

        [TestMethod]
        public void BasicWhereExists()
        {
            var select1 = new Select();
            select1.SelectClause = new SelectClause().SetArgs(new Literal() { Value = "1" });
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                },
                WhereClause = new WhereClause().SetArgs(new Exists().SetArgs(select1))
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" where exists (select 1)", statement);
        }

        [TestMethod]
        public void BasicWhereNotExists()
        {
            var select1 = new Select();
            select1.SelectClause = new SelectClause().SetArgs(new Literal() { Value = "1" });
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                },
                WhereClause = new WhereClause().SetArgs(new NotExists().SetArgs(select1))
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" where not exists (select 1)", statement);
        }

        [TestMethod]
        public void BasicWhereWithLike()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                },
                WhereClause = new WhereClause()
                {
                    Args = new AstNode[] {new LikePredicate() { Args = new AstNode[] { new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}}, new NamedParameterIdentifier() { Name = "nameToFind" }} }}
                }
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" where (\"name\" like @nameToFind)", statement);
        }

        [TestMethod]
        public void BasicWhereWithNotLike()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                },
                WhereClause = new WhereClause()
                {
                    Args = new AstNode[] {new NotLikePredicate() { Args = new AstNode[] { new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}}, new NamedParameterIdentifier() { Name = "nameToFind" }} }}
                }
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" where (\"name\" not like @nameToFind)", statement);
        }

        [TestMethod]
        public void BasicWhereWithIn()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                },
                WhereClause = new WhereClause().SetArgs(new InPredicate() { LeftComparison = new ColumnIdentifier().SetArgs(new RelationalColumn() {Label = "name"})}.SetArgs(new NamedParameterIdentifier() { Name = "nameToFind1" }, new NamedParameterIdentifier() { Name = "nameToFind2" }))
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" where (\"name\" in (@nameToFind1, @nameToFind2))", statement);
        }

        [TestMethod]
        public void BasicWhereWithNotIn()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] { new ColumnIdentifier() { Args = new[] { new RelationalColumn() { Label = "name" } } }, }
                },
                FromClause = new FromClause()
                {
                    Args = new[] { new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } } } }
                },
                WhereClause = new WhereClause().SetArgs(new NotInPredicate() { LeftComparison = new ColumnIdentifier().SetArgs(new RelationalColumn() { Label = "name" }) }.SetArgs(new NamedParameterIdentifier() { Name = "nameToFind1" }, new NamedParameterIdentifier() { Name = "nameToFind2" }))
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" where (\"name\" not in (@nameToFind1, @nameToFind2))", statement);
        }

        [TestMethod]
        public void BasicWhereWithInSelectStatement()
        {
            var emptySelect = new Select();
            emptySelect.SelectClause = new SelectClause().SetArgs(new Literal() { Value = "1" });
            emptySelect.WhereClause = new WhereClause().SetArgs(
                new EqualsOperator().SetArgs(
                    new Literal() { Value = "0"},
                    new Literal() { Value = "1" }
                )
            );
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                },
                WhereClause = new WhereClause().SetArgs(new InPredicate() { LeftComparison = new ColumnIdentifier().SetArgs(new RelationalColumn() {Label = "name"})}.SetArgs(
                    emptySelect)
                )
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" where (\"name\" in (select 1 where (0 = 1)))", statement);
        }

        [TestMethod]
        public void BasicGreaterThan()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                },
                WhereClause = new WhereClause().SetArgs(new GreaterThanOperator().SetArgs(new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "startDate"}}}, new NamedParameterIdentifier() { Name = "StartDate"}))
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" where (\"startDate\" > @StartDate)", statement);
        }

        [TestMethod]
        public void BasicGreaterThanEqualTo()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                },
                WhereClause = new WhereClause().SetArgs(new GreaterThanOrEqualToOperator().SetArgs(new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "startDate"}}}, new NamedParameterIdentifier() { Name = "StartDate"}))
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" where (\"startDate\" >= @StartDate)", statement);
        }

        [TestMethod]
        public void BasicLessThan()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                },
                WhereClause = new WhereClause().SetArgs(new LessThanOperator().SetArgs(new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "startDate"}}}, new NamedParameterIdentifier() { Name = "StartDate"}))
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" where (\"startDate\" < @StartDate)", statement);
        }

        [TestMethod]
        public void BasicLessThanOrEqualTo()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                },
                WhereClause = new WhereClause().SetArgs(new LessThanOrEqualToOperator().SetArgs(new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "startDate"}}}, new NamedParameterIdentifier() { Name = "StartDate"}))
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" where (\"startDate\" <= @StartDate)", statement);
        }

        [TestMethod]
        public void Placeholder()
        {
            var placeholder = new Placeholder();
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause().SetArgs(
                    new ColumnIdentifier().SetArgs(new RelationalColumn() {Label = "name"})),
                FromClause = new FromClause().SetArgs(
                    new TableIdentifier().SetArgs(new RelationalTable() { Label = "person" })),
                WhereClause = new WhereClause().SetArgs(
                    placeholder.SetArgs(
                        new EqualsOperator().SetArgs(
                            new ColumnIdentifier().SetArgs(new RelationalColumn() {Label = "name"}), 
                            new NamedParameterIdentifier() { Name = "nameToFind" })))
            };

            var statement1 = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" where (\"name\" = @nameToFind)", statement1);

            placeholder.Args = new AstNode[0];
            
            var statement2 = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" where", statement2);
        }

        [TestMethod]
        public void BasicOrderBy()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                },
                OrderByClause = new OrderByClause()
                {
                    Args = new[] {new OrderByIdentifier() { Args = new [] { new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}} } }}
                }
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" order by \"name\"", statement);
        }

        [TestMethod]
        public void BasicOrderByDesc()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                },
                OrderByClause = new OrderByClause()
                {
                    Args = new[] {new OrderByIdentifier() { Direction = "desc", Args = new [] { new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}} } }}
                }
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" order by \"name\" desc", statement);
        }

        [TestMethod]
        public void BasicOrderBy_Multiple()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] { new ColumnIdentifier() { Args = new[] { new RelationalColumn() { Label = "name" } } }, }
                },
                FromClause = new FromClause()
                {
                    Args = new[] { new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } } } }
                },
                OrderByClause = new OrderByClause()
                {
                    Args = new[]
                    {
                        new OrderByIdentifier() { Args = new[] { new ColumnIdentifier() { Args = new[] { new RelationalColumn() { Label = "name" } } } } },
                        new OrderByIdentifier() { Args = new[] { new ColumnIdentifier() { Args = new[] { new RelationalColumn() { Label = "id" } } } } }
                    }
                }
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" order by \"name\", \"id\"", statement);
        }

        [TestMethod]
        public void BasicOrderByWithOffset()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                },
                OrderByClause = new OrderByClause()
                {
                    Args = new[] {new OrderByIdentifier() { Args = new [] { new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}} } }},
                    Offset = new OffsetClause()
                    {
                        OffsetCount = new Literal() { Value = "10" }
                    }
                }
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" order by \"name\" offset 10 rows", statement);
        }

        [TestMethod]
        public void BasicOrderByWithFetch()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                },
                OrderByClause = new OrderByClause()
                {
                    Args = new[] {new OrderByIdentifier() { Args = new [] { new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}} } }},
                    Offset = new OffsetClause()
                    {
                        OffsetCount = new Literal() { Value = "10" },
                        Fetch = new FetchClause()
                        {
                            FetchCount = new Literal() { Value = "5" }
                        }
                    }
                }
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" order by \"name\" offset 10 rows fetch next 5 rows only", statement);
        }
        
        [TestMethod]
        public void BasicOver()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause().SetArgs(new OverClause()
                {
                    Function = new Function()
                    {
                        Name = "ROW_NUMBER"
                    }
                }.SetArgs(
                    new OrderByClause().SetArgs(
                        new ColumnIdentifier().SetArgs(
                            new RelationalTable()
                            {
                                Label = "table"
                            },
                            new RelationalColumn()
                            {
                                Label = "column"
                            }))))
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select ROW_NUMBER() over(order by \"table\".\"column\")", statement);
        }
        
        [TestMethod]
        public void OverWithPartition()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause().SetArgs(new OverClause()
                {
                    Function = new Function()
                    {
                        Name = "ROW_NUMBER"
                    }
                }.SetArgs(
                    new PartitionByClause().SetArgs(
                        new ColumnIdentifier().SetArgs(
                            new RelationalTable()
                            {
                                Label = "p1"
                            },
                            new RelationalColumn()
                            {
                                Label = "col"
                            })),
                    new OrderByClause().SetArgs(
                        new ColumnIdentifier().SetArgs(
                            new RelationalTable()
                            {
                                Label = "table"
                            },
                            new RelationalColumn()
                            {
                                Label = "column"
                            }))))
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select ROW_NUMBER() over(partition by \"p1\".\"col\" order by \"table\".\"column\")", statement);
        }

        [TestMethod]
        public void BasicGroupBy()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new AstNode[] {
                        new ColumnIdentifier() {Args = new AstNode[] {new RelationalTable() {Label = "employer"}, new RelationalColumn() {Label = "id"}}},
                        new Count() { Args = new AstNode[] {new ColumnIdentifier() { Args = new AstNode[] {new RelationalTable() {Label = "person"}, new RelationalColumn() {Label = "id"}} }}}
                    }
                },
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}, new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "employer" } }}}
                },
                GroupByClause = new GroupByClause()
                {
                    Args = new[] { new ColumnIdentifier() {Args = new AstNode[] {new RelationalTable() {Label = "employer"}, new RelationalColumn() {Label = "id"}}} }
                }
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"employer\".\"id\", count(\"person\".\"id\") from \"person\", \"employer\" group by \"employer\".\"id\"", statement);
        }

        [TestMethod]
        public void BasicInnerJoin()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new AstNode[]
                    {
                        new FromClauseNode()
                        {
                            Args = new AstNode[]
                            {
                                new TableIdentifier()
                                {
                                    Args = new AstNode[]
                                    {
                                        new RelationalTable() { Label = "person" }
                                    }
                                },
                                new InnerJoin() {
                                    RightNode =
                                        new TableIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "employee"}}},
                                    Args = new AstNode[]
                                    {
                                        new EqualsOperator() { Args = new AstNode[]
                                        {
                                            new ColumnIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "employee" }, new RelationalColumn() { Label = "personid" } }},
                                            new ColumnIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "person" }, new RelationalColumn() { Label = "id" } }}
                                        }}
                                    }

                                },
                            }
                        }, 
                       
                    }
                }
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" inner join \"employee\" on (\"employee\".\"personid\" = \"person\".\"id\")", statement);
        }

        [TestMethod]
        public void MultiInnerJoin()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new AstNode[]
                    {
                        new FromClauseNode()
                        {
                            Args = new AstNode[]
                            {
                                new TableIdentifier()
                                {
                                    Args = new AstNode[]
                                    {
                                        new RelationalTable() { Label = "person" }
                                    }
                                },
                                new InnerJoin() {
                                    RightNode =
                                        new TableIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "employee"}}},
                                    Args = new AstNode[]
                                    {
                                        new EqualsOperator() { Args = new AstNode[]
                                        {
                                            new ColumnIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "employee" }, new RelationalColumn() { Label = "personid" } }},
                                            new ColumnIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "person" }, new RelationalColumn() { Label = "id" } }}
                                        }}
                                    }

                                },
                                new InnerJoin() {
                                    RightNode =
                                        new TableIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "student"}}},
                                    Args = new AstNode[]
                                    {
                                        new EqualsOperator() { Args = new AstNode[]
                                        {
                                            new ColumnIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "student" }, new RelationalColumn() { Label = "personid" } }},
                                            new ColumnIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "person" }, new RelationalColumn() { Label = "id" } }}
                                        }}
                                    }

                                },
                            }
                        }, 
                       
                    }
                }
            };
        
            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" inner join \"employee\" on (\"employee\".\"personid\" = \"person\".\"id\") inner join \"student\" on (\"student\".\"personid\" = \"person\".\"id\")", statement);
        }

        [TestMethod]
        public void MultiLeftOuterJoin()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] {new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}},}
                },
                FromClause = new FromClause()
                {
                    Args = new AstNode[]
                    {
                        new FromClauseNode()
                        {
                            Args = new AstNode[]
                            {
                                new TableIdentifier()
                                {
                                    Args = new AstNode[]
                                    {
                                        new RelationalTable() { Label = "person" }
                                    }
                                },
                                new LeftOuterJoin() {
                                    RightNode =
                                        new TableIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "employee"}}},
                                    Args = new AstNode[]
                                    {
                                        new EqualsOperator() { Args = new AstNode[]
                                        {
                                            new ColumnIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "employee" }, new RelationalColumn() { Label = "personid" } }},
                                            new ColumnIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "person" }, new RelationalColumn() { Label = "id" } }}
                                        }}
                                    }

                                },
                                new LeftOuterJoin() {
                                    RightNode =
                                        new TableIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "student"}}},
                                    Args = new AstNode[]
                                    {
                                        new EqualsOperator() { Args = new AstNode[]
                                        {
                                            new ColumnIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "student" }, new RelationalColumn() { Label = "personid" } }},
                                            new ColumnIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "person" }, new RelationalColumn() { Label = "id" } }}
                                        }}
                                    }

                                },
                            }
                        }, 
                       
                    }
                }
            };
        
            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" left outer join \"employee\" on (\"employee\".\"personid\" = \"person\".\"id\") left outer join \"student\" on (\"student\".\"personid\" = \"person\".\"id\")", statement);
        }

        [TestMethod]
        public void SubqueryAlias()
        {
            var countStatement = new Select();
            countStatement.SelectClause = new SelectClause();
            countStatement.SelectClause.SetArgs(
                new Alias() { Label = "Count" }.SetArgs(new Count().SetArgs(new Literal() { Value = "1" })));
            countStatement.FromClause = new FromClause();
            var baseSelectStatement = new Select();
            baseSelectStatement.SelectClause = new SelectClause();
            baseSelectStatement.SelectClause.SetArgs(new ColumnIdentifier().SetArgs(new RelationalColumn() { Label = "id" }));
            baseSelectStatement.FromClause = new FromClause();
            baseSelectStatement.FromClause.SetArgs(new TableIdentifier().SetArgs(new RelationalTable() { Label = "WorkLog" }));
            countStatement.FromClause.SetArgs(new SubqueryAlias() { Alias = "Subquery" }.SetArgs(baseSelectStatement));

            var statement = Build(countStatement);
            Assert.AreEqual("select count(1) \"Count\" from (select \"id\" from \"WorkLog\") Subquery", statement);
        }

        [TestMethod]
        public void JoinWithMultipleConditions()
        {
            var selectStatement = new Select()
            {
                SelectClause = new SelectClause()
                {
                    Args = new[] { new ColumnIdentifier() { Args = new[] { new RelationalColumn() { Label = "name" } } }, }
                },
                FromClause = new FromClause()
                {
                    Args = new AstNode[]
                   {
                        new FromClauseNode()
                        {
                            Args = new AstNode[]
                            {
                                new TableIdentifier()
                                {
                                    Args = new AstNode[]
                                    {
                                        new RelationalTable() { Label = "person" }
                                    }
                                },
                                new InnerJoin() {
                                    RightNode =
                                        new TableIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "employee"}}}
                                }.SetArgs(new AndOperator().SetArgs(new AstNode[]
                                {
                                    new EqualsOperator() { Args = new AstNode[]
                                    {
                                        new ColumnIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "employee" }, new RelationalColumn() { Label = "personid" } }},
                                        new ColumnIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "person" }, new RelationalColumn() { Label = "id" } }}
                                    }},
                                    new EqualsOperator() { Args = new AstNode[]
                                    {
                                        new ColumnIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "employee" }, new RelationalColumn() { Label = "employeeid" } }},
                                        new ColumnIdentifier() { Args = new AstNode[] { new RelationalTable() { Label = "person" }, new RelationalColumn() { Label = "employeeid" } }}
                                    }}
                                }))
                            }
                        },

                   }
                }
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("select \"name\" from \"person\" inner join \"employee\" on ((\"employee\".\"personid\" = \"person\".\"id\") and (\"employee\".\"employeeid\" = \"person\".\"employeeid\"))", statement);
        }

        [TestMethod]
        public void BasicDelete()
        {
            var selectStatement = new Delete()
            {
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                }
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("delete from \"person\"", statement);
        }

        [TestMethod]
        public void BasicDeleteWithAlias()
        {
            var selectStatement = new Delete()
            {
                FromClause = new FromClause()
                {
                    Args = new [] {
                        new FromClauseNode() {
                            Args = new AstNode[] {
                                new Alias() { 
                                    Label = "p", 
                                    Args = new AstNode[]
                                    {
                                        new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}
                                    }
                                }
                            }
                        }
                    }
                },
                Args = new AstNode[]
                {
                    new Alias()
                    {
                        Label = "p"
                    }
                }
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("delete \"p\" from \"person\" \"p\"", statement);
        }

        [TestMethod]
        public void BasicDeleteWhere()
        {
            var selectStatement = new Delete()
            {
                FromClause = new FromClause()
                {
                    Args = new [] {new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}}
                },
                WhereClause = new WhereClause()
                {
                    Args = new AstNode[] {new EqualsOperator() { Args = new AstNode[] { new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}}, new NamedParameterIdentifier() { Name = "nameToFind" }} }}
                }
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("delete from \"person\" where (\"name\" = @nameToFind)", statement);
        }

        [TestMethod]
        public void BasicUpdate()
        {
            var selectStatement = new Update()
            {
                Args = new AstNode[]
                {
                    new TableIdentifier()
                    {
                        Args = new AstNode[]
                        {
                            new RelationalTable()
                            {
                                Label = "person"
                            }
                        }   
                    }
                },
                SetClause = new []
                {
                    new SetEqualOperator()
                    {
                        Args = new AstNode[]
                        {
                            new ColumnIdentifier()
                            {
                               Args = new AstNode[]
                               {
                                   new RelationalColumn()
                                   {
                                       Label = "name"
                                   }
                               }
                            },
                            new NamedParameterIdentifier()
                            {
                                Name = "newName"
                            }
                        }
                    }
                }
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("update \"person\" set \"name\" = @newName;", statement);
        }

        [TestMethod]
        public void BasicUpdateWithWhere()
        {
            var selectStatement = new Update()
            {
                Args = new AstNode[]
                {
                    new TableIdentifier()
                    {
                        Args = new AstNode[]
                        {
                            new RelationalTable()
                            {
                                Label = "person"
                            }
                        }   
                    }
                },
                SetClause = new []
                {
                    new SetEqualOperator()
                    {
                        Args = new AstNode[]
                        {
                            new ColumnIdentifier()
                            {
                               Args = new AstNode[]
                               {
                                   new RelationalColumn()
                                   {
                                       Label = "name"
                                   }
                               }
                            },
                            new NamedParameterIdentifier()
                            {
                                Name = "newName"
                            }
                        }
                    }
                },
                WhereClause = new WhereClause()
                {
                    Args = new AstNode[] {new EqualsOperator() { Args = new AstNode[] { new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}}, new NamedParameterIdentifier() { Name = "nameToFind" }} }}
                }
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("update \"person\" set \"name\" = @newName where (\"name\" = @nameToFind);", statement);
        }

        [TestMethod]
        public void UpdateWithAlias()
        {
            var selectStatement = new Update()
            {
                Args = new AstNode[]
                {
                    new Alias()
                    {
                        Label = "p"
                    }
                },
                SetClause = new []
                {
                    new SetEqualOperator()
                    {
                        Args = new AstNode[]
                        {
                            new ColumnIdentifier()
                            {
                               Args = new AstNode[]
                               {
                                   new RelationalColumn()
                                   {
                                       Label = "name"
                                   }
                               }
                            },
                            new NamedParameterIdentifier()
                            {
                                Name = "newName"
                            }
                        }
                    }
                },
                WhereClause = new WhereClause()
                {
                    Args = new AstNode[] {new EqualsOperator() { Args = new AstNode[] { new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "name"}}}, new NamedParameterIdentifier() { Name = "nameToFind" }} }}
                },
                FromClause = new FromClause()
                {
                    Args = new [] {
                        new FromClauseNode() {
                            Args = new AstNode[] {
                                new Alias() { 
                                    Label = "p", 
                                    Args = new AstNode[]
                                    {
                                        new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }}
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var statement = Build(selectStatement);
            Assert.AreEqual("update \"p\" set \"name\" = @newName from \"person\" \"p\" where (\"name\" = @nameToFind);", statement);
        }

        [TestMethod]
        public void UpdateQuery()
        {
            var ast = new Update()
            {
                SetClause = new[]
                {
                    new SetEqualOperator()
                        .SetArgs(
                            new ColumnIdentifier().SetArgs(
                                new RelationalColumn()
                                {
                                    Label = "name"
                                }),
                            new ColumnIdentifier().SetArgs(
                                new RelationalTable()
                                {
                                    Label = "student"
                                },
                                new RelationalColumn()
                                {
                                    Label = "firstname"
                                })
                        )
                },
                FromClause = new FromClause().SetArgs(
                        new FromClauseNode().SetArgs(
                                new Alias()
                                {
                                    Label = "p"
                                }.SetArgs(
                                    new TableIdentifier().SetArgs(
                                            new RelationalTable() { Label = "person" }
                                        )),
                                new InnerJoin()
                                {
                                    RightNode = new TableIdentifier()
                                        .SetArgs(new RelationalTable()
                                        {
                                            Label = "student"
                                        })
                                }.SetArgs(new EqualsOperator().SetArgs(
                                    new ColumnIdentifier().SetArgs(
                                        new RelationalTable()
                                        {
                                            Label = "student"
                                        },
                                        new RelationalColumn()
                                        {
                                            Label = "personid"
                                        }),
                                    new ColumnIdentifier().SetArgs(
                                        new RelationalTable()
                                        {
                                            Label = "p"
                                        },
                                        new RelationalColumn()
                                        {
                                            Label = "id"
                                        })))
                         )
                 )
            }.SetArgs(new Alias()
            {
                Label = "p"
            });

            var statement = Build(ast);
            Assert.AreEqual("update \"p\" set \"name\" = \"student\".\"firstname\" from \"person\" \"p\" inner join \"student\" on (\"student\".\"personid\" = \"p\".\"id\");", statement);
        }

        [TestMethod]
        public void BasicInsert()
        {
            var insertStatement = new Insert()
            {
                Object = new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }},
                ColumnList = new []
                {
                    new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "Name"}}},
                    new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "Birthdate"}}},
                },
                ValuesList = new ValuesListClause().SetArgs(
                    new ValuesList().SetArgs(
                        new NamedParameterIdentifier()
                        {
                            Name = "name"
                        },
                        new NamedParameterIdentifier()
                        {
                            Name = "birthdate"
                        })
                )
            };

            var statement = Build(insertStatement);
            Assert.AreEqual("insert \"person\"(\"Name\", \"Birthdate\") values(@name, @birthdate)", statement);
        }

        [TestMethod]
        public void InsertWithMultipleRows()
        {
            var insertStatement = new Insert()
            {
                
                Object = new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }},
                ColumnList = new [] {
                    new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "Name"}}},
                    new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "Birthdate"}}}
                },
                ValuesList = new ValuesListClause().SetArgs(
                    new ValuesList().SetArgs(
                        new NamedParameterIdentifier()
                        {
                            Name = "name1"
                        },
                        new NamedParameterIdentifier()
                        {
                            Name = "birthdate1"
                        }),
                    new ValuesList().SetArgs(
                        new NamedParameterIdentifier()
                        {
                            Name = "name2"
                        },
                        new NamedParameterIdentifier()
                        {
                            Name = "birthdate2"
                        })
                    )
            };

            var statement = Build(insertStatement);
            Assert.AreEqual("insert \"person\"(\"Name\", \"Birthdate\") values(@name1, @birthdate1), (@name2, @birthdate2)", statement);
        }

        [TestMethod]
        public void BasicInsertWithOutputClause()
        {
            var insertStatement = new Insert()
            {
                
                Object = new TableIdentifier() { Args = new[] { new RelationalTable() { Label = "person" } }},
                ColumnList = new [] {
                    new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "Name"}}},
                    new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "Birthdate"}}}
                },
                Output = new OutputClause()
                {
                    Into = new IntoClause() { Object = new NamedParameterIdentifier() { Name = "results" } }.SetArgs(
                        new ColumnIdentifier().SetArgs(new RelationalColumn() { Label = "id" }), 
                        new ColumnIdentifier().SetArgs(new RelationalColumn() { Label = "name" }))
                }.SetArgs(
                    new ColumnIdentifier().SetArgs(new RelationalTable() { Label = "inserted" }, new RelationalColumn() { Label = "id" }), 
                    new ColumnIdentifier().SetArgs(new RelationalTable() { Label = "inserted" }, new RelationalColumn() { Label = "name" })),
                ValuesList = new ValuesListClause().SetArgs(
                    new ValuesList().SetArgs(
                        new NamedParameterIdentifier()
                        {
                            Name = "name"
                        },
                        new NamedParameterIdentifier()
                        {
                            Name = "birthdate"
                        }
                    )
                )
            };
        
            var statement = Build(insertStatement);
            Assert.AreEqual("insert \"person\"(\"Name\", \"Birthdate\") output \"inserted\".\"id\", \"inserted\".\"name\" into @results(\"id\", \"name\") values(@name, @birthdate)", statement);
        }

        [TestMethod]
        public void DeclareTableParameter()
        {
            var declareStatement = new DeclareStatement()
            {
                Parameter = new NamedParameterIdentifier() { Name = "results" },
                DataType = new DataType() { Type = new Literal() { Value = "table" } }
                    .SetArgs(
                        new ColumnDeclaration().SetArgs(
                            new Literal() { Value = "id" },
                            new DataType() { Type = new Literal() { Value = "int" } }
                        )
                    )
            };
            var statement = Build(declareStatement);
            Assert.AreEqual("declare @results table(id int)", statement);
        }

        [TestMethod]
        public void Merge_WhenNotMatched()
        {
            var ast = new Merge()
            {
                Table = new TableIdentifier().SetArgs(new RelationalTable() {Label = "Employee"}),
                Using = new MergeUsing()
                {
                    Values = new ValuesListClause().SetArgs(
                        new ValuesList().SetArgs(
                            new NamedParameterIdentifier() { Name = "Name0" },
                            new Literal() { Value = "0" }
                        ),
                        new ValuesList().SetArgs(
                            new NamedParameterIdentifier() { Name = "Name1" },
                            new Literal() { Value = "1" }
                        )
                    ),
                    As = new TableAliasDefinition() { Alias = "i"}
                        .SetArgs(
                            new ColumnDeclaration().SetArgs(
                                new RelationalColumn() { Label = "Name" }
                            ),
                            new ColumnDeclaration().SetArgs(
                                new RelationalColumn() { Label = "_index" }
                            )
                        )
                },
                On = new EqualsOperator().SetArgs(
                    new Literal() { Value = "1" },
                    new Literal() { Value = "0" }
                ),
                WhenNotMatched = new WhenNotMatched()
                {
                    Insert = new MergeInsert()
                    {
                        ColumnList = new [] {
                            new ColumnIdentifier() {Args = new[] {new RelationalColumn() {Label = "Name"}}}
                        },
                        Output = new OutputClause()
                        {
                            Into = new IntoClause() { Object = new NamedParameterIdentifier() { Name = "insertedEmployee" } }.SetArgs(
                                new ColumnIdentifier().SetArgs(new RelationalColumn() { Label = "id" }), 
                                new ColumnIdentifier().SetArgs(new RelationalColumn() { Label = "_index" }))
                        }.SetArgs(
                            new ColumnIdentifier().SetArgs(new RelationalTable() { Label = "inserted" }, new RelationalColumn() { Label = "id" }), 
                            new ColumnIdentifier().SetArgs(new RelationalTable() { Label = "i" }, new RelationalColumn() { Label = "_index" })),
                        ValuesList = new ValuesListClause().SetArgs(
                            new ValuesList().SetArgs(
                                new ColumnIdentifier().SetArgs(new RelationalTable() { Label = "i" }, new RelationalColumn() { Label = "Name" }))
                        )
                    }
                }
            };

            var sql = Build(ast);
            Assert.AreEqual("merge \"Employee\" using (values(@Name0, 0), (@Name1, 1)) as i (\"Name\",\"_index\") on (1 = 0)\n" +
                            " when not matched then\n" +
                            " insert (\"Name\") values(\"i\".\"Name\") output \"inserted\".\"id\", \"i\".\"_index\" into @insertedEmployee(\"id\", \"_index\");", sql);
        }

        private string Build(AstNode arg)
        {
            return builder.Build(arg);
        }
    }
}
