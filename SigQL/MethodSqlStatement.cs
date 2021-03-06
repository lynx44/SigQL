using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SigQL.Schema;
using SigQL.Types;

namespace SigQL
{
    public class MethodSqlStatement
    {
        public MethodSqlStatement()
        {
            this.Parameters = new List<ParameterPath>();
            this.Tokens = new List<TokenPath>();
        }

        public PreparedSqlStatement GetPreparedStatement(IEnumerable<ParameterArg> parameterValues)
        {
            var additionalSqlParameters = this.ParseTokens(parameterValues);
            var parameters = this.GetParameters(parameterValues);
            var allParameters = MergeParameters(parameters, additionalSqlParameters);
            foreach (var key in allParameters.Keys.ToList())
            {
                if (allParameters[key] == null)
                {
                    allParameters[key] = DBNull.Value;
                }
            }
            return new PreparedSqlStatement()
            {
                CommandText = string.Join("\r\n", this.CommandAst.Select(this.SqlBuilder.Build)),
                Parameters = allParameters
            };
        }
        internal IEnumerable<AstNode> CommandAst { get; set; }
        public Type UnwrappedReturnType { get; set; }
        public Type ReturnType { get; set; }
        public IEnumerable<ParameterPath> Parameters { get; set; }
        // these are string literal replacements, for non parameterized variables
        // sent through method arguments. For example, the direction of the order by clause
        public IEnumerable<TokenPath> Tokens { get; set; }
        // public IEnumerable<ColumnAliasForeignKeyDefinition> ColumnAliasRelations { get; set; }
        public ITableKeyDefinition TargetTablePrimaryKey { get; set; }
        public IDictionary<string, ITableKeyDefinition> TablePrimaryKeyDefinitions { get; set; }
        internal SqlStatementBuilder SqlBuilder { get; set; }

        public IDictionary<string, object> GetParameters(IEnumerable<ParameterArg> methodArgs)
        {
            var joinedParams = this.Parameters.Join(methodArgs, pp => pp.Parameter, pa => pa.Parameter, (pp, pa) => new { Path = pp, Arg = pa }).ToList();
            var dbParameters = new Dictionary<string, object>();
            foreach (var joinedParam in joinedParams)
            {
                var parameterName = joinedParam.Path.SqlParameterName;
                object value = GetValueForParameterPath(joinedParam.Arg.Value, joinedParam.Path.Properties);

                dbParameters[parameterName] = value;
            }

            return dbParameters;
        }

        public IDictionary<string, object> ParseTokens(IEnumerable<ParameterArg> parameterArgs)
        {
            var joinedParams = this.Tokens.Join(parameterArgs, pp => pp.Parameter, pa => pa.Parameter, (pp, pa) => new { Path = pp, Arg = pa }).ToList();
            var allParameters = new Dictionary<string, object>();
            foreach (var joinedParam in joinedParams)
            {
                object value = GetValueForParameterPath(joinedParam.Arg.Value, joinedParam.Path.Properties);

                var newParameters = joinedParam.Path.UpdateNodeFunc.Invoke(value, joinedParam.Path);
                allParameters = MergeParameters(allParameters, newParameters);
            }

            return allParameters;
        }

        private static Dictionary<string, object> MergeParameters(IDictionary<string, object> allParameters, IDictionary<string, object> newParameters)
        {
            var addedParameters = newParameters.Where(p => !allParameters.ContainsKey(p.Key)).ToList();
            var updateParameters = newParameters.Where(p => allParameters.ContainsKey(p.Key)).ToList();
            var concatenatedParameters = allParameters.Concat(addedParameters).ToDictionary(p => p.Key, p => p.Value);
            foreach (var parameter in updateParameters)
            {
                concatenatedParameters[parameter.Key] = parameter.Value;
            }
            return concatenatedParameters;
        }

        internal static object GetValueForParameterPath(object value, IEnumerable<PropertyInfo> propertyPaths)
        {
            if (propertyPaths != null && Enumerable.Any<PropertyInfo>(propertyPaths))
            {
                foreach (var propertyInfo in propertyPaths)
                {
                    value = propertyInfo.GetMethod.Invoke(value, null);
                }
            }

            var parameterProvider = value as IDatabaseParameterValueProvider;
            if (parameterProvider != null)
            {
                value = parameterProvider.SqlValue;
            }

            return value;
        }

        // private void TokenValueConverter(AstNode parentNode, object value)
        // {
        //     switch (parentNode)
        //     {
        //         case OrderByIdentifier x:
        //             var orderByValue = value as IOrderByValue;
        //             var directionString = orderByValue == null || orderByValue.Direction == OrderByDirection.Ascending ? "asc" : "desc";
        //             x.Direction = directionString;
        //             break;
        //         default:
        //             throw new ArgumentException($"Unsupported token parameter value {value?.GetType()}");
        //     }
        // }
    }
}