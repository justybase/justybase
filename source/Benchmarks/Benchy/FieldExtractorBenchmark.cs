using BenchmarkDotNet.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Benchy;

[MemoryDiagnoser]
[SimpleJob]
public partial class FieldExtractorBenchmark
{
    private readonly string _testText = "Fields[12345]";
    private readonly Regex _fieldsRegex = Reg1();

    [Benchmark(Baseline = true)]
    public int StringMethod()
    {
        int start = _testText.IndexOf('[') + 1;
        int end = _testText.IndexOf(']');
        return int.Parse(_testText.Substring(start, end - start));
    }

    //[Benchmark]
    public int RegexMethod()
    {
        var match = _fieldsRegex.Match(_testText);
        return int.Parse(match.Groups[1].Value);
    }

    [Benchmark]
    public int StringMethodSpan()
    {
        ReadOnlySpan<char> span = _testText.AsSpan();
        int start = _testText.IndexOf('[') + 1;
        int end = _testText.IndexOf(']');
        return int.Parse(span.Slice(start, end - start));
    }

    [Benchmark]
    public int StringMethodSpan2()
    {
        ReadOnlySpan<char> span = _testText.AsSpan();
        int start = 7;
        int end = _testText.Length - 1;
        return int.Parse(span[start..end]);
    }

    [GeneratedRegex(@"Fields\[(\d+)\]", RegexOptions.Compiled)]
    private static partial Regex Reg1();
}
