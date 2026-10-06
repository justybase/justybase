using System.Reflection;

namespace JustyBase.Editor;

internal static class ReflectionUtil
{
    internal static TMethod CreateDelegate<TOwner, TMethod>(string methodName)
    {
        var args = typeof(TMethod).GetRuntimeMethods().First(c => c.Name == nameof(Action.Invoke))
            .GetParameters().Select(p => p.ParameterType).ToArray();
        var methodInfo = typeof(TOwner).GetRuntimeMethods().First(m => m.Name == methodName && m.GetParameters()
            .Select(p => p.ParameterType).SequenceEqual(args));
        return (TMethod)(object)methodInfo.CreateDelegate(typeof(TMethod));
    }
}
