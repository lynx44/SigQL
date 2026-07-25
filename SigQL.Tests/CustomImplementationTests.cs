using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.Exceptions;
using SigQL.Schema;
using SigQL.Tests.Common.Databases.Labor;
using SigQL.Tests.Infrastructure;
using SigQL.Types.Attributes;

namespace SigQL.Tests
{
    /// <summary>
    /// Repository members that supply their own implementation rather than having SQL generated:
    /// default interface methods, virtual methods on abstract repository classes, and [Inject]
    /// members.
    /// </summary>
    [TestClass]
    public class CustomImplementationTests
    {
        private DatabaseConfiguration databaseConfiguration;
        private StubQueryMaterializer materializer;
        private List<Type> resolvedServiceTypes;

        [TestInitialize]
        public void Setup()
        {
            databaseConfiguration = new MockWorkLogDatabaseConfigurator().WorkLogDatabaseConfiguration;
            materializer = new StubQueryMaterializer();
            resolvedServiceTypes = new List<Type>();
        }

        private RepositoryBuilder BuildRepositoryBuilder(Func<Type, object> serviceResolver = null)
        {
            var options = new RepositoryBuilderOptions()
            {
                ServiceResolver = serviceResolver
            };

            return new RepositoryBuilder(new StubQueryExecutor(), databaseConfiguration, materializer, options);
        }

        private Func<Type, object> SummarizerResolver()
        {
            return t =>
            {
                resolvedServiceTypes.Add(t);
                return t == typeof(IWorkLogSummarizer) ? new WorkLogSummarizer() : null;
            };
        }

        [TestMethod]
        public void AbstractInterfaceMethod_StillGeneratesSql()
        {
            var repository = BuildRepositoryBuilder().Build<ICustomImplementationRepository>();

            materializer.Result = new List<WorkLog.IWorkLogId>() { new WorkLog() { Id = 1 } };

            var result = repository.GetAllIds().ToList();

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1, materializer.InvocationCount);
        }

        [TestMethod]
        public void DefaultInterfaceMethod_RunsItsOwnBody()
        {
            var repository = BuildRepositoryBuilder().Build<ICustomImplementationRepository>();

            materializer.Result = new List<WorkLog.IWorkLogId>()
                { new WorkLog() { Id = 1 }, new WorkLog() { Id = 2 } };

            Assert.AreEqual(2, repository.CountAllIds());
        }

        [TestMethod]
        public void DefaultInterfaceMethod_ComposesGeneratedMethods()
        {
            var repository = BuildRepositoryBuilder().Build<ICustomImplementationRepository>();

            materializer.Result = new List<WorkLog.IWorkLogId>() { new WorkLog() { Id = 1 } };
            repository.CountAllIds();

            // the call the custom body made to GetAllIds still routed through SigQL
            Assert.AreEqual(1, materializer.InvocationCount);
        }

        [TestMethod]
        public void InjectedParameter_IsSuppliedWhenCallerOmitsIt()
        {
            var repository = BuildRepositoryBuilder(SummarizerResolver()).Build<ICustomImplementationRepository>();

            materializer.Result = new List<WorkLog.IWorkLogId>()
                { new WorkLog() { Id = 1 }, new WorkLog() { Id = 2 } };

            Assert.AreEqual("summarized 2", repository.SummarizeAll());
            CollectionAssert.Contains(resolvedServiceTypes, typeof(IWorkLogSummarizer));
        }

        [TestMethod]
        public void InjectedParameter_IsOverriddenWhenCallerSuppliesIt()
        {
            var repository = BuildRepositoryBuilder(SummarizerResolver()).Build<ICustomImplementationRepository>();

            materializer.Result = new List<WorkLog.IWorkLogId>() { new WorkLog() { Id = 1 } };

            // an explicitly passed service is a test seam, but the resolver still wins at runtime
            Assert.AreEqual("summarized 1", repository.SummarizeAll(new WorkLogSummarizer()));
        }

        [TestMethod]
        public void InjectedParameter_CoexistsWithOrdinaryParameters()
        {
            var repository = BuildRepositoryBuilder(SummarizerResolver()).Build<ICustomImplementationRepository>();

            materializer.Result = new WorkLog() { Id = 7 };

            Assert.AreEqual("summarized 1", repository.SummarizeOne(7));
            Assert.AreEqual(7, materializer.LastArguments.Single().Value);
        }

        [TestMethod]
        public void InjectedProperty_ReturnsResolvedService()
        {
            var repository = BuildRepositoryBuilder(SummarizerResolver()).Build<ICustomImplementationRepository>();

            Assert.IsInstanceOfType(repository.Summarizer, typeof(WorkLogSummarizer));
        }

        [TestMethod]
        public void InjectedProperty_IsUsableFromACustomBody()
        {
            var repository = BuildRepositoryBuilder(SummarizerResolver()).Build<ICustomImplementationRepository>();

            materializer.Result = new List<WorkLog.IWorkLogId>()
                { new WorkLog() { Id = 1 }, new WorkLog() { Id = 2 }, new WorkLog() { Id = 3 } };

            Assert.AreEqual("summarized 3", repository.SummarizeViaProperty());
        }

        [TestMethod]
        public void InjectedParameter_WithoutDefaultValue_ThrowsDescriptiveException()
        {
            var repository = BuildRepositoryBuilder(SummarizerResolver()).Build<IInvalidInjectRepository>();

            var exception = Assert.ThrowsException<InvalidAttributeException>(
                () => repository.SummarizeAll(new WorkLogSummarizer()));

            Assert.AreEqual(typeof(InjectAttribute), exception.AttributeType);
            StringAssert.Contains(exception.Message, "is not optional");
        }

        [TestMethod]
        public void InjectedMember_WithoutServiceResolver_ThrowsDescriptiveException()
        {
            var repository = BuildRepositoryBuilder().Build<ICustomImplementationRepository>();

            var exception = Assert.ThrowsException<InvalidOperationException>(() => repository.SummarizeAll());

            StringAssert.Contains(exception.Message, "no service resolver is configured");
        }

        [TestMethod]
        public void InjectedMember_WhenResolverReturnsNull_ThrowsDescriptiveException()
        {
            var repository = BuildRepositoryBuilder(t => null).Build<ICustomImplementationRepository>();

            var exception = Assert.ThrowsException<InvalidOperationException>(() => repository.SummarizeAll());

            StringAssert.Contains(exception.Message, "returned null");
        }

        [TestMethod]
        public void BuildWithResolver_UsesItAsTheServiceResolver()
        {
            var repository = BuildRepositoryBuilder().Build<ICustomImplementationRepository>(SummarizerResolver());

            materializer.Result = new List<WorkLog.IWorkLogId>() { new WorkLog() { Id = 1 } };

            Assert.AreEqual("summarized 1", repository.SummarizeAll());
        }

        [TestMethod]
        public void AbstractClass_VirtualMethodWithBody_IsNotParsedAsSql()
        {
            var repository = BuildRepositoryBuilder()
                .Build<CustomImplementationAbstractRepository>();

            materializer.Result = new List<WorkLog.IWorkLogId>()
                { new WorkLog() { Id = 1 }, new WorkLog() { Id = 2 } };

            Assert.AreEqual(2, repository.CountAllIds());
        }

        [TestMethod]
        public void AbstractClass_VirtualMethodReceivesInjectedParameter()
        {
            var repository = BuildRepositoryBuilder(SummarizerResolver())
                .Build<CustomImplementationAbstractRepository>();

            materializer.Result = new List<WorkLog.IWorkLogId>() { new WorkLog() { Id = 1 } };

            Assert.AreEqual("summarized 1", repository.SummarizeAll());
        }

        private class StubQueryMaterializer : IQueryMaterializer
        {
            public object Result { get; set; }
            public int InvocationCount { get; private set; }
            public IDictionary<string, object> LastArguments { get; private set; }

            public object Materialize(SqlMethodInvocation invocation, IEnumerable<ParameterArg> parameterArgs)
            {
                InvocationCount++;
                LastArguments = invocation.SqlStatement.GetPreparedStatement(parameterArgs).Parameters;
                return Result;
            }

            public Task<object> MaterializeAsync(SqlMethodInvocation invocation, IEnumerable<ParameterArg> parameterArgs)
            {
                return Task.FromResult(Materialize(invocation, parameterArgs));
            }

            // the remaining overloads are the raw-sql surface, unused by these tests
            public Task<object> MaterializeAsync(Type outputType, PreparedSqlStatement sqlStatement) => throw new NotSupportedException();
            public Task<object> MaterializeAsync(Type outputType, string commandText) => throw new NotSupportedException();
            public Task<T> MaterializeAsync<T>(PreparedSqlStatement sqlStatement) => throw new NotSupportedException();
            public Task<T> MaterializeAsync<T>(string commandText) => throw new NotSupportedException();
            public Task<T> MaterializeAsync<T>(string commandText, IDictionary<string, object> parameters, PrimaryKeyQuerySpecifierCollection primaryKeys = null) => throw new NotSupportedException();
            public Task<T> MaterializeAsync<T>(string commandText, object parameters, PrimaryKeyQuerySpecifierCollection primaryKeys = null) => throw new NotSupportedException();
            public object Materialize(Type outputType, PreparedSqlStatement sqlStatement) => throw new NotSupportedException();
            public object Materialize(Type outputType, string commandText) => throw new NotSupportedException();
            public T Materialize<T>(PreparedSqlStatement sqlStatement) => throw new NotSupportedException();
            public T Materialize<T>(string commandText) => throw new NotSupportedException();
            public T Materialize<T>(string commandText, IDictionary<string, object> parameters, PrimaryKeyQuerySpecifierCollection primaryKeys = null) => throw new NotSupportedException();
            public T Materialize<T>(string commandText, object parameters, PrimaryKeyQuerySpecifierCollection primaryKeys = null) => throw new NotSupportedException();
        }

        private class StubQueryExecutor : IQueryExecutor
        {
            public Task<IDataReader> ExecuteReaderAsync(string commandText, IDictionary<string, object> parameters, int? commandTimeout = null) =>
                throw new NotSupportedException();

            public IDataReader ExecuteReader(string commandText, IDictionary<string, object> parameters, int? commandTimeout = null) =>
                throw new NotSupportedException();

            public Task<int> ExecuteNonQueryAsync(string commandText, IDictionary<string, object> parameters, int? commandTimeout = null) =>
                Task.FromResult(0);

            public int ExecuteNonQuery(string commandText, IDictionary<string, object> parameters, int? commandTimeout = null) => 0;
        }
    }
}
