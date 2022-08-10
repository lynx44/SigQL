using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Castle.DynamicProxy;
using SigQL.Extensions;

namespace SigQL
{
    public class DynamicView : IDynamicView
        
    {
        public DynamicView(string sql)
        {
            Sql = sql;
        }

        public T As<T>()
            where T : class
        {
            //if (!typeof(T).IsInterface)
            //{
            //    throw new ArgumentException(
            //        $"DynamicView can only be used with interface types. {typeof(T)} is not an interface.");
            //}

            var options = new ProxyGenerationOptions();
            options.AddMixinInstance((IDynamicView) this);

            var proxyGenerator = new Castle.DynamicProxy.ProxyGenerator();
            return (T) (typeof(T).IsInterface ? 
                proxyGenerator.CreateInterfaceProxyWithoutTarget(typeof(T), options) :
                proxyGenerator.CreateClassProxy(typeof(T), options));
        }

        //public static implicit operator T(DynamicView<T> view)
        //{
        //    return view.ReturnValue;
        //}

        //public static implicit operator DynamicView<T>(T view)
        //{
        //    return null;
        //}

        public string Sql { get; set; }
        //public T ReturnValue { get; set; }
    }

    public interface IDynamicView
    {
        string Sql { get; }
    }

    //public class DynamicViewInterceptor : IInterceptor
    //{
    //    private readonly DynamicView dynamicView;

    //    public DynamicViewInterceptor(DynamicView dynamicView)
    //    {
    //        this.dynamicView = dynamicView;
    //    }

    //    public void Intercept(IInvocation invocation)
    //    {
    //        throw new NotImplementedException();
    //    }
    //}
}
