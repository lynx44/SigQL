﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SigQL.Extensions;
using SigQL.Schema;

namespace SigQL
{
    /// <summary>
    /// Generic class that wraps common accessors for
    /// ParameterInfo and PropertyInfo
    /// </summary>
    internal interface IArgument
    {
        Type Type { get; }
        string Name { get; }
        TAttribute GetCustomAttribute<TAttribute>()
            where TAttribute : Attribute;

        IEnumerable<IArgument> ClassProperties { get; }

        IArgument Parent { get; }

        ParameterInfo GetParameterInfo();
        PropertyInfo GetPropertyInfo();
        
        string GetCallsiteTypeName();
    }


    internal class TableArgument : IArgument
    {
        private readonly ITableDefinition tableDefinition;
        private readonly IEnumerable<IArgument> arguments;
        public Type Type => throw new InvalidOperationException("Argument is a table, not a CLR type");
        public string Name => tableDefinition.Name;

        public TableArgument(ITableDefinition tableDefinition, IEnumerable<IArgument> arguments)
        {
            this.tableDefinition = tableDefinition;
            this.arguments = arguments;
        }

        public TAttribute GetCustomAttribute<TAttribute>() where TAttribute : Attribute
        {
            return null;
            //throw new InvalidOperationException("Argument is a table, not a CLR type");
        }

        public IEnumerable<IArgument> ClassProperties => this.arguments;
        public IArgument Parent { get; set; }
        public ParameterInfo GetParameterInfo()
        {
            throw new InvalidOperationException("Argument is a table, not a parameter");
        }

        public PropertyInfo GetPropertyInfo()
        {
            throw new InvalidOperationException("Argument is a table, not a property");
        }

        public TResult WhenParameter<TResult>(Func<ParameterInfo, TResult> parameterAction, Func<PropertyInfo, TResult> propertyAction)
        {
            throw new InvalidOperationException("Argument is a table, not a parameter or property");
        }

        public string GetCallsiteTypeName()
        {
            return "table";
        }
    }

    internal class TypeArgument : IArgument
    {
        private readonly DatabaseResolver databaseResolver;
        public Type Type { get; }
        public string Name => OutputFactory.UnwrapType(Type).Name;

        public TypeArgument(Type type, DatabaseResolver databaseResolver)
        {
            this.databaseResolver = databaseResolver;
            Type = type;
        }

        public TAttribute GetCustomAttribute<TAttribute>() where TAttribute : Attribute
        {
            return this.Type.GetCustomAttribute<TAttribute>();
        }

        public IEnumerable<IArgument> ClassProperties =>
            this.databaseResolver.IsTableOrTableProjection(this.Type)
                ? OutputFactory.UnwrapType(this.Type).GetProperties().Select(p => new PropertyArgument(p, this, databaseResolver)).ToList()
                : new List<PropertyArgument>();

        public IArgument Parent { get; set; }
        public ParameterInfo GetParameterInfo()
        {
            throw new InvalidOperationException("Argument is a type, not a parameter");
        }

        public PropertyInfo GetPropertyInfo()
        {
            throw new InvalidOperationException("Argument is a type, not a property");
        }

        public TResult WhenParameter<TResult>(Func<ParameterInfo, TResult> parameterAction, Func<PropertyInfo, TResult> propertyAction)
        {
            throw new InvalidOperationException("Argument is a type, not a parameter or property");
        }

        public string GetCallsiteTypeName()
        {
            return "type";
        }
    }

    internal class ColumnArgument : IArgument
    {
        private readonly IColumnDefinition column;

        public ColumnArgument(IColumnDefinition column, IArgument parent)
        {
            this.column = column;
            this.ClassProperties = Array.Empty<IArgument>();
            this.Parent = parent;
        }

        public Type Type { get; }
        public string Name => column.Name;
        public TAttribute GetCustomAttribute<TAttribute>() where TAttribute : Attribute
        {
            return null;
        }

        public IEnumerable<IArgument> ClassProperties { get; set; }
        public IArgument Parent { get; set; }
        public ParameterInfo GetParameterInfo()
        {
            throw new NotImplementedException();
        }

        public PropertyInfo GetPropertyInfo()
        {
            throw new NotImplementedException();
        }

        public string GetCallsiteTypeName()
        {
            return "generated";
        }
    }

    internal static class MethodInfoExtensions
    {
        public static IEnumerable<IArgument> AsArguments(this IEnumerable<ParameterInfo> parameters, DatabaseResolver databaseResolver)
        {
            return parameters.Select(p => new ParameterArgument(p, databaseResolver)).ToList();
        }
    }

    internal static class IArgumentExtensions
    {
        public static IEnumerable<IArgument> PathToRoot(this IArgument argument)
        {
            var arguments = new List<IArgument>();
            do
            {
                arguments.Add(argument);
                argument = argument.Parent;
            } while (argument != null);

            return arguments;
        }

        public static IEnumerable<IArgument> RootToPath(this IArgument argument)
        {
            return argument.PathToRoot().Reverse().ToList();
        }

        public static IArgument FindParameter(this IArgument argument)
        {
            return
                argument.RootToPath().Take(1).FirstOrDefault(arg => arg is ParameterArgument);
        }

        public static IEnumerable<IArgument> FindPropertiesFromRoot(this IArgument argument)
        {
            return
                argument.RootToPath().Where(arg => arg is PropertyArgument).ToList();
        }

        public static IEnumerable<IArgument> FindFromPropertyRoot(this IArgument argument)
        {
            var rootToPath = argument.RootToPath().ToList();
            var firstProperty = rootToPath.FirstOrDefault(arg => arg is PropertyArgument);

            if (firstProperty != null)
            {
                var remainder = rootToPath.Skip(rootToPath.IndexOf(firstProperty)).ToList();
                return
                    remainder.ToList();
            }

            return Array.Empty<IArgument>();
        }

        public static string FullyQualifiedName(this IArgument argument)
        {
            return string.Join(".", argument.RootToPath().Select(a => a.Name));
        }

        public static string FullyQualifiedTypeName(this IArgument argument)
        {
            var rootToPath = argument.RootToPath();
            return string.Join(".", rootToPath.Take(rootToPath.Count() - 1).Select(a => a.Type?.Name ?? a.Name).AppendOne(argument.Name));
        }

        public static int CalculateDepth(this IArgument argument)
        {
            int depth = 0;
            var parent = argument.Parent;
            while (parent != null)
            {
                depth++;
                parent = parent.Parent;
            }
            return depth;
        }


        public static ParameterPath ToParameterPath(this IArgument argument)
        {
            var rootToPath = argument.RootToPath();
            return new ParameterPath(argument);
        }

        public static IArgument GetEndpoint(this IArgument argument)
        {
            return argument.PathToRoot().First();
        }
        public static bool IsDescendentOf(this IArgument argument, Type parentType)
        {
            var node = argument;
            while (parentType != typeof(void) && node.Parent != null)
            {
                if (OutputFactory.UnwrapType(node.Parent.Type) == parentType)
                {
                    return true;
                }

                node = node.Parent;
            }

            return false;
        }

        internal static int GetOrdinal(this IEnumerable<IArgument> arguments, IArgument argument)
        {
            var depth = argument.PathToRoot().Count();
            var parentIndex = argument.Parent?.ClassProperties.ToList().IndexOf(argument) ?? 0;
            var paramPosition = arguments.ToList().IndexOf(argument);
            return (depth * 100) + parentIndex * 10 + paramPosition;
            //throw new NotImplementedException("implement ordinals");
            //var parameterList = arguments.ToList();
            //return parameterList.IndexOf(path.Parameter);
        }

        internal static IEnumerable<IArgument> Filter(this IEnumerable<IArgument> arguments, Func<IArgument, bool> matchCondition)
        {
            var matches = new List<IArgument>();
            var matchingArguments = arguments.Where(a => matchCondition(a)).ToList();
            matches.AddRange(matchingArguments);
            matches.AddRange(arguments.SelectMany(a => Filter(a.ClassProperties, matchCondition)).ToList());

            return matches;
        }

        internal static bool EquivalentTo(this IArgument arg1, IArgument arg2)
        {
            return (arg1 == null && arg2 == null) || 
                   (
                       arg1 != null && arg2 != null &&
                       arg1.GetType() == arg2.GetType() &&
                       arg1.Type == arg2.Type &&
                       arg1.Name == arg2.Name &&
                       arg1.Parent.EquivalentTo(arg2.Parent)
                   );
        }
    }

    internal class ArgumentContainer
    {
        public IEnumerable<IArgument> Arguments { get; set; }
        public ITableDefinition TargetTable { get; set; }
    }

    internal class ParameterArgument : IArgument
    {
        public IArgument Parent { get; }
        public ParameterInfo GetParameterInfo()
        {
            return this.parameterInfo;
        }

        public PropertyInfo GetPropertyInfo()
        {
            throw new InvalidOperationException("Argument is a parameter, not a property");
        }

        public TResult WhenParameter<TResult>(Func<ParameterInfo, TResult> parameterAction, Func<PropertyInfo, TResult> propertyAction)
        {
            return parameterAction(this.parameterInfo);
        }

        public string GetCallsiteTypeName()
        {
            return "parameter";
        }

        private readonly ParameterInfo parameterInfo;
        private readonly DatabaseResolver databaseResolver;

        public ParameterArgument(ParameterInfo parameterInfo, DatabaseResolver databaseResolver)
        {
            this.parameterInfo = parameterInfo;
            this.databaseResolver = databaseResolver;
        }

        public Type Type => parameterInfo.ParameterType;
        public string Name => parameterInfo.Name;
        public TAttribute GetCustomAttribute<TAttribute>()
            where TAttribute : Attribute
        {
            return parameterInfo.GetCustomAttribute<TAttribute>();
        }

        public IEnumerable<IArgument> ClassProperties =>
            this.databaseResolver.IsTableOrTableProjection(this.Type)
                ? OutputFactory.UnwrapType(this.Type).GetProperties().Select(p => new PropertyArgument(p, this, databaseResolver)).ToList()
                : new List<PropertyArgument>();
    }

    internal class PropertyArgument : IArgument
    {
        public IArgument Parent { get; }
        public ParameterInfo GetParameterInfo()
        {
            throw new InvalidOperationException("Argument is a property, not a parameter");
        }

        public PropertyInfo GetPropertyInfo()
        {
            return this.propertyInfo;
        }

        public TResult WhenParameter<TResult>(Func<ParameterInfo, TResult> parameterAction, Func<PropertyInfo, TResult> propertyAction)
        {
            return propertyAction(this.propertyInfo);
        }

        public string GetCallsiteTypeName()
        {
            return "property";
        }


        private readonly PropertyInfo propertyInfo;
        private readonly DatabaseResolver databaseResolver;

        public PropertyArgument(PropertyInfo propertyInfo, IArgument parent, DatabaseResolver databaseResolver)
        {
            Parent = parent;
            this.propertyInfo = propertyInfo;
            this.databaseResolver = databaseResolver;
        }

        public Type Type => propertyInfo.PropertyType;
        public string Name => propertyInfo.Name;
        public TAttribute GetCustomAttribute<TAttribute>() where TAttribute : Attribute
        {
            return propertyInfo.GetCustomAttribute<TAttribute>();
        }
        public IEnumerable<IArgument> ClassProperties =>
            this.databaseResolver.IsTableOrTableProjection(this.Type)
                ? OutputFactory.UnwrapType(this.Type).GetProperties().Select(p => new PropertyArgument(p, this, databaseResolver)).ToList()
                : new List<PropertyArgument>();
    }

    internal class ArgumentTreeNode
    {
        internal IArgument Argument { get; set; }
        internal object Value { get; set; }
        internal IEnumerable<ArgumentTreeNode> Children { get; set; }
        internal ArgumentTreeNode Parent { get; set;  }

        internal IEnumerable<ArgumentTreeNode> FindParents()
        {
            var parents = new List<ArgumentTreeNode>();
            var parent = this.Parent;
            while (parent != null)
            {
                parents.Add(parent);
                parent = parent.Parent;
            }

            return parents;
        }
    }

    internal class ArgumentTree
    {
        private IEnumerable<ArgumentTreeNode> nodes;

        public ArgumentTree(DatabaseResolver databaseResolver, IEnumerable<ParameterArg> parameters)
        {
            nodes = Build(databaseResolver, parameters);
        }

        internal IEnumerable<ArgumentTreeNode> FindTreeNodes(IEnumerable<IArgument> arguments)
        {
            var result = new List<ArgumentTreeNode>();
            Traverse(node =>
            {
                if (arguments.Any(a => a.EquivalentTo(node.Argument)))
                {
                    result.Add(node);
                }
            }, nodes);

            return result;
        }

        //internal IEnumerable<ArgumentTreeNode> FindRelatedTreeNodes(IEnumerable<IArgument> arguments, IArgument parentArgument)
        //{
        //    var result = new List<ArgumentTreeNode>();
        //    Traverse(node =>
        //    {
        //        if (node.Argument.Equals(parentArgument))
        //        {
        //            result.AddRange(node.FindParents().Where(p => arguments.Any(a => a.Equals(p.Argument))));
        //            Traverse(childNode =>
        //            {
        //                if (arguments.Any(a => a.Equals(childNode.Argument)))
        //                {
        //                    result.Add(childNode);
        //                }
        //            }, node.Children);
        //        }
        //    }, nodes);

        //    return result;
        //}

        //internal IEnumerable<ArgumentTreeNode> FindChildTreeNodes(IArgument parent, object parentValue)
        //{
        //    IEnumerable<ArgumentTreeNode> result = new List<ArgumentTreeNode>();
        //    Traverse(node =>
        //    {
        //        if (node.Argument.Equals(parent) && node.Value == parentValue)
        //        {
        //            result = node.Children;
        //        }
        //    }, nodes);

        //    return result;
        //}

        private void Traverse(Action<ArgumentTreeNode> action, IEnumerable<ArgumentTreeNode> nodeList)
        {
            foreach (var node in nodeList)
            {
                action(node);
                Traverse(action, node.Children);
            }
        }

        private IEnumerable<ArgumentTreeNode> Build(DatabaseResolver databaseResolver, IEnumerable<ParameterArg> parameters)
        {
            return parameters.Select(p =>
            {
                var node = BuildNode(new ParameterArgument(p.Parameter, databaseResolver), p.Value, null);
                return node;
            }).ToList();
        }

        private ArgumentTreeNode BuildNode(IArgument argument, object value, ArgumentTreeNode parent)
        {
            var node = new ArgumentTreeNode()
            {
                Argument = argument,
                Value = value,
                Parent = parent
            };
            node.Children = argument.ClassProperties.Select(c =>
            {
                var childValue = GetChildValue(c, value);
                return BuildNode(c, childValue, node);
            }).ToList();
            return node;
        }

        private object GetChildValue(IArgument argument, object parentValue)
        {
            return MethodSqlStatement.GetValueForParameterPath(
                parentValue, argument.GetPropertyInfo().AsEnumerable());
        }
    }
}