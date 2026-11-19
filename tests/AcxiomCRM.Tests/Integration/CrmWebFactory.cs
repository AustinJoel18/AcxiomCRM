using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AcxiomCRM.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AcxiomCRM.Tests.Integration;

/// <summary>Boots the real application (Testing environment) against a throw-away SQLite database seeded with demo data.</summary>
public sealed class CrmWebFactory : WebApplicationFactory<Program>
{
    public const string Password = "Test@Pass123";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"acxiomcrm-test-{Guid.NewGuid():N}.db");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={_dbPath}");
        builder.UseSetting("Seed:DemoData", "true");
        builder.UseSetting("Seed:DemoPassword", Password);
        builder.UseSetting("Security:LoginRateLimitPerMinute", "10000");
        builder.UseSetting("App:EnableApiDocs", "true");
    }

    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public async Task<string> TokenAsync(string email, string password = Password)
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { userNameOrEmail = email, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("accessToken").GetString()!;
    }

    public async Task<HttpClient> ApiAsync(string email)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(email));
        return client;
    }

    /// <summary>Signs in through the MVC login form (anti-forgery token included) and returns the cookie-holding client.</summary>
    public async Task<HttpClient> SignedInBrowserAsync(string email, string password = Password)
    {
        var client = Browser();
        var token = await AntiforgeryTokenAsync(client, "/Account/Login");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserNameOrEmail"] = email, ["Password"] = password, ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    public static async Task<string> AntiforgeryTokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(match.Success, $"No anti-forgery token on {path}");
        return match.Groups[1].Value;
    }

    public T Db<T>(Func<ApplicationDbContext, T> query)
    {
        using var scope = Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            try { File.Delete(f); } catch (IOException) { }
    }
}

public static class Users
{
    public const string Admin = "admin@acxiomcrm.local";
    public const string Manager = "manager@acxiomcrm.local";   // team: sales, sales2
    public const string Sales = "sales@acxiomcrm.local";       // Vikram Singh
    public const string Sales2 = "sales2@acxiomcrm.local";
    public const string Sales3 = "sales3@acxiomcrm.local";     // other team
    public const string Sales4 = "sales4@acxiomcrm.local";
}
