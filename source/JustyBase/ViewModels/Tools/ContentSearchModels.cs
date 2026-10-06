using System.ComponentModel;
using JustyBase.Services;

namespace JustyBase.ViewModels.Tools;

/// <summary>
/// One file in the Files search results with its line hits.
/// Shared presentation model for the Files panel results tree.
/// </summary>
public sealed class ContentSearchFileGroup : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isExpanded = true;

    public ContentSearchFileGroup(ContentSearchFile file)
    {
        File = file;
        Hits = file.Hits.Select(hit => new ContentSearchHitRow(hit)).ToArray();
    }

    public ContentSearchFile File { get; }
    public string Name => Path.GetFileName(File.Path);
    public string RelativePath => File.RelativePath;

    /// <summary>
    /// Parent directory of the relative path (VS Code shows name + folder).
    /// Empty for files directly in a search root, so the name is not duplicated.
    /// </summary>
    public string RelativeDir
    {
        get
        {
            var dir = Path.GetDirectoryName(RelativePath);
            return string.IsNullOrEmpty(dir) || dir == "." ? "" : dir;
        }
    }
    public int Count => File.Hits.Count;
    public IReadOnlyList<ContentSearchHitRow> Hits { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }
    }

    public ContentSearchFileGroup Initialize()
    {
        IsExpanded = true;
        foreach (var hit in Hits)
        {
            hit.Group = this;
        }
        return this;
    }
}

public sealed class ContentSearchHitRow(ContentSearchHit hit)
{
    public ContentSearchHit Hit { get; } = hit;
    public ContentSearchFileGroup Group { get; set; } = null!;
    public int Line => Hit.Line;
    public string Before => Hit.Before;
    public string Match => Hit.Match;
    public string After => Hit.After;
}
