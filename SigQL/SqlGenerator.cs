using SigQL.Schema;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace SigQL
{
    public class SqlGenerator
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

        public PreparedSqlStatement CreateWithOuterQuery(Type type)
        {
            var methodSqlStatement = this.methodParser.SqlFor(new SqlGeneratorMethodInfo(type));
            var preparedSqlStatement = methodSqlStatement.GetPreparedStatement(new List<ParameterArg>());
            preparedSqlStatement.CommandText = $"select * from ({preparedSqlStatement.CommandText}) __generatedouterquery ";
            return preparedSqlStatement;
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
