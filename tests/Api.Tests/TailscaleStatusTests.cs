using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Features.Admin;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The Tailscale card's status (dev-plan 19.1), read from the file the
/// sidecar's health check writes. Samples are trimmed from a real
/// <c>tailscale status --json --peers=false</c>.
/// </summary>
public class TailscaleStatusTests
{
    private static string Sample(string state = "Running", bool online = true, string? keyExpiry = "2027-03-23T09:21:42Z") => $$"""
        {
          "Version": "1.102.4-tbbcd7d1fc",
          "BackendState": "{{state}}",
          "AuthURL": "",
          "Self": {
            "HostName": "tesria",
            "DNSName": "tesria.example-tailnet.ts.net.",
            "Online": {{(online ? "true" : "false")}}{{(keyExpiry is null ? "" : $",\n    \"KeyExpiry\": \"{keyExpiry}\"")}}
          }
        }
        """;

    private static string Write(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ts-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void No_file_means_the_sidecar_is_not_in_use() =>
        Assert.False(TailscaleEndpoints.Read(Path.Combine(Path.GetTempPath(), "missing-ts.json")).Configured);

    [Fact]
    public void A_running_device_gives_its_address_key_expiry_and_version()
    {
        var path = Write(Sample());
        try
        {
            var s = TailscaleEndpoints.Read(path);
            Assert.True(s.Configured && s.Online && !s.Stale);
            Assert.Equal("Running", s.State);
            Assert.Equal("https://tesria.example-tailnet.ts.net", s.Address);
            Assert.Equal(new DateTimeOffset(2027, 3, 23, 9, 21, 42, TimeSpan.Zero), s.KeyExpiry);
            Assert.Equal("1.102.4", s.Version);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Key_expiry_turned_off_reads_as_no_expiry_and_an_old_file_as_stale()
    {
        var path = Write(Sample(keyExpiry: null));
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-10));
            var s = TailscaleEndpoints.Read(path);
            Assert.Null(s.KeyExpiry);
            Assert.True(s.Stale);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Only_those_who_change_the_instance_see_it()
    {
        var path = Write(Sample());
        try
        {
            using var f = new TestAppFactory(new Dictionary<string, string?> { ["Tailscale:StatusFile"] = path });
            var owner = f.CreateClient();
            await owner.RegisterAndSignInAsync();
            var status = await owner.GetFromJsonAsync<TailscaleEndpoints.TailscaleStatus>("/api/admin/tailscale");
            Assert.Equal("https://tesria.example-tailnet.ts.net", status!.Address);

            var member = f.CreateClient();
            await member.RegisterAndSignInAsync();
            Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/tailscale")).StatusCode);
        }
        finally { File.Delete(path); }
    }
}
