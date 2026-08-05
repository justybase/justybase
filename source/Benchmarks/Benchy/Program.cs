using BenchmarkDotNet.Running;
using Benchy;

_ = BenchmarkRunner.Run<FieldExtractorBenchmark>();

Console.WriteLine("done");
