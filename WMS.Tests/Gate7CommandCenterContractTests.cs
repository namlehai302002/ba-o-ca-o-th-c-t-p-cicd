using System.Reflection;
using Microsoft.EntityFrameworkCore;
using WMS.Controllers;
using WMS.Data;
using WMS.Models;

namespace WMS.Tests;

public sealed class Gate7CommandCenterContractTests
{
    [Fact]
    public void OperationExceptionUpdatedAt_ShouldBeConfiguredAsConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"gate7-exception-model-{Guid.NewGuid():N}")
            .Options;
        using var db = new AppDbContext(options);

        var entity = db.Model.FindEntityType(typeof(OperationExceptionCase));
        var property = entity?.FindProperty(nameof(OperationExceptionCase.UpdatedAt));

        Assert.NotNull(property);
        Assert.True(property!.IsConcurrencyToken);
    }

    [Fact]
    public void ExceptionKeyNormalization_ShouldBeStableAndFitDatabaseContract()
    {
        var method = typeof(OperationsController).GetMethod(
            "NormalizeExceptionKey",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var source = "  AUDIT_TEST_GATE7|" + new string('X', 300) + "  ";
        var first = Assert.IsType<string>(method!.Invoke(null, new object[] { source }));
        var second = Assert.IsType<string>(method.Invoke(null, new object[] { source }));

        Assert.Equal(first, second);
        Assert.Equal(200, first.Length);
        Assert.StartsWith("AUDIT_TEST_GATE7|", first, StringComparison.Ordinal);
        Assert.Contains('|', first[135..]);
    }

    [Fact]
    public void ExceptionKeyNormalization_ShouldOnlyTrimKeysWithinLimit()
    {
        var method = typeof(OperationsController).GetMethod(
            "NormalizeExceptionKey",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var normalized = Assert.IsType<string>(method!.Invoke(
            null,
            new object[] { "  AUDIT_TEST_GATE7_SHORT_KEY  " }));

        Assert.Equal("AUDIT_TEST_GATE7_SHORT_KEY", normalized);
    }
}
