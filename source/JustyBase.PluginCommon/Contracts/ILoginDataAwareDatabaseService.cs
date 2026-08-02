using JustyBase.PluginCommon.Models;

namespace JustyBase.PluginCommon.Contracts;

public interface ILoginDataAwareDatabaseService
{
    void ApplyLoginData(LoginDataModel loginData);
}
