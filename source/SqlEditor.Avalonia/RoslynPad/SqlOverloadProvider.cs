using System.ComponentModel;
using JustyBase.NetezzaSqlParser.Authoring;

namespace JustyBase.Editor;

public sealed class SqlOverloadProvider : IOverloadProviderEx, INotifyPropertyChanged
{
    private readonly IReadOnlyList<SqlSignatureInfo> _signatures;
    private int _selectedIndex;
    private readonly int _activeParameter;

    public SqlOverloadProvider(SqlSignatureHelpInfo signatureHelp)
    {
        _signatures = signatureHelp.Signatures;
        _selectedIndex = signatureHelp.ActiveSignature;
        _activeParameter = signatureHelp.ActiveParameter;
    }

    public int Count => _signatures.Count;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value == _selectedIndex || value < 0 || value >= Count)
                return;

            _selectedIndex = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedIndex)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentHeader)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentIndexText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentContent)));
            CurrentHeaderChanged?.Invoke(this, System.EventArgs.Empty);
        }
    }

    public object CurrentHeader => _signatures.Count == 0 ? string.Empty : _signatures[_selectedIndex].Label;

    public string CurrentIndexText => _signatures.Count == 0
        ? string.Empty
        : $"{_selectedIndex + 1} of {Count}";

    public object CurrentContent
    {
        get
        {
            if (_signatures.Count == 0)
                return string.Empty;

            var signature = _signatures[_selectedIndex];
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(signature.Documentation))
            {
                lines.Add(signature.Documentation!);
            }

            if (signature.Parameters.Length > 0)
            {
                if (lines.Count > 0)
                    lines.Add(string.Empty);

                for (int i = 0; i < signature.Parameters.Length; i++)
                {
                    var parameter = signature.Parameters[i];
                    var prefix = i == _activeParameter ? "> " : "  ";
                    var text = string.IsNullOrWhiteSpace(parameter.Documentation)
                        ? parameter.Label
                        : $"{parameter.Label} — {parameter.Documentation}";
                    lines.Add(prefix + text);
                }
            }

            return string.Join("\n", lines.Where(line => line is not null));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event System.EventHandler? CurrentHeaderChanged;

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentHeader)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentIndexText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentContent)));
        CurrentHeaderChanged?.Invoke(this, System.EventArgs.Empty);
    }
}
