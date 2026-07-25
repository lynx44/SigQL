using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.Exceptions;
using SigQL.Schema;
using SigQL.Tests.Common.Databases.Labor;
using SigQL.Tests.Infrastructure;

namespace SigQL.Tests
{
    [TestClass]
    public class ForeignKeyResolverTests
    {
        private DatabaseConfiguration workLogDatabaseConfiguration;

        [TestInitialize]
        public void Setup()
        {
            workLogDatabaseConfiguration = new MockWorkLogDatabaseConfigurator().WorkLogDatabaseConfiguration;
        }

        [TestMethod]
        public void DefaultForeignKeyResolver_ReturnsTablesOwnForeignKeyCollection()
        {
            var workLogTable = workLogDatabaseConfiguration.Tables.FindByName(nameof(WorkLog));

            var resolvedForeignKeys = DefaultForeignKeyResolver.Instance.GetForeignKeys(workLogTable);

            Assert.AreSame(workLogTable.ForeignKeyCollection, resolvedForeignKeys);
        }

        [TestMethod]
        public void WithoutForeignKeyResolver_TableMissingSchemaForeignKeys_ThrowsInvalidIdentifierException()
        {
            RemoveAllSchemaForeignKeys(workLogDatabaseConfiguration);

            var methodParser = new MethodParser(new SqlStatementBuilder(), workLogDatabaseConfiguration, DefaultPluralizationHelper.Instance);
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogWithEmployee));

            Assert.ThrowsException<InvalidIdentifierException>(() => methodParser.SqlFor(methodInfo));
        }

        [TestMethod]
        public void CustomForeignKeyResolver_ManyToOneRelationship_ResolvesJoinWithoutSchemaForeignKeys()
        {
            var resolver = new LookupForeignKeyResolver(CaptureAndRemoveAllSchemaForeignKeys(workLogDatabaseConfiguration));

            var sql = SqlFor(resolver, nameof(IMonolithicRepository.GetWorkLogWithEmployee));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"Employee.Id\", \"Employee\".\"Name\" \"Employee.Name\" from \"WorkLog\" left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")", sql);
        }

        [TestMethod]
        public void CustomForeignKeyResolver_ManyToManyRelationship_ResolvesJoinWithoutSchemaForeignKeys()
        {
            var resolver = new LookupForeignKeyResolver(CaptureAndRemoveAllSchemaForeignKeys(workLogDatabaseConfiguration));

            var sql = SqlFor(resolver, nameof(IMonolithicRepository.GetEmployeeWithAddresses));

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Address\".\"Id\" \"Addresses.Id\", \"Address\".\"StreetAddress\" \"Addresses.StreetAddress\" from \"Employee\" left outer join \"EmployeeAddress\" on (\"EmployeeAddress\".\"EmployeeId\" = \"Employee\".\"Id\") left outer join \"Address\" on (\"EmployeeAddress\".\"AddressId\" = \"Address\".\"Id\")", sql);
        }

        [TestMethod]
        public void ConventionBasedForeignKeyResolver_ManyToOneRelationship_ResolvesJoinByColumnNameWithoutSchemaForeignKeys()
        {
            RemoveAllSchemaForeignKeys(workLogDatabaseConfiguration);
            var resolver = new ConventionForeignKeyResolver(workLogDatabaseConfiguration);

            var sql = SqlFor(resolver, nameof(IMonolithicRepository.GetWorkLogWithEmployee));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"Employee.Id\", \"Employee\".\"Name\" \"Employee.Name\" from \"WorkLog\" left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")", sql);
        }

        [TestMethod]
        public void ConventionBasedForeignKeyResolver_ManyToManyRelationship_ResolvesJoinByColumnNameWithoutSchemaForeignKeys()
        {
            RemoveAllSchemaForeignKeys(workLogDatabaseConfiguration);
            var resolver = new ConventionForeignKeyResolver(workLogDatabaseConfiguration);

            var sql = SqlFor(resolver, nameof(IMonolithicRepository.GetEmployeeWithAddresses));

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Address\".\"Id\" \"Addresses.Id\", \"Address\".\"StreetAddress\" \"Addresses.StreetAddress\" from \"Employee\" left outer join \"EmployeeAddress\" on (\"EmployeeAddress\".\"EmployeeId\" = \"Employee\".\"Id\") left outer join \"Address\" on (\"EmployeeAddress\".\"AddressId\" = \"Address\".\"Id\")", sql);
        }

        private string SqlFor(IForeignKeyResolver resolver, string methodName)
        {
            var methodParser = new MethodParser(new SqlStatementBuilder(), workLogDatabaseConfiguration, DefaultPluralizationHelper.Instance, resolver);
            var methodInfo = typeof(IMonolithicRepository).GetMethod(methodName);
            return methodParser.SqlFor(methodInfo).GetPreparedStatement(new ParameterArg[0]).CommandText;
        }

        private static void RemoveAllSchemaForeignKeys(IDatabaseConfiguration databaseConfiguration)
        {
            foreach (var table in databaseConfiguration.Tables)
            {
                ((TableDefinition) table).ForeignKeyCollection = new ForeignKeyDefinitionCollection();
            }
        }

        private static IDictionary<string, IForeignKeyDefinitionCollection> CaptureAndRemoveAllSchemaForeignKeys(IDatabaseConfiguration databaseConfiguration)
        {
            var foreignKeysByTable = databaseConfiguration.Tables.ToDictionary(t => t.Name, t => t.ForeignKeyCollection);
            RemoveAllSchemaForeignKeys(databaseConfiguration);
            return foreignKeysByTable;
        }

        /// <summary>
        /// Simulates relationships defined entirely in code (e.g. loaded from an external mapping),
        /// independent of whatever foreign keys (if any) exist on the table definitions themselves.
        /// </summary>
        private class LookupForeignKeyResolver : IForeignKeyResolver
        {
            private readonly IDictionary<string, IForeignKeyDefinitionCollection> foreignKeysByTable;

            public LookupForeignKeyResolver(IDictionary<string, IForeignKeyDefinitionCollection> foreignKeysByTable)
            {
                this.foreignKeysByTable = foreignKeysByTable;
            }

            public IForeignKeyDefinitionCollection GetForeignKeys(ITableDefinition table)
            {
                return this.foreignKeysByTable.TryGetValue(table.Name, out var foreignKeys) ? foreignKeys : new ForeignKeyDefinitionCollection();
            }
        }

    }
}
