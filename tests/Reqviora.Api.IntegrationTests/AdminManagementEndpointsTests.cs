
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class AdminManagementEndpointsTests
{
    [Theory]
    [InlineData("/api/admin/users/search")]
    [InlineData("/api/admin/roles/list")]
    [InlineData("/api/admin/projects/search")]
    [InlineData("/api/admin/audit/search")]
    [InlineData("/api/admin/security/login-summary")]
    [InlineData("/api/admin/system/report")]
    [InlineData("/api/admin/system/health")]
    public async Task Anonymous_access_is_rejected(
        string endpoint)
    {
        using var factory = new AdminTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            endpoint,
            new { code = "123456" });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Theory]
    [InlineData("/api/admin/users/search")]
    [InlineData("/api/admin/roles/list")]
    [InlineData("/api/admin/projects/search")]
    [InlineData("/api/admin/audit/search")]
    [InlineData("/api/admin/security/login-summary")]
    [InlineData("/api/admin/system/report")]
    [InlineData("/api/admin/system/health")]
    public async Task Invalid_JWT_is_rejected(
        string endpoint)
    {
        using var factory = new AdminTestFactory();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers
                .AuthenticationHeaderValue(
                    "Bearer",
                    "invalid-jwt-token");

        using var response = await client.PostAsJsonAsync(
            endpoint,
            new { code = "123456" });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    private sealed class AdminTestFactory
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.UseSetting(
                "ConnectionStrings:ReqvioraDatabase",
                "Host=localhost;Database=unused");

            builder.UseSetting(
                "Jwt:Key",
                "admin-management-test-key-at-least-32-characters");

            builder.UseSetting(
                "Security:AuditEnabled",
                "false");
        }
    }
}
