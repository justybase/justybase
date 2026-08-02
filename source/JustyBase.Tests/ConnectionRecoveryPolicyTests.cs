using System.Data;
using JustyBase.Services.Documents;

namespace JustyBase.Tests;

public class ConnectionRecoveryPolicyTests
{
    [Fact]
    public void MaxReconnectAttempts_IsOne()
    {
        Assert.Equal(1, ConnectionRecoveryPolicy.MaxReconnectAttempts);
    }

    [Theory]
    [InlineData("The Connection is broken.", ConnectionState.Open, true)]
    [InlineData("Timeout while getting a connection from pool.", ConnectionState.Closed, true)]
    [InlineData("ERROR: relation does not exist", ConnectionState.Open, false)]
    [InlineData("ERROR: Query was cancelled.", ConnectionState.Open, false)]
    public void IsBrokenConnection_DetectsKnownFailures(string message, ConnectionState state, bool expected)
    {
        var ex = new InvalidOperationException(message);
        Assert.Equal(expected, ConnectionRecoveryPolicy.IsBrokenConnection(ex, state));
    }

    [Fact]
    public void IsBrokenConnection_TrueWhenStateBroken()
    {
        Assert.True(ConnectionRecoveryPolicy.IsBrokenConnection(ex: null, ConnectionState.Broken));
    }

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(1, false, false)]
    [InlineData(0, true, false)]
    [InlineData(1, true, false)]
    public void CanAttemptReconnect_RespectsCancelAndCounter(int attemptsUsed, bool isCancelled, bool expected)
    {
        Assert.Equal(expected, ConnectionRecoveryPolicy.CanAttemptReconnect(attemptsUsed, isCancelled));
    }

    [Fact]
    public void RetryCounter_AllowsOnlyOneAttempt()
    {
        var attempts = 0;
        Assert.True(ConnectionRecoveryPolicy.CanAttemptReconnect(attempts, isCancelled: false));
        attempts++;
        Assert.False(ConnectionRecoveryPolicy.CanAttemptReconnect(attempts, isCancelled: false));
        Assert.Equal(ConnectionRecoveryPolicy.MaxReconnectAttempts, attempts);
    }
}
