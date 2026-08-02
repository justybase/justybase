using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(JustyBase.HeadlessTests.HeadlessAppSetup))]
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]
