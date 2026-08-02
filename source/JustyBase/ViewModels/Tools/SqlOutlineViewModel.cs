using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using JustyBase.Services;

namespace JustyBase.ViewModels.Tools;

public sealed partial class SqlOutlineViewModel : Tool
{
    private readonly AvaloniaList<SqlOutlineItem> _items = new();

    public IReadOnlyList<SqlOutlineItem> Items => _items;

    public Action<int>? NavigateToOffset { get; set; }

    [ObservableProperty]
    public partial SqlOutlineItem? SelectedItem { get; set; }

    partial void OnSelectedItemChanged(SqlOutlineItem? value)
    {
        if (value is not null)
            NavigateToOffset?.Invoke(value.StartOffset);
    }

    public void UpdateOutline(string sql)
    {
        _items.Clear();
        foreach (var entry in SqlOutlineBuilder.Build(sql))
        {
            var indent = new string(' ', entry.Depth * 2);
            _items.Add(new SqlOutlineItem($"{indent}{entry.Title}", entry.Kind, entry.StartOffset));
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        if (GetCurrentSql is not null)
            UpdateOutline(GetCurrentSql());
    }

    public Func<string>? GetCurrentSql { get; set; }
}

public sealed record SqlOutlineItem(string Title, string Kind, int StartOffset);
