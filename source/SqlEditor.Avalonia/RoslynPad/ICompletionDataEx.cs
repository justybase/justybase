
namespace JustyBase.Editor;

public interface ICompletionDataEx : ICompletionData
{
    bool IsSelected { get; }

    string SortText { get; }

    /// <summary>Text inserted when this completion is accepted.</summary>
    string InsertText { get; }
}

