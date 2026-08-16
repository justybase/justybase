using CommunityToolkit.Mvvm.ComponentModel;
using JustyBase.SqliteDriver.Samples;

namespace JustyBase.ViewModels.Tools;

public sealed partial class SqliteSampleObjectOption : ObservableObject
{
    public SqliteSampleObjectOption(SqliteSampleObjectDefinition definition)
    {
        Definition = definition;
    }

    public SqliteSampleObjectDefinition Definition { get; }

    public string DisplayName => Definition.DisplayName;

    public string KindDisplayName => Definition.Kind switch
    {
        SqliteSampleObjectKind.Table => "Table",
        SqliteSampleObjectKind.View => "View",
        SqliteSampleObjectKind.Index => "Index",
        SqliteSampleObjectKind.Trigger => "Trigger",
        SqliteSampleObjectKind.BuiltInFunctionExample => "Built-in functions",
        _ => Definition.Kind.ToString(),
    };

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
