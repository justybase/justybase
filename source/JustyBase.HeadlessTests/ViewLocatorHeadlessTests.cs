using Avalonia.Controls;
using JustyBase;
using JustyBase.Services;
using JustyBase.ViewModels.Tools;
using JustyBase.Views.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace JustyBase.HeadlessTests;

public sealed class ViewLocatorHeadlessTests : HeadlessSessionTestBase
{
    [Fact]
    public async Task Build_KnownViewModel_CreatesExplicitlyRegisteredView()
    {
        await RunOnUi(() =>
        {
            using var services = new ServiceCollection().BuildServiceProvider();
            var locator = new ViewLocator(services);

            Control view = locator.Build(new SqlDiagnosticsViewModel());

            Assert.IsType<SqlDiagnosticsView>(view);
        });
    }

    [Fact]
    public async Task Build_UnknownModel_ReturnsReadableFallback()
    {
        await RunOnUi(() =>
        {
            using var services = new ServiceCollection().BuildServiceProvider();
            var locator = new ViewLocator(services);

            var view = Assert.IsType<TextBlock>(locator.Build(new object()));

            Assert.Contains("No view registered", view.Text);
        });
    }
}
