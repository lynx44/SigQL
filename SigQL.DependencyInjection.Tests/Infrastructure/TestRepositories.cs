using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using SigQL.Schema;
using SigQL.Types;
using SigQL.Types.Attributes;

namespace SigQL.DependencyInjection.Tests.Infrastructure
{
    /// <summary>
    /// Building a proxy never touches the database — SQL is generated when a generated method is
    /// called — so registration tests only need a stand in.
    /// </summary>
    public class StubQueryExecutor : IQueryExecutor
    {
        public Task<IDataReader> ExecuteReaderAsync(string commandText, IDictionary<string, object> parameters, int? commandTimeout = null) =>
            throw new NotSupportedException();

        public IDataReader ExecuteReader(string commandText, IDictionary<string, object> parameters, int? commandTimeout = null) =>
            throw new NotSupportedException();

        public Task<int> ExecuteNonQueryAsync(string commandText, IDictionary<string, object> parameters, int? commandTimeout = null) =>
            throw new NotSupportedException();

        public int ExecuteNonQuery(string commandText, IDictionary<string, object> parameters, int? commandTimeout = null) =>
            throw new NotSupportedException();
    }

    public class StubDatabaseConfiguration : IDatabaseConfiguration
    {
        public ITableDefinitionCollection Tables { get; } = new TableDefinitionCollection(new List<ITableDefinition>());
    }

    public interface ITagProvider
    {
        string Tag { get; }
    }

    public class TagProvider : ITagProvider
    {
        public string Tag { get; } = Guid.NewGuid().ToString();
    }

    /// <summary>
    /// A custom body that reads an injected service, so tests can observe which scope the service
    /// resolver came from.
    /// </summary>
    public interface IScopedTagRepository
    {
        string GetTag([Inject] ITagProvider tagProvider = null) => tagProvider.Tag;
    }

    /// <summary>
    /// Named without the "Repository" suffix, and picked up only because it implements IRepository.
    /// </summary>
    public interface ITaggedByMarker : IRepository
    {
        string Describe() => "marker";
    }

    /// <summary>
    /// Matches the naming convention but cannot be proxied, so a scan must skip it.
    /// </summary>
    public class ConcreteRepository
    {
    }

    public interface IAmbiguousRepository
    {
    }

    public abstract class FirstAmbiguousRepository : IAmbiguousRepository
    {
    }

    public abstract class SecondAmbiguousRepository : IAmbiguousRepository
    {
    }

    public interface IReplaceableRepository
    {
        string Describe();
    }

    public class HandWrittenReplaceableRepository : IReplaceableRepository
    {
        public string Describe() => "hand written";
    }
}
