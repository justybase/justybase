namespace JustyBase.Common.Models;

public sealed class ConnectionInfoSafe
{
    public required string ConnectionName { get; init; }
    public string? Driver { get; init; }
    public string? Server { get; init; }
    public string? UserName { get; init; }
    public string? Database { get; init; }
    public string? Schema { get; init; }
    public string? Warehouse { get; init; }
    public string? Role { get; init; }
    
    public static ConnectionInfoSafe FromLoginData(PluginCommon.Models.LoginDataModel login)
    {
        return new ConnectionInfoSafe
        {
            ConnectionName = login.ConnectionName,
            Driver = login.Driver,
            Server = login.Server,
            UserName = login.UserName,
            Database = login.Database,
            Schema = login.Schema,
            Warehouse = login.Warehouse,
            Role = login.Role
        };
    }
}
