namespace JustyBase.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void ViewModels_DoNotReference_WinForms()
    {
        var viewModelAssembly = typeof(ViewModels.Documents.SqlDocumentViewModel).Assembly;
        var references = viewModelAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, r => r.Name == "System.Windows.Forms");
    }

    [Fact]
    public void Host_DoesNotReference_HogimnSqlFormatter()
    {
        var hostAssembly = typeof(GeneralApplicationData).Assembly;
        var references = hostAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, r => r.Name == "Hogimn.Sql.Formatter");
    }
}
