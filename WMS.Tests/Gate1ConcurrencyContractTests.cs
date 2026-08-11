using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using WMS.Data;
using WMS.Models;

namespace WMS.Tests;

public sealed class Gate1ConcurrencyContractTests
{
    [Fact]
    public void VoucherUpdatedAt_ShouldBeConfiguredAsConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"gate1-model-{Guid.NewGuid():N}")
            .Options;
        using var db = new AppDbContext(options);

        var entity = db.Model.FindEntityType(typeof(Voucher));
        var property = entity?.FindProperty(nameof(Voucher.UpdatedAt));

        Assert.NotNull(property);
        Assert.True(property!.IsConcurrencyToken);
    }

    [Fact]
    public void ProblemDetails_ShouldMapEfConcurrencyConflictToVietnamese409()
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "AUDIT_TEST_GATE1_TRACE"
        };
        context.Request.Path = "/Vouchers/Update";

        var problem = WMS.Models.ProblemDetails.FromException(
            new DbUpdateConcurrencyException("AUDIT_TEST simulated conflict"),
            context);

        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("Xung đột cập nhật dữ liệu", problem.Title);
        Assert.Contains("tải lại trang", problem.Detail, StringComparison.Ordinal);
        Assert.Equal("DATA_CONCURRENCY_CONFLICT", problem.Extensions["code"]);
        Assert.Equal("AUDIT_TEST_GATE1_TRACE", problem.TraceId);
        Assert.Equal("/Vouchers/Update", problem.Instance);
    }

    [Fact]
    public void DatabaseTransactionFiles_ShouldNotCallExternalNetworkProviders()
    {
        var root = FindRepositoryRoot();
        var sourceRoots = new[] { "Controllers", "Data", "Services" };
        var transactionMethods = sourceRoots
            .Select(path => Path.Combine(root, path))
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            .Select(path => new { Path = path, Content = File.ReadAllText(path) })
            .SelectMany(file => ExtractMethodBodies(file.Content)
                .Where(method => method.Body.Contains("BeginTransaction", StringComparison.Ordinal))
                .Select(method => new { file.Path, Method = method.Name, method.Body }))
            .ToList();
        var externalCallTokens = new[]
        {
            "HttpClient",
            ".SendAsync(",
            ".PostAsync(",
            "SmtpClient",
            "SendMailAsync(",
            "GenerateContentAsync("
        };

        var offenders = transactionMethods
            .Where(method => externalCallTokens.Any(token => method.Body.Contains(token, StringComparison.Ordinal)))
            .Select(method => $"{Path.GetRelativePath(root, method.Path)}::{method.Method}")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.NotEmpty(transactionMethods);
        Assert.True(offenders.Count == 0,
            $"External provider calls must not run inside database transaction files: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void InventoryLedger_ShouldHaveNoRuntimeUpdateOrDeletePath()
    {
        var root = FindRepositoryRoot();
        var sourceRoots = new[] { "Controllers", "Data", "Services" };
        var forbiddenTokens = new[]
        {
            "InventoryTransactions.Update(",
            "InventoryTransactions.UpdateRange(",
            "InventoryTransactions.Remove(",
            "InventoryTransactions.RemoveRange(",
            "DELETE FROM InventoryTransactions",
            "DELETE FROM [InventoryTransactions]",
            "UPDATE InventoryTransactions",
            "UPDATE [InventoryTransactions]"
        };
        var offenders = sourceRoots
            .Select(path => Path.Combine(root, path))
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            .Select(path => new { Path = path, Content = File.ReadAllText(path) })
            .Where(file => forbiddenTokens.Any(token => file.Content.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .Select(file => Path.GetRelativePath(root, file.Path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Inventory ledger must be append-only outside controlled database maintenance: {string.Join(", ", offenders)}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WMS.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("WMS repository root was not found.");
    }

    private static IEnumerable<(string Name, string Body)> ExtractMethodBodies(string content)
    {
        var signaturePattern = new Regex(
            @"(?m)^\s*(?:public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?[^\r\n;{}=]+?\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\([^;{}]*\)\s*\{",
            RegexOptions.CultureInvariant);

        foreach (Match match in signaturePattern.Matches(content))
        {
            var openingBrace = content.IndexOf('{', match.Index + match.Length - 1);
            if (openingBrace < 0)
                continue;

            var depth = 0;
            for (var index = openingBrace; index < content.Length; index++)
            {
                if (content[index] == '{')
                    depth++;
                else if (content[index] == '}')
                    depth--;

                if (depth != 0)
                    continue;

                yield return (match.Groups["name"].Value, content[openingBrace..(index + 1)]);
                break;
            }
        }
    }
}
