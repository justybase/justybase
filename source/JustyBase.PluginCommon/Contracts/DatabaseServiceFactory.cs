using JustyBase.PluginCommon.Models;

namespace JustyBase.PluginCommon.Contracts;

/// <summary>Creates a database service from named connection settings.</summary>
public delegate IDatabaseService DatabaseServiceFactory(DbConnectionOptions options);
