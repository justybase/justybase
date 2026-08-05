using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Models;

namespace JustyBase.PluginDatabaseBase.Database;

public static class DatabaseCacheInvalidationHelper
{
    public static void ClearCaches(
        TypeInDatabaseEnum[] typeInDatabaseArr,
        Dictionary<string, Dictionary<string, Dictionary<string, ProcedureCachedInfo>>> procedureDictCache,
        Dictionary<string, Dictionary<string, Dictionary<string, ViewCachedInfo>>> viewDictCache,
        Dictionary<string, Dictionary<string, Dictionary<string, SynonymCachedInfo>>> synonymTableDictCache,
        Action? clearExternalTableCache)
    {
        if (typeInDatabaseArr.Contains(TypeInDatabaseEnum.Procedure))
        {
            procedureDictCache.Clear();
        }

        if (typeInDatabaseArr.Contains(TypeInDatabaseEnum.View))
        {
            viewDictCache.Clear();
        }

        if (typeInDatabaseArr.Contains(TypeInDatabaseEnum.ExternalTable))
        {
            clearExternalTableCache?.Invoke();
        }

        if (typeInDatabaseArr.Contains(TypeInDatabaseEnum.Synonym))
        {
            synonymTableDictCache.Clear();
        }
    }
}
