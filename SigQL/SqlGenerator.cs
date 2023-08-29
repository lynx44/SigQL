using SigQL.Schema;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using SigQL.Extensions;

namespace SigQL
{
    public interface ISqlGenerator
    {
        PreparedSqlStatement CreateSelectQuery(Type type);
    }

    public class SqlGenerator : ISqlGenerator
    {
        private IDatabaseConfiguration databaseConfiguration;
        private IPluralizationHelper pluralizationHelper;
        private MethodParser methodParser;

        public SqlGenerator(IDatabaseConfiguration databaseConfiguration, IPluralizationHelper pluralizationHelper)
        {
            this.databaseConfiguration = databaseConfiguration;
            this.pluralizationHelper = pluralizationHelper;
            methodParser = new MethodParser(new SqlStatementBuilder(), databaseConfiguration, pluralizationHelper);
        }

        public SqlGenerator(IDatabaseConfiguration databaseConfiguration) : this(databaseConfiguration,
            DefaultPluralizationHelper.Instance)
        {
        }

        public PreparedSqlStatement CreateSelectQuery(Type type)
        {
            var methodSqlStatement = this.methodParser.SqlFor(new SqlGeneratorMethodInfo(type));
            var preparedSqlStatement = methodSqlStatement.GetPreparedStatement(new List<ParameterArg>());
            preparedSqlStatement.CommandText = $"select * from ({preparedSqlStatement.CommandText}) __generatedouterquery ";
            return preparedSqlStatement;
        }

        public Func<Expression<Func<T, object>>, string> GetQuotedQualifiedColumnNameResolver<T>()
        {
            return new ColumnNameResolver<T>(new DatabaseResolver(this.databaseConfiguration, this.pluralizationHelper))
                .ResolveQuotedQualifiedColumnName;
        }
    }

    internal class ColumnNameResolver<T>
    {
        private readonly DatabaseResolver databaseResolver;

        public ColumnNameResolver(DatabaseResolver databaseResolver)
        {
            this.databaseResolver = databaseResolver;
        }

        public string ResolveQualifiedColumnName(Expression<Func<T, object>> accessor)
        {
            var propertyList = new List<PropertyInfo>();
            ResolvePropertyPath(accessor.Body as MemberExpression, propertyList);

            if (propertyList.Any())
            {
                var propertyArguments = new List<PropertyArgument>();
                propertyList.Reverse();
                for (var index = 0; index < propertyList.Count; index++)
                {
                    var propertyInfo = propertyList[index];
                    var parentArgument = propertyArguments.Any() ? propertyArguments[index - 1] : null;
                    var propertyArgument = new PropertyArgument(propertyInfo, parentArgument, databaseResolver);

                    propertyArguments.Add(propertyArgument);
                }

                return propertyArguments.Last().FullyQualifiedName();
            }

            throw new ArgumentException("A property must be specified");
        }

        public string ResolveQuotedQualifiedColumnName(Expression<Func<T, object>> accessor)
        {
            return $"\"{ResolveQualifiedColumnName(accessor)}\"";
        }

        private void ResolvePropertyPath(MemberExpression expression, List<PropertyInfo> propertyList)
        {
            if (expression != null)
            {
                var property = expression.Member as PropertyInfo;
                if (property != null)
                {
                    propertyList.Add(property);
                }

                var parentMemberExpression = expression.Expression as MemberExpression;
                
                if (parentMemberExpression != null)
                    ResolvePropertyPath(parentMemberExpression, propertyList);
                else
                {
                    var parentMethodExpression = expression.Expression as MethodCallExpression;
                    var callerExpression = parentMethodExpression?.Arguments?.ElementAt(0) as MemberExpression;
                    
                    if (parentMethodExpression != null && callerExpression != null)
                    {
                        var parentPropertyInfo = callerExpression.Member as PropertyInfo;
                        if (parentPropertyInfo.PropertyType.IsCollectionType() &&
                            parentMethodExpression.Method.ReturnType ==
                            parentPropertyInfo.PropertyType.GetGenericArguments()[0])
                        {
                            ResolvePropertyPath(callerExpression, propertyList);
                        }
                    }
                    
                }
            }
        }
    }

    internal class SqlGeneratorMethodInfo : MethodInfo
    {
        public SqlGeneratorMethodInfo(Type returnType)
        {
            ReturnType = returnType;
        }

        public override object[] GetCustomAttributes(bool inherit)
        {
            return new object[0];
        }

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            return new object[0];
        }

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            return true;
        }

        public override Type DeclaringType => typeof(void);
        public override string Name => "sqlgenerator";
        public override Type ReflectedType => typeof(void);
        public override MethodImplAttributes GetMethodImplementationFlags()
        {
            return MethodImplAttributes.Managed;
        }

        public override ParameterInfo[] GetParameters()
        {
            return new ParameterInfo[0];
        }

        public override object Invoke(object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public override MethodAttributes Attributes => MethodAttributes.Abstract;
        public override RuntimeMethodHandle MethodHandle => new RuntimeMethodHandle();
        public override MethodInfo GetBaseDefinition()
        {
            return null;
        }

        public override Type ReturnType { get; }
        public override ICustomAttributeProvider ReturnTypeCustomAttributes => null;
    }
}
