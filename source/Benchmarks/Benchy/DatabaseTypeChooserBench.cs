using BenchmarkDotNet.Attributes;
using JustyBase.Common.Tools.ImportHelpers;
using System.Text;

namespace Benchy;

[MemoryDiagnoser]
public class DatabaseTypeChooserBench
{
    public string Path { get; set; } = @"D:\DEV\sqls\CsvReader\200kFile.csv";

    [Benchmark]
    public void Met1()
    {
        DatabaseTypeChooser databaseTypeChooser = new DatabaseTypeChooser();

        using var excelReader = new CsvReader();
        excelReader.Open(Path, true, encoding: Encoding.UTF8);
        databaseTypeChooser.ExcelTypeDetection(excelReader, "xyz");
    }
}
