using System;

namespace SigQL.Types.Attributes
{
    /// <summary>
    /// Marks a parameter or property as a dependency that SigQL supplies from the configured
    /// service resolver, rather than treating it as part of a generated query.
    ///
    /// Applied to a parameter of a method that provides its own implementation (a default
    /// interface method, or a virtual method on an abstract repository class), the parameter
    /// is filled in at call time. Such parameters must be optional so callers can omit them.
    ///
    /// Applied to an abstract property, the property returns the resolved service.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
    public class InjectAttribute : Attribute
    {
    }
}
