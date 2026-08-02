using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Database;
using JustyBase.PluginDatabaseBase.Models;

namespace JustyBase.Tests;

public sealed class DatabaseCacheInvalidationHelperTests
{
    [Fact]
    public void ClearCaches_ProcedureAndView_ClearsOnlyRequestedCaches()
    {
        var procedureCache = new Dictionary<string, Dictionary<string, Dictionary<string, ProcedureCachedInfo>>>
        {
            ["DB"] = new()
            {
                ["SCHEMA"] = new()
                {
                    ["PROC_A"] = new ProcedureCachedInfo { ProcedureSignature = "PROC_A()" }
                }
            }
        };
        var viewCache = new Dictionary<string, Dictionary<string, Dictionary<string, ViewCachedInfo>>>
        {
            ["DB"] = new()
            {
                ["SCHEMA"] = new()
                {
                    ["VIEW_A"] = new ViewCachedInfo("select 1")
                }
            }
        };
        var synonymCache = new Dictionary<string, Dictionary<string, Dictionary<string, SynonymCachedInfo>>>
        {
            ["DB"] = new()
            {
                ["SCHEMA"] = new()
                {
                    ["SYN_A"] = new SynonymCachedInfo("DB", "SCHEMA", "OBJ")
                }
            }
        };

        DatabaseCacheInvalidationHelper.ClearCaches(
            [TypeInDatabaseEnum.Procedure, TypeInDatabaseEnum.View],
            procedureCache,
            viewCache,
            synonymCache,
            clearExternalTableCache: null);

        Assert.Empty(procedureCache);
        Assert.Empty(viewCache);
        Assert.NotEmpty(synonymCache);
    }

    [Fact]
    public void ClearCaches_ExternalTable_InvokesCallback()
    {
        var procedureCache = new Dictionary<string, Dictionary<string, Dictionary<string, ProcedureCachedInfo>>>();
        var viewCache = new Dictionary<string, Dictionary<string, Dictionary<string, ViewCachedInfo>>>();
        var synonymCache = new Dictionary<string, Dictionary<string, Dictionary<string, SynonymCachedInfo>>>();
        var callbackInvoked = false;

        DatabaseCacheInvalidationHelper.ClearCaches(
            [TypeInDatabaseEnum.ExternalTable],
            procedureCache,
            viewCache,
            synonymCache,
            clearExternalTableCache: () => callbackInvoked = true);

        Assert.True(callbackInvoked);
    }

    [Fact]
    public void ClearCaches_Synonym_ClearsSynonymCache()
    {
        var procedureCache = new Dictionary<string, Dictionary<string, Dictionary<string, ProcedureCachedInfo>>>();
        var viewCache = new Dictionary<string, Dictionary<string, Dictionary<string, ViewCachedInfo>>>();
        var synonymCache = new Dictionary<string, Dictionary<string, Dictionary<string, SynonymCachedInfo>>>
        {
            ["DB"] = new()
            {
                ["SCHEMA"] = new()
                {
                    ["SYN_A"] = new SynonymCachedInfo("DB", "SCHEMA", "OBJ")
                }
            }
        };

        DatabaseCacheInvalidationHelper.ClearCaches(
            [TypeInDatabaseEnum.Synonym],
            procedureCache,
            viewCache,
            synonymCache,
            clearExternalTableCache: null);

        Assert.Empty(synonymCache);
    }
}
