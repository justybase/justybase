using JustyBase.Services.DataGrid;
using System;
using System.Collections.Generic;

namespace JustyBase.Tests;

public sealed class ResultGridGroupExpandCollapseServiceTests
{
    [Fact]
    public void TryCommitPendingEdit_WhenActionSucceeds_ReturnsTrueWithoutErrors()
    {
        var service = new ResultGridGroupExpandCollapseService();
        var errors = new List<Exception>();
        bool commitCalled = false;

        bool result = service.TryCommitPendingEdit(
            () => commitCalled = true,
            errors.Add);

        Assert.True(result);
        Assert.True(commitCalled);
        Assert.Empty(errors);
    }

    [Fact]
    public void TryCommitPendingEdit_WhenInvalidOperationIsThrown_ReturnsFalseAndReportsError()
    {
        var service = new ResultGridGroupExpandCollapseService();
        var errors = new List<Exception>();

        bool result = service.TryCommitPendingEdit(
            () => throw new InvalidOperationException("commit failed"),
            errors.Add);

        Assert.False(result);
        Assert.IsType<InvalidOperationException>(Assert.Single(errors));
    }

    [Fact]
    public void TryExecuteGroupOperation_WhenCollapseRequested_InvokesCollapseAction()
    {
        var service = new ResultGridGroupExpandCollapseService();
        var errors = new List<Exception>();
        bool collapseCalled = false;
        bool expandCalled = false;

        bool result = service.TryExecuteGroupOperation(
            ResultGridGroupOperation.Collapse,
            () => collapseCalled = true,
            () => expandCalled = true,
            errors.Add);

        Assert.True(result);
        Assert.True(collapseCalled);
        Assert.False(expandCalled);
        Assert.Empty(errors);
    }

    [Fact]
    public void TryExecuteGroupOperation_WhenExpandRequested_InvokesExpandAction()
    {
        var service = new ResultGridGroupExpandCollapseService();
        var errors = new List<Exception>();
        bool collapseCalled = false;
        bool expandCalled = false;

        bool result = service.TryExecuteGroupOperation(
            ResultGridGroupOperation.Expand,
            () => collapseCalled = true,
            () => expandCalled = true,
            errors.Add);

        Assert.True(result);
        Assert.False(collapseCalled);
        Assert.True(expandCalled);
        Assert.Empty(errors);
    }

    [Fact]
    public void TryExecuteGroupOperation_WhenObjectDisposedIsThrown_ReturnsFalseAndReportsError()
    {
        var service = new ResultGridGroupExpandCollapseService();
        var errors = new List<Exception>();
        bool collapseCalled = false;
        bool expandCalled = false;

        bool result = service.TryExecuteGroupOperation(
            ResultGridGroupOperation.Expand,
            () => collapseCalled = true,
            () =>
            {
                expandCalled = true;
                throw new ObjectDisposedException("ResultDataGrid");
            },
            errors.Add);

        Assert.False(result);
        Assert.False(collapseCalled);
        Assert.True(expandCalled);
        Assert.IsType<ObjectDisposedException>(Assert.Single(errors));
    }

    [Fact]
    public void TryExecuteGroupOperation_WhenOperationIsUnknown_ThrowsArgumentOutOfRangeException()
    {
        var service = new ResultGridGroupExpandCollapseService();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.TryExecuteGroupOperation(
                (ResultGridGroupOperation)123,
                () => { },
                () => { },
                _ => { }));
    }
}
