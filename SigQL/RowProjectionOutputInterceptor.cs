using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Castle.DynamicProxy;
using SigQL.Extensions;

namespace SigQL
{
    public class RowProjectionBuilder
    {
        public object Build(Type projectionType, RowValues rowValues)
        {
            if (rowValues.Values.All(v => v.Value == DBNull.Value))
            {
                return null;
            }

            var memberResolver = new RowProjectionMemberResolver(rowValues, this);
            if (projectionType.IsInterface)
            {
                var target = new Castle.DynamicProxy.ProxyGenerator().CreateInterfaceProxyWithoutTarget(projectionType,
                    new RowProjectionOutputInterceptor(memberResolver)
                );

                return target;
            }

            var pocoInstance = Activator.CreateInstance(projectionType);
            foreach (var property in projectionType.GetProperties().ToList())
            {
                if (property.CanWrite)
                {
                    var memberResolutionResult = memberResolver.ResolveProperty(property);
                    if (memberResolutionResult.ReturnValueSet)
                    {
                        property.SetValue(pocoInstance, memberResolutionResult.ReturnValue);
                    }
                }
            }

            return pocoInstance;
        }
    }

    internal class RowProjectionMemberResolver
    {
        private readonly RowValues rowValues;
        private readonly RowProjectionBuilder rowProjectionBuilder;
        private Dictionary<PropertyInfo, object> propertyValueCache;

        public RowProjectionMemberResolver(
            RowValues rowValues,
            RowProjectionBuilder rowProjectionBuilder)
        {
            this.rowValues = rowValues;
            this.rowProjectionBuilder = rowProjectionBuilder;
            propertyValueCache = new Dictionary<PropertyInfo, object>();
        }

        public MemberResolutionResult ResolveProperty(PropertyInfo propertyInfo)
        {
            //var propertyInfo = GetMatchingProperty(methodInfo);

            var interceptResult = new MemberResolutionResult();
            if (propertyInfo != null)
            {
                if (propertyValueCache.ContainsKey(propertyInfo))
                {
                    SetReturnValue(propertyValueCache[propertyInfo], interceptResult);
                    return interceptResult;
                }
                // is a value
                if (rowValues.Values.ContainsKey(propertyInfo.Name))
                {
                    var value = rowValues.Values[propertyInfo.Name].ConvertToClr();
                    SetReturnValue(value, interceptResult);
                    propertyValueCache[propertyInfo] = value;
                }

                // is a single navigation property
                var navigationPrefix = $"{propertyInfo.Name}.";
                var navigationPropertyKeys = rowValues.Values.Keys.Where(k => k.StartsWith(navigationPrefix)).ToList();
                if (navigationPropertyKeys.Any())
                {
                    // this is a single navigation property: many-to-one or one-to-one
                    if (!propertyInfo.PropertyType.IsCollectionType())
                    {
                        var navigationProperty = this.rowProjectionBuilder.Build(propertyInfo.PropertyType, this.rowValues.Relations[propertyInfo.Name].Rows.First().Value);
                        SetReturnValue(navigationProperty, interceptResult);
                        propertyValueCache[propertyInfo] = navigationProperty;
                    }
                    // this is a collection navigation property: one-to-many or many-to-many
                    else
                    {
                        var navigationRows = this.rowValues.Relations[propertyInfo.Name];

                        var navigationProperty = navigationRows.Rows.Values.Where(v => v.Values.Any(p => p.Value != DBNull.Value)).OrderBy(v => v.RowNumber).Select(v => this.rowProjectionBuilder.Build(OutputFactory.UnwrapType(propertyInfo.PropertyType), new RowValues() { Values = v.Values })).ToList();

                        SetReturnValue(OutputFactory.Cast(navigationProperty, propertyInfo.PropertyType), interceptResult);
                        propertyValueCache[propertyInfo] = interceptResult.ReturnValue;
                    }

                }
            }

            return interceptResult;
        }

        private void SetReturnValue(object returnValue, MemberResolutionResult memberResolutionResult)
        {
            memberResolutionResult.ReturnValue = returnValue;
            memberResolutionResult.ReturnValueSet = true;
        }

        private PropertyInfo GetMatchingProperty(MethodInfo method)
        {
            return method.DeclaringType.GetProperties()
                .FirstOrDefault(prop => prop.GetGetMethod() == method);
        }
    }
    
    internal class MemberResolutionResult
    {
        public object ReturnValue { get; set; }
        public bool ReturnValueSet { get; set; }
    }

    internal class RowProjectionOutputInterceptor : IInterceptor
    {
        private readonly RowProjectionMemberResolver memberResolver;

        public RowProjectionOutputInterceptor(
            RowProjectionMemberResolver memberResolver)
        {
            this.memberResolver = memberResolver;
        }

        public void Intercept(IInvocation invocation)
        {
            var propertyInfo = GetMatchingProperty(invocation.Method);

            var result = memberResolver.ResolveProperty(propertyInfo);
            
            if (!result.ReturnValueSet)
            {
                invocation.Proceed();
            }
            else
            {
                invocation.ReturnValue = result.ReturnValue;
            }
        }

        private PropertyInfo GetMatchingProperty(MethodInfo method)
        {
            return method.DeclaringType.GetProperties()
                .FirstOrDefault(prop => prop.GetGetMethod() == method);
        }
    }
}
