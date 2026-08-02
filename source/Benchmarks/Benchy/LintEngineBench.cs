using BenchmarkDotNet.Attributes;
using JustyBase.NetezzaSqlParser.Linter;

namespace Benchy;

[MemoryDiagnoser]
public class LintEngineBench
{
    private LintEngine _engine = null!;
    private string _smallSql = string.Empty;
    private string _largeSql = string.Empty;
    private const string SmallSelectStar = "SELECT * FROM users WHERE id = 1";
    private const string SmallUpdateNoWhere = "UPDATE users SET name = 'test'";
    private const string SmallDeleteNoWhere = "DELETE FROM users";

    [GlobalSetup]
    public void Setup()
    {
        _engine = new LintEngine();

        // Build a large SQL with many patterns for expensive rules to detect
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            sb.AppendLine($"SELECT * FROM table_{i} WHERE col_{i} = {i};");
            sb.AppendLine($"UPDATE table_{i} SET col_{i} = {i};");
            sb.AppendLine($"DELETE FROM table_{i};");
            sb.AppendLine($"SELECT t1.*, t2.* FROM table_{i} t1, table_{i + 1} t2 WHERE t1.id = t2.id;");
            sb.AppendLine($"INSERT INTO table_{i} VALUES ({i});");
        }
        _largeSql = sb.ToString();
        _smallSql = "SELECT * FROM users WHERE id = 1;\nUPDATE users SET name = 'test';\nDELETE FROM users;";
    }

    [Benchmark(Baseline = true)]
    public int RunCheapRules_Small()
    {
        return _engine.RunCheapRules(SmallSelectStar).Count;
    }

    [Benchmark]
    public int RunCheapRules_Small_Multiple()
    {
        return _engine.RunCheapRules(_smallSql).Count;
    }

    [Benchmark]
    public int RunCheapRules_Large_100Statements()
    {
        return _engine.RunCheapRules(_largeSql).Count;
    }

    [Benchmark]
    public int RunCheapRules_WithSeverityOverride()
    {
        _engine.Registry.SetSeverity("NZ001", RuleSeverityConfig.Off);
        var count = _engine.RunCheapRules(SmallSelectStar).Count;
        _engine.Registry.ResetSeverities();
        return count;
    }

    [Benchmark]
    public int RunCheapRules_WithPriorityOverride()
    {
        _engine.Registry.SetPriority("NZ001", 50);
        var count = _engine.RunCheapRules(SmallSelectStar).Count;
        _engine.Registry.ResetPriorities();
        return count;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _engine.Dispose();
    }
}
