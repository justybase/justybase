namespace JustyBase.ViewModels.Tools;

public sealed class RowDetail
{
    public string Name { get; set; } = string.Empty;

    public bool IsColumnVisible
    {
        get;
        set
        {
            field = value;
            ChangeColVisiblity?.Invoke();
        }
    } = true;
    public List<string> FieldsValues { get; set; } = [];
    public string TypeName { get; set; } = string.Empty;
    public Action? ChangeColVisiblity { get; set; }
}
