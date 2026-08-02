namespace JustyBase.Common.Models;

public enum HistoryRunStatus
{
    Unknown = 0,
    Success = 1,
    Failed = 2,
    Cancelled = 3,
}

public enum HistoryDurationPreset
{
    All = 0,
    Under1s = 1,
    From1To10s = 2,
    From10sTo1min = 3,
    Over1min = 4,
}
