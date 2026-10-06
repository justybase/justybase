using Dock.Model.Core;
using JustyBase.ViewModels.Tools;
using Moq;

namespace JustyBase.Tests;

public sealed class SqlOutlineViewModelTests
{
    private static SqlOutlineViewModel CreateVm()
    {
        var vm = new SqlOutlineViewModel(Mock.Of<IFactory>());
        vm.NavigateToOffset = _ => Assert.Fail("FollowCaret must highlight without navigating.");
        return vm;
    }

    private static SqlOutlineItem FindByTitle(SqlOutlineViewModel vm, string title)
    {
        var stack = new Stack<SqlOutlineItem>(vm.Items.Reverse());
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.Title == title)
                return current;
            foreach (var child in current.Children.Reverse())
                stack.Push(child);
        }

        throw new InvalidOperationException($"Outline item '{title}' not found.");
    }

    [Fact]
    public void FollowCaret_SelectsDeepestSymbolWithoutNavigating()
    {
        var vm = CreateVm();
        vm.UpdateOutline("WITH CTE1 AS (SELECT 1 AS a), CTE2 AS (SELECT 2 AS b) SELECT * FROM CTE1, CTE2;");
        Assert.NotEmpty(vm.Items);

        var cte2 = FindByTitle(vm, "CTE2");

        vm.FollowCaret(0);
        Assert.Equal("SELECT #1", vm.SelectedItem?.Title);

        vm.FollowCaret(cte2.StartOffset);
        Assert.Same(cte2, vm.SelectedItem);
    }

    [Fact]
    public void FollowCaret_InsideSameSymbol_KeepsSelection()
    {
        var vm = CreateVm();
        vm.UpdateOutline("WITH CTE1 AS (SELECT 1 AS a), CTE2 AS (SELECT 2 AS b) SELECT * FROM CTE1, CTE2;");

        var cte2 = FindByTitle(vm, "CTE2");
        vm.FollowCaret(cte2.StartOffset);
        Assert.Same(cte2, vm.SelectedItem);

        vm.FollowCaret(cte2.StartOffset + 2);
        Assert.Same(cte2, vm.SelectedItem);
    }
}
