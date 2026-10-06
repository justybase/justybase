using JustyBase.PluginCommon.Models;

namespace JustyBase.PluginCommon.Contracts;

public interface IDatabaseInfo
{
    ISimpleLogger GlobalLoggerObject { get; }

    /// <summary>
    /// Point-in-time snapshot of saved connections. The returned dictionary is
    /// a copy: mutating it has no effect. Use
    /// <c>IGeneralApplicationData.AddToOrEditLoginData</c> /
    /// <c>DeleteFromLoginData</c> / <c>SetAccessOptions</c> for mutations.
    /// </summary>
    IReadOnlyDictionary<string, LoginDataModel> LoginDataDic { get; }
    string GetDataDir();
}
