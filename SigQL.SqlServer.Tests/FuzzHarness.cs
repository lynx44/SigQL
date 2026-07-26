using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.SqlServer.Tests.Data;
using SigQL.Tests.Common.Databases.Labor;
using SigQL.Types;

namespace SigQL.SqlServer.Tests
{
    /// <summary>
    /// Exploratory harness (not a real regression test). Invokes every method on the fuzz
    /// repositories against the live test database and reports any that blow up.
    /// </summary>
    [TestClass]
    public class FuzzHarness
    {
        private LaborDbContext laborDbContext;
        private RepositoryBuilder repositoryBuilder;

        [TestInitialize]
        public void Setup()
        {
            var sqlConnection = (Microsoft.Data.SqlClient.SqlConnection) TestSettings.LaborDbConnection;
            DatabaseHelpers.DropAllObjects(sqlConnection);
            this.laborDbContext = new LaborDbContext();
            laborDbContext.Database.Migrate();

            var sqlDatabaseConfiguration = new SqlDatabaseConfiguration(sqlConnection.ConnectionString);
            repositoryBuilder = new RepositoryBuilder(
                new SqlQueryExecutor(() => TestSettings.LaborDbConnection),
                sqlDatabaseConfiguration,
                statement => { });

            SeedData();
        }

        private void SeedData()
        {
            var address = new EFAddress() { StreetAddress = "123 fake st", City = "Seattle", State = "WA", Classification = AddressClassification.Home };
            var address2 = new EFAddress() { StreetAddress = "456 real ave", City = "Portland", State = "OR", Classification = AddressClassification.Work };
            var employee = new EFEmployee() { Name = "Bob", Addresses = new List<EFAddress>() { address } };
            var employee2 = new EFEmployee() { Name = "Sue", Addresses = new List<EFAddress>() { address, address2 } };
            var location = new EFLocation() { Name = "HQ", Address = address };
            laborDbContext.Address.AddRange(address, address2);
            laborDbContext.Employee.AddRange(employee, employee2);
            laborDbContext.Location.Add(location);
            laborDbContext.WorkLog.AddRange(
                new EFWorkLog() { StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2), Employee = employee, Location = location },
                new EFWorkLog() { StartDate = new DateTime(2022, 2, 2), EndDate = new DateTime(2022, 2, 3), Employee = employee2, Location = location });
            var category = new EFCategory() { Id = Guid.NewGuid(), Name = "cat" };
            laborDbContext.Category.Add(category);
            laborDbContext.CategoryItem.Add(new EFCategoryItem() { Name = "item", Category = category });
            laborDbContext.CompositeKeyTable.Add(new EFCompositeKeyTable() { FirstName = "first", LastName = "last" });
            laborDbContext.SaveChanges();
        }

        private enum ArgMode
        {
            /// <summary>every value populated</summary>
            Populated,
            /// <summary>every nullable/reference leaf null, collections null</summary>
            Nulls,
            /// <summary>collections empty, strings empty</summary>
            Empty
        }

        [TestMethod]
        public void Fuzz()
        {
            var results = new List<string>();
            var failures = new List<string>();
            RunAll<IFuzzRepository>(results, failures);
            RunAll<IMonolithicRepository>(results, failures);

            Console.WriteLine(string.Join(Environment.NewLine, results));
            if (failures.Any())
            {
                Assert.Fail($"{failures.Count} fuzz failures:{Environment.NewLine}{string.Join(Environment.NewLine + new string('-', 80) + Environment.NewLine, failures)}");
            }
        }

        private void RunAll<TRepo>(List<string> results, List<string> failures)
            where TRepo : class
        {
            var repo = repositoryBuilder.Build<TRepo>();
            foreach (var method in typeof(TRepo).GetMethods().OrderBy(m => m.Name))
            {
                if (method.Name.StartsWith("INVALID_") || method.Name.StartsWith("ILLEGAL_"))
                    continue;
                var signature = typeof(TRepo).Name + "." + Describe(method);
                foreach (ArgMode mode in Enum.GetValues(typeof(ArgMode)))
                {
                    if (mode != ArgMode.Populated && !method.GetParameters().Any())
                        continue;

                    object[] args;
                    try
                    {
                        args = method.GetParameters().Select(p => BuildArg(p.ParameterType, p.Name, 0, mode, true)).ToArray();
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"[{mode}] {signature}{Environment.NewLine}ARG BUILD FAILED: {ex}");
                        continue;
                    }

                    try
                    {
                        var result = method.Invoke(repo, args);
                        if (result is Task task)
                        {
                            task.GetAwaiter().GetResult();
                            result = task.GetType().IsGenericType ? task.GetType().GetProperty("Result").GetValue(task) : null;
                        }
                        // force enumeration/materialization
                        Materialize(result);
                        results.Add($"OK   [{mode}] {signature}");
                    }
                    catch (Exception ex)
                    {
                        var inner = ex is TargetInvocationException tie ? tie.InnerException : ex;
                        // a SigQL exception is a deliberate, described rejection - report but do not fail
                        if (inner.GetType().Namespace == "SigQL.Exceptions" || IsDataConstraintViolation(inner))
                        {
                            results.Add($"REJECTED [{mode}] {signature} -> {inner.GetType().Name}: {FirstLine(inner.Message)}");
                            continue;
                        }
                        results.Add($"FAIL [{mode}] {signature} -> {inner.GetType().Name}: {FirstLine(inner.Message)}");
                        failures.Add($"[{mode}] {signature}{Environment.NewLine}{inner.GetType().FullName}: {inner.Message}{Environment.NewLine}{inner.StackTrace}");
                    }
                }
            }
        }

        private static string FirstLine(string s) => (s ?? "").Split('\n')[0];

        /// <summary>
        /// The fuzzer makes up parameter values, so it routinely references rows that do not exist.
        /// A constraint violation means the sql was valid and the data was not; anything else from
        /// sql server (syntax, unknown identifier, duplicate column, type mismatch) is a defect in
        /// the generated statement.
        /// </summary>
        private static bool IsDataConstraintViolation(Exception exception)
        {
            var sqlException = exception as Microsoft.Data.SqlClient.SqlException;
            if (sqlException == null) return false;

            return sqlException.Errors.Cast<Microsoft.Data.SqlClient.SqlError>().All(e =>
                e.Class < 11 ||       // informational
                e.Number == 547 ||    // foreign key / check constraint conflict
                e.Number == 515 ||    // null into a non-nullable column
                e.Number == 2627 ||   // primary key violation
                e.Number == 2601 ||   // unique index violation
                e.Number == 544 ||    // explicit value for an identity column
                e.Number == 3621);    // "the statement has been terminated" follow-on
        }

        private static void Materialize(object result)
        {
            if (result == null) return;
            if (result is string) return;
            if (result is IEnumerable enumerable)
            {
                foreach (var item in enumerable) Materialize(item);
                return;
            }

            var countResultType = result.GetType().GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(ITotalCountResult<>)));
            if (countResultType != null)
            {
                Materialize(countResultType.GetProperty("Result")?.GetValue(result));
            }
        }

        private static string Describe(MethodInfo method)
        {
            var sb = new StringBuilder();
            foreach (var attr in method.GetCustomAttributes(false))
                sb.Append($"[{attr.GetType().Name.Replace("Attribute", "")}] ");
            sb.Append(FriendlyName(method.ReturnType)).Append(' ').Append(method.Name).Append('(');
            sb.Append(string.Join(", ", method.GetParameters().Select(p =>
                string.Join("", p.GetCustomAttributes(false).Select(a => $"[{a.GetType().Name.Replace("Attribute", "")}]"))
                + FriendlyName(p.ParameterType) + " " + p.Name)));
            sb.Append(')');
            return sb.ToString();
        }

        private static string FriendlyName(Type t)
        {
            if (t == typeof(void)) return "void";
            if (!t.IsGenericType) return t.Name;
            return t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(FriendlyName)) + ">";
        }

        private static object BuildArg(Type type, string name, int depth, ArgMode mode, bool isTopLevelParameter)
        {
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
            {
                if (mode == ArgMode.Nulls) return null;
                return BuildArg(underlying, name, depth, mode, isTopLevelParameter);
            }

            var isFilterClass = !type.IsValueType && type != typeof(string) && type != typeof(byte[]) &&
                                !typeof(IEnumerable).IsAssignableFrom(type) && !typeof(Like).IsAssignableFrom(type) &&
                                !typeof(IOrderBy).IsAssignableFrom(type);

            if (mode == ArgMode.Nulls && !type.IsValueType && !(isFilterClass && isTopLevelParameter))
                return null;

            if (mode == ArgMode.Empty)
            {
                if (type == typeof(string)) return "";
                if (type != typeof(byte[]) && type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
                    return BuildEmptyCollection(type);
            }

            if (type == typeof(int)) return 1;
            if (type == typeof(long)) return 1L;
            if (type == typeof(short)) return (short) 1;
            if (type == typeof(byte)) return (byte) 1;
            if (type == typeof(decimal)) return 1m;
            if (type == typeof(double)) return 1d;
            if (type == typeof(float)) return 1f;
            if (type == typeof(bool)) return true;
            if (type == typeof(Guid)) return Guid.Empty;
            if (type == typeof(DateTime)) return new DateTime(2021, 1, 1);
            if (type == typeof(DateTimeOffset)) return new DateTimeOffset(new DateTime(2021, 1, 1), TimeSpan.Zero);
            if (type == typeof(string)) return "Bob";
            if (type == typeof(byte[])) return new byte[] { 1 };
            if (type.IsEnum) return Enum.GetValues(type).GetValue(0);
            if (type == typeof(SigQL.Types.StartsWith)) return new SigQL.Types.StartsWith("B");
            if (type == typeof(SigQL.Types.Contains)) return new SigQL.Types.Contains("o");
            if (type == typeof(SigQL.Types.EndsWith)) return new SigQL.Types.EndsWith("b");
            if (type == typeof(Like)) return Like.FromUnsafeRawValue("%o%");
            if (type == typeof(OrderByRelation)) return new OrderByRelation("WorkLog->Employee", "Name");
            if (type == typeof(IOrderBy) || type == typeof(OrderBy)) return new OrderBy("WorkLog", "Id", OrderByDirection.Ascending);

            if (type.IsArray)
            {
                var elementType = type.GetElementType();
                var array = Array.CreateInstance(elementType, 1);
                array.SetValue(BuildArg(elementType, name, depth + 1, mode, false), 0);
                return array;
            }

            if (type.IsGenericType)
            {
                var def = type.GetGenericTypeDefinition();
                if (def == typeof(IEnumerable<>) || def == typeof(List<>) || def == typeof(IList<>) ||
                    def == typeof(ICollection<>) || def == typeof(IReadOnlyCollection<>) || def == typeof(IReadOnlyList<>))
                {
                    var elementType = type.GetGenericArguments()[0];
                    var list = (IList) Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
                    list.Add(BuildArg(elementType, name, depth + 1, mode, false));
                    return list;
                }
            }

            if (type.IsInterface || type.IsAbstract) return null;

            if (depth > 4) return null;

            var instance = Activator.CreateInstance(type);
            foreach (var property in type.GetProperties().Where(p => p.CanWrite))
            {
                property.SetValue(instance, BuildArg(property.PropertyType, property.Name, depth + 1, mode, false));
            }

            return instance;
        }

        private static object BuildEmptyCollection(Type type)
        {
            if (type.IsArray) return Array.CreateInstance(type.GetElementType(), 0);
            var elementType = type.IsGenericType ? type.GetGenericArguments()[0] : typeof(object);
            return Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
        }
    }
}
