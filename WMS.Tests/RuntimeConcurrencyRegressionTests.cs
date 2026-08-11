using Xunit;

namespace WMS.Tests;

public class RuntimeConcurrencyRegressionTests
{
    [Fact]
    public void OperationExceptionCenter_ShouldSerializeExceptionCaseSync()
    {
        var source = ReadRepoFile("Controllers", "OperationsController.ExceptionCenter.cs");

        Assert.Contains("OperationExceptionSyncLock", source, StringComparison.Ordinal);
        Assert.Contains("await OperationExceptionSyncLock.WaitAsync();", source, StringComparison.Ordinal);
        Assert.Contains("OperationExceptionSyncLock.Release();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LaborManagement_ShouldSerializeActivityCodeGenerationAndInsert()
    {
        var source = ReadRepoFile("Services", "LaborManagementService.cs");

        Assert.Contains("LaborActivityCaptureLock", source, StringComparison.Ordinal);
        Assert.Contains("await LaborActivityCaptureLock.WaitAsync(ct);", source, StringComparison.Ordinal);
        Assert.Contains("LaborActivityCaptureLock.Release();", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] relativeParts)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WMS.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Không tìm thấy WMS.sln từ thư mục test.");
    }
}
