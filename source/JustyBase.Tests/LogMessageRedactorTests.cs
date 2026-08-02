using JustyBase.Services.Logging;

namespace JustyBase.Tests;

public sealed class LogMessageRedactorTests
{
    [Theory]
    [InlineData("Password=Secret123", "Password=***")]
    [InlineData("password=Secret123", "password=***")]
    [InlineData("PWD=Secret123", "PWD=***")]
    [InlineData("pwd=Secret123", "pwd=***")]
    [InlineData("Pass=Secret123", "Pass=***")]
    public void Redact_PasswordAssignments_ReplacesValue(string input, string expected)
    {
        Assert.Equal(expected, LogMessageRedactor.Redact(input));
    }

    [Fact]
    public void Redact_ConnectionStringWithPassword_RedactsPassword()
    {
        var input = "Server=db.example;Database=app;User Id=sa;Password=SuperSecret!;TrustServerCertificate=true";
        var result = LogMessageRedactor.Redact(input);

        Assert.DoesNotContain("SuperSecret!", result);
        Assert.Contains("Password=***", result);
        Assert.Contains("Server=db.example", result);
    }

    [Fact]
    public void Redact_ConnectionStringAssignment_RedactsEntireValue()
    {
        var input = @"ConnectionString=Server=localhost;Password=abc;Database=x";
        var result = LogMessageRedactor.Redact(input);

        Assert.Equal("ConnectionString=***", result);
        Assert.DoesNotContain("abc", result);
    }

    [Fact]
    public void Redact_QuotedConnectionStringAssignment_RedactsValue()
    {
        var input = @"Connection String=""Server=localhost;Password=abc;Database=x""";
        var result = LogMessageRedactor.Redact(input);

        Assert.Equal("Connection String=***", result);
        Assert.DoesNotContain("abc", result);
    }

    [Fact]
    public void Redact_JsonPassword_RedactsValue()
    {
        var input = """{"User":"admin","Password":"hunter2","Host":"db"}""";
        var result = LogMessageRedactor.Redact(input);

        Assert.DoesNotContain("hunter2", result);
        Assert.Contains("\"Password\":\"***\"", result);
        Assert.Contains("\"User\":\"admin\"", result);
    }

    [Fact]
    public void Redact_QuotedPasswordAssignment_RedactsQuotedValue()
    {
        var input = @"Password=""p@ss;word""";
        var result = LogMessageRedactor.Redact(input);

        Assert.Equal("Password=***", result);
        Assert.DoesNotContain("p@ss;word", result);
    }

    [Fact]
    public void Redact_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, LogMessageRedactor.Redact(null));
        Assert.Equal(string.Empty, LogMessageRedactor.Redact(string.Empty));
    }

    [Fact]
    public void Redact_NonSensitiveText_Unchanged()
    {
        const string input = "Failed to open table CUSTOMER_ORDERS";
        Assert.Equal(input, LogMessageRedactor.Redact(input));
    }
}
