using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tesria.Api.Features.Admin;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>Administration, About: the version, the dependencies, and the vulnerability check.</summary>
public class AboutTests
{
    private record Dep(string Ecosystem, string Name, string Version, string License, string Component);
    private record Vuln(string Id, string? Summary, string? Severity);
    private record Affected(string Name, string Version, List<string> Components, List<Vuln> Vulnerabilities);
    private record Check(int Checked, List<Affected> Affected, string? ByName);
    private record About(string Version, List<Dep> Dependencies, Check? LastCheck);

    /// <summary>Says the first package asked about has one vulnerability, and remembers what it was asked.</summary>
    private sealed class FakeOsv : IOsvClient
    {
        public List<OsvPackage> Asked = [];
        public bool Fail;
        public Task<IReadOnlyList<IReadOnlyList<AboutEndpoints.Vulnerability>>> QueryAsync(IReadOnlyList<OsvPackage> packages, CancellationToken ct)
        {
            if (Fail) throw new HttpRequestException("offline");
            Asked = [.. packages];
            return Task.FromResult<IReadOnlyList<IReadOnlyList<AboutEndpoints.Vulnerability>>>(
                [.. packages.Select((p, i) => (IReadOnlyList<AboutEndpoints.Vulnerability>)(i == 0
                    ? [new AboutEndpoints.Vulnerability("GHSA-test-0001", "A test vulnerability", "HIGH", ["CVE-2026-0001"], "https://osv.dev/vulnerability/GHSA-test-0001")]
                    : []))]);
        }
    }

    private static (WebApplicationFactoryHandle App, FakeOsv Osv) Start()
    {
        var osv = new FakeOsv();
        var factory = new TestAppFactory();
        var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IOsvClient>();
            s.AddSingleton<IOsvClient>(osv);
        }));
        return (new WebApplicationFactoryHandle(factory, app), osv);
    }

    private sealed record WebApplicationFactoryHandle(TestAppFactory Factory, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> App) : IDisposable
    {
        public void Dispose() { App.Dispose(); Factory.Dispose(); }
    }

    [Fact]
    public async Task About_lists_every_dependency_with_its_license()
    {
        var (h, _) = Start();
        using var _h = h;
        var owner = h.App.CreateClient();
        await owner.RegisterAndSignInAsync();

        var about = await owner.GetFromJsonAsync<About>("/api/admin/about");
        Assert.Equal(Tesria.Api.Infrastructure.Versioning.AppVersion.Current, about!.Version);
        Assert.Contains(about.Dependencies, d => d.Ecosystem == "NuGet" && d.Name == "MailKit");
        Assert.Contains(about.Dependencies, d => d.Ecosystem == "npm" && d.Component == "Web app");
        Assert.DoesNotContain(about.Dependencies, d => d.License == "UNKNOWN");
        Assert.Null(about.LastCheck);

        var notices = await owner.GetStringAsync("/api/admin/about/notices");
        Assert.Contains("Third-party software in Tesria", notices);
    }

    [Fact]
    public async Task A_check_asks_about_each_package_once_and_keeps_what_it_found()
    {
        var (h, osv) = Start();
        using var _h = h;
        var owner = h.App.CreateClient();
        await owner.RegisterAndSignInAsync();

        var check = await (await owner.PostAsync("/api/admin/about/check", null)).Content.ReadFromJsonAsync<Check>();
        Assert.Equal(osv.Asked.Count, check!.Checked);
        Assert.Equal(osv.Asked.Count, osv.Asked.Distinct().Count());
        Assert.DoesNotContain(osv.Asked, p => p.Ecosystem == "Container");
        var hit = Assert.Single(check.Affected);
        Assert.Equal("GHSA-test-0001", Assert.Single(hit.Vulnerabilities).Id);

        var about = await owner.GetFromJsonAsync<About>("/api/admin/about");
        Assert.Equal("GHSA-test-0001", about!.LastCheck!.Affected.Single().Vulnerabilities.Single().Id);

        // Offline: said so, and the last result stays.
        osv.Fail = true;
        Assert.Equal(HttpStatusCode.BadGateway, (await owner.PostAsync("/api/admin/about/check", null)).StatusCode);
        Assert.NotNull((await owner.GetFromJsonAsync<About>("/api/admin/about"))!.LastCheck);
    }

    [Fact]
    public async Task About_is_for_administrators()
    {
        var (h, _) = Start();
        using var _h = h;
        await h.App.CreateClient().RegisterAndSignInAsync();
        var member = h.App.CreateClient();
        await member.RegisterAndSignInAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/about")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync("/api/admin/about/check", null)).StatusCode);
    }
}
