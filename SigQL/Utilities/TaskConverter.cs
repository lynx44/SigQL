using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SigQL.Utilities
{

    /// <summary>
    /// adapted from https://github.com/twogood/Activout.RestClient/blob/master/Activout.RestClient/Helpers/Implementation/TaskConverter2.cs
    /// </summary>
    public class TaskConverter
    {
        private readonly Type _type;
        private readonly MethodInfo _setResultMethod;
        private readonly MethodInfo _setExceptionMethod;
        private readonly PropertyInfo _taskProperty;

        public TaskConverter(Type actualReturnType)
        {
            _type = typeof(TaskCompletionSource<>).MakeGenericType(actualReturnType);
            _setResultMethod = _type.GetMethod("SetResult");
            _setExceptionMethod = _type.GetMethod("SetException", new[] { typeof(Exception) });
            _taskProperty = _type.GetProperty("Task");
        }

        public object ConvertReturnType(Task<object> task)
        {
            var taskCompletionSource = Activator.CreateInstance(_type);

            Task.Factory.StartNew(async () => await AsyncHelper(task, taskCompletionSource));

            return GetTask(taskCompletionSource);
        }

        private async Task AsyncHelper(Task<object> task, object taskCompletionSource)
        {
            try
            {
                SetResult(taskCompletionSource, await task);
            }
            catch (Exception e)
            {
                SetException(taskCompletionSource, e);
            }
        }

        private object GetTask(object taskCompletionSource)
        {
            return _taskProperty.GetValue(taskCompletionSource);
        }

        private void SetException(object taskCompletionSource, Exception e)
        {
            _setExceptionMethod.Invoke(taskCompletionSource, new object[] { e });
        }

        private void SetResult(object taskCompletionSource, object result)
        {
            _setResultMethod.Invoke(taskCompletionSource, new[] { result });
        }
    }
}
