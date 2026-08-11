using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using WMS.Common;
using WMS.Authorization;
using WMS.Controllers;
using WMS.Data;
using WMS.Models;

namespace WMS.Tests;

public sealed class Gate2SecurityContractTests
{
    [Fact]
    public void UnsafeBusinessActions_ShouldHaveRoleOrPermissionGuardBeyondGlobalAuthentication()
    {
        var personalAccountActions = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(AccountController.Logout),
            nameof(AccountController.RevokeCurrentTrustedDevice),
            nameof(AccountController.RevokeAllTrustedDevices)
        };

        var missingGuards = typeof(HomeController).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(controller => controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(IsActionMethod)
                .Where(IsUnsafeHttpAction)
                .Select(action => new { Controller = controller, Action = action }))
            .Where(entry => !IsAnonymous(entry.Controller, entry.Action))
            .Where(entry => entry.Controller != typeof(AccountController)
                || !personalAccountActions.Contains(entry.Action.Name))
            .Where(entry => !HasBusinessAuthorization(entry.Controller, entry.Action))
            .Select(entry => $"{entry.Controller.Name}.{entry.Action.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missingGuards.Length == 0,
            "Unsafe actions missing role/permission guard:" + Environment.NewLine + string.Join(Environment.NewLine, missingGuards));
    }

    [Fact]
    public async Task DirectEntityCreateActions_ShouldOverrideIdentityStatusAuditAndNavigationFields()
    {
        await using var db = CreateDb(nameof(DirectEntityCreateActions_ShouldOverrideIdentityStatusAuditAndNavigationFields));
        var forgedAt = new DateTime(2000, 1, 1);

        var categoriesController = AttachHttp(new CategoriesController(db));
        var categoryResult = await categoriesController.Create(new ItemCategory
        {
            CategoryId = 999,
            CategoryCode = " AUDIT_TEST_CATEGORY ",
            CategoryName = " Audit category ",
            IsActive = false,
            CreatedAt = forgedAt,
            UpdatedAt = forgedAt,
            ChildCategories = new List<ItemCategory> { new() { CategoryCode = "FORGED", CategoryName = "Forged" } }
        });
        Assert.IsType<RedirectToActionResult>(categoryResult);

        var partnersController = AttachHttp(new PartnersController(db));
        var partnerResult = await partnersController.Create(new Partner
        {
            PartnerId = 999,
            PartnerCode = " AUDIT_TEST_PARTNER ",
            PartnerName = " Audit partner ",
            IsActive = false,
            CreatedAt = forgedAt,
            UserOwnerScopes = new List<AppUserOwnerScope> { new() { UserId = 999, OwnerPartnerId = 999 } }
        });
        Assert.IsType<RedirectToActionResult>(partnerResult);

        var category = await db.ItemCategories.SingleAsync(x => x.CategoryCode == "AUDIT_TEST_CATEGORY");
        Assert.NotEqual(999, category.CategoryId);
        Assert.True(category.IsActive);
        Assert.True(category.CreatedAt > forgedAt);
        Assert.Null(category.UpdatedAt);
        Assert.Empty(category.ChildCategories);

        var partner = await db.Partners.SingleAsync(x => x.PartnerCode == "AUDIT_TEST_PARTNER");
        Assert.NotEqual(999, partner.PartnerId);
        Assert.True(partner.IsActive);
        Assert.True(partner.CreatedAt > forgedAt);
        Assert.Empty(partner.UserOwnerScopes);
    }

    [Theory]
    [InlineData("Server=db.internal;Password=top-secret")]
    [InlineData("C:\\private\\documents\\receipt.pdf")]
    [InlineData("ApiKey=not-for-client")]
    [InlineData("/var/app/private/file")]
    public void UserSafeError_ShouldRedactConnectionSecretAndInternalPathShapes(string unsafeMessage)
    {
        var result = UserSafeError.From(new InvalidOperationException(unsafeMessage));

        Assert.Equal(UserSafeError.GenericMessage, result);
        Assert.DoesNotContain(unsafeMessage, result, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultJsonEncoding_ShouldNeutralizeScriptBreakingCharactersUsedByRazorJsonPayloads()
    {
        var payload = JsonSerializer.Serialize("</script><script>alert('xss')</script>");

        Assert.DoesNotContain("</script>", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\\u003C", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DevelopmentExceptionPage_ShouldRequireExplicitDiagnosticOptIn()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Program.cs"));

        Assert.Contains("Diagnostics:ExposeDeveloperExceptionPage", program, StringComparison.Ordinal);
        Assert.Contains("UserSafeError.From(exception", program, StringComparison.Ordinal);
    }

    private static bool IsActionMethod(MethodInfo method)
    {
        if (method.IsSpecialName || method.GetCustomAttribute<NonActionAttribute>(true) != null)
            return false;

        return typeof(IActionResult).IsAssignableFrom(method.ReturnType)
            || (method.ReturnType.IsGenericType
                && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)
                && typeof(IActionResult).IsAssignableFrom(method.ReturnType.GenericTypeArguments[0]));
    }

    private static bool IsUnsafeHttpAction(MethodInfo method)
        => method.GetCustomAttribute<HttpPostAttribute>(true) != null
            || method.GetCustomAttribute<HttpPutAttribute>(true) != null
            || method.GetCustomAttribute<HttpPatchAttribute>(true) != null
            || method.GetCustomAttribute<HttpDeleteAttribute>(true) != null;

    private static bool IsAnonymous(Type controller, MethodInfo action)
        => controller.GetCustomAttribute<AllowAnonymousAttribute>(true) != null
            || action.GetCustomAttribute<AllowAnonymousAttribute>(true) != null;

    private static bool HasBusinessAuthorization(Type controller, MethodInfo action)
    {
        var usesApiKeyGuard = controller.GetCustomAttribute<ApiKeyAllowAnonymousAttribute>(true) != null
            || action.GetCustomAttribute<ApiKeyAllowAnonymousAttribute>(true) != null;
        if (usesApiKeyGuard)
            return true;

        return controller.GetCustomAttributes<AuthorizeAttribute>(true)
            .Concat(action.GetCustomAttributes<AuthorizeAttribute>(true))
            .Any(attribute => !string.IsNullOrWhiteSpace(attribute.Policy)
                || !string.IsNullOrWhiteSpace(attribute.Roles));
    }

    private static AppDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"{name}-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options) { SkipAudit = true };
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "WMS.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Cannot find repository root.");
    }

    private static T AttachHttp<T>(T controller) where T : Controller
    {
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());
        return controller;
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
