using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Models;
using JustyBase.Services;
using JustyBase.Services.Documents;
using JustyBase.ViewModels.Tools;
using Moq;
using System.Data.Common;

namespace JustyBase.Tests;

/// <summary>
/// Regression tests for the data-loss vector behind "test connection hangs":
/// the old <c>TestConnectionAsync</c> mutated the live <c>LoginDataDic</c>
/// (temp entry + restore in finally), which raced with <c>SaveConfig</c> on
/// window close / unobserved-exception handler and could persist half-tested
/// credentials. The probe must leave saved connections untouched.
/// </summary>
public class AddNewConnectionViewModelTests
{
    private static LoginDataModel SavedConnection() => new()
    {
        ConnectionName = "SAVED",
        Driver = "NetezzaSQL",
        Server = "prod.example.com",
        Port = "5480",
        Database = "analytics",
        UserName = "analyst",
        Password = "s3cret",
    };

    private static AddNewConnectionViewModel CreateViewModel(
        Dictionary<string, LoginDataModel> loginDic,
        Mock<IDatabaseServiceResolver> resolver,
        Mock<IDatabaseService>? service = null)
    {
        var appData = new Mock<IGeneralApplicationData>();
        appData.SetupGet(x => x.LoginDataDic).Returns(loginDic);
        appData.Setup(x => x.GetDataDir()).Returns(Path.GetTempPath());
        appData.SetupGet(x => x.GlobalLoggerObject).Returns(ISimpleLogger.EmptyLogger);

        if (service is not null)
        {
            resolver.Setup(r => r.CreateTransientService(
                    It.IsAny<IGeneralApplicationData>(),
                    It.IsAny<LoginDataModel>(),
                    It.IsAny<int>()))
                .Returns(service.Object);
        }

        return new AddNewConnectionViewModel(
            Mock.Of<IFactory>(),
            appData.Object,
            Mock.Of<IMessageForUserTools>(),
            ISimpleLogger.EmptyLogger,
            Mock.Of<IAvaloniaSpecificHelpers>(),
            resolver.Object);
    }

    private static Mock<IDatabaseService> WorkingService()
    {
        var connection = new Mock<DbConnection>();
        connection.Setup(c => c.OpenAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new Mock<IDatabaseService>();
        service.Setup(s => s.GetConnection(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(connection.Object);
        return service;
    }

    private static void FillProbeForm(AddNewConnectionViewModel vm)
    {
        vm.SelectedDriver = vm.DriversList.First(d => d.Id == "NetezzaSQL");
        vm.ConName = "PROBE_NEW";
        vm.Server = "db.example.com";
        vm.Port = "5480";
        vm.Database = "mydb";
        vm.UserName = "user";
        vm.Pass = "pw";
    }

    [Fact]
    public async Task TestConnection_DoesNotMutateSavedConnections()
    {
        var saved = SavedConnection();
        var loginDic = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase)
        {
            ["SAVED"] = saved,
        };
        var resolver = new Mock<IDatabaseServiceResolver>();
        AddNewConnectionViewModel vm = CreateViewModel(loginDic, resolver, WorkingService());
        FillProbeForm(vm);
        Assert.True(vm.CanTest);

        await ((AsyncRelayCommand)vm.TestConnectionCommand).ExecuteAsync(null);

        Assert.True(vm.ConnectionTestIsSuccess);
        Assert.Equal("Connection successful", vm.ConnectionTestStatus);
        Assert.False(vm.IsTestingConnection);

        // The probe connection must never appear in the saved dictionary ...
        Assert.Single(loginDic);
        Assert.False(loginDic.ContainsKey("PROBE_NEW"));

        // ... and the pre-existing entry must be the same untouched instance.
        Assert.Same(saved, loginDic["SAVED"]);
        Assert.Equal("prod.example.com", saved.Server);
        Assert.Equal("s3cret", saved.Password);

        // The probe goes through the transient (non-cached) path, never the
        // shared registry that fans out background schema loads.
        resolver.Verify(r => r.CreateTransientService(
            It.IsAny<IGeneralApplicationData>(),
            It.IsAny<LoginDataModel>(),
            It.IsAny<int>()), Times.Once);
        resolver.Verify(r => r.GetDatabaseService(
            It.IsAny<IGeneralApplicationData>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<Action<string>?>()), Times.Never);
        resolver.Verify(r => r.RemoveCachedConnection(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TestConnection_FailedProbe_DoesNotMutateSavedConnections()
    {
        var saved = SavedConnection();
        var loginDic = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase)
        {
            ["SAVED"] = saved,
        };

        var connection = new Mock<DbConnection>();
        connection.Setup(c => c.OpenAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("host unreachable"));
        var service = new Mock<IDatabaseService>();
        service.Setup(s => s.GetConnection(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(connection.Object);

        var resolver = new Mock<IDatabaseServiceResolver>();
        AddNewConnectionViewModel vm = CreateViewModel(loginDic, resolver, service);
        FillProbeForm(vm);

        await ((AsyncRelayCommand)vm.TestConnectionCommand).ExecuteAsync(null);

        Assert.False(vm.ConnectionTestIsSuccess);
        Assert.Equal("Connection failed", vm.ConnectionTestStatus);
        Assert.False(vm.IsTestingConnection);
        Assert.True(vm.HasConnectionTestResult);

        Assert.Single(loginDic);
        Assert.Same(saved, loginDic["SAVED"]);
    }

    [Fact]
    public void CloneConnection_RoutesDriverOptionsThroughService_NotLiveObjects()
    {
        // Regression test for A.1: driver options must go through
        // IGeneralApplicationData.SetAccessOptions, never by mutating objects
        // obtained from LoginDataDic (which is now a snapshot copy).
        var loginDic = new Dictionary<string, LoginDataModel>(StringComparer.OrdinalIgnoreCase);
        var appData = new Mock<IGeneralApplicationData>();
        appData.SetupGet(x => x.LoginDataDic).Returns(loginDic);
        var factory = new Mock<IFactory>();
        factory
            .Setup(f => f.Find(It.IsAny<Func<IDockable, bool>>()))
            .Returns(Enumerable.Empty<IDockable>());

        var vm = new AddNewConnectionViewModel(
            factory.Object,
            appData.Object,
            Mock.Of<IMessageForUserTools>(),
            ISimpleLogger.EmptyLogger,
            Mock.Of<IAvaloniaSpecificHelpers>(),
            Mock.Of<IDatabaseServiceResolver>());
        vm.SelectedDriver = vm.DriversList.First(d => d.Id == "Access");
        vm.ConName = "MYACCESS";
        vm.Database = @"C:\data\db.accdb";
        Assert.True(vm.CanSave);

        vm.CloneConnectionCommand.Execute(null);

        appData.Verify(x => x.SetAccessOptions(
            "MYACCESS_CLONE",
            It.Is<AccessConnectionOptions>(o => o.ReadOnly == true)), Times.Once);
    }
}
