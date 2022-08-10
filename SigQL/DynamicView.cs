using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Castle.DynamicProxy;
using SigQL.Extensions;

namespace SigQL
{
    public interface IDynamicViewFactory
    {
        T Create<T>(string sql)
            where T : class;
    }

    public class DynamicViewFactory : IDynamicViewFactory
    {
        public T Create<T>(string sql)
            where T : class
        {
            return new DynamicView(sql).As<T>();
        }
    }

    public class DynamicView : IDynamicView
        
    {
        internal DynamicView(string sql)
        {
            Sql = sql;
        }

        public T As<T>()
            where T : class
        {
            var options = new ProxyGenerationOptions();
            options.AddMixinInstance((IDynamicView) this);

            var proxyGenerator = new Castle.DynamicProxy.ProxyGenerator();
            return (T) (typeof(T).IsInterface ? 
                proxyGenerator.CreateInterfaceProxyWithoutTarget(typeof(T), options) :
                proxyGenerator.CreateClassProxy(typeof(T), options));
        }

        public string Sql { get; set; }
    }

    public interface IDynamicView
    {
        string Sql { get; }
    }
}
