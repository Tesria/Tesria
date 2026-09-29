using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// deploy/docker-desktop, the real visitor addresses setup. It runs on the
/// owner's Mac or PC, outside Docker, so these read the files.
/// </summary>
public class RealAddressesTests
{
    private static string Read(string file) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../../deploy/docker-desktop", file))).Replace("\r\n", "\n");

    [Fact]
    public void Each_tesria_on_a_pc_gets_its_own_task_rule_and_ports()
    {
        // WIN-007: a second install replaced the first one's task, moved its
        // own Caddy onto the first one's loopback ports (and was left down),
        // and -Uninstall removed the task every install shared.
        var ps1 = Read("install-windows.ps1");
        Assert.Contains("$Project = (Get-Setting 'COMPOSE_PROJECT_NAME' 'tesria')", ps1);
        Assert.Contains("$TaskName = \"Tesria real addresses$Suffix\"", ps1);
        Assert.Contains("$HttpPort = Get-Port 'TESRIA_HTTP_PORT' 80", ps1);
        Assert.Contains("LISTEN_HTTP=$HttpPort LISTEN_HTTPS=$HttpsPort TARGET_HTTP=$LoopHttp TARGET_HTTPS=$LoopHttps", ps1);
        Assert.Contains("$lines.Add(\"TESRIA_LOOPBACK_HTTP_PORT=$LoopHttp\")", ps1);
        // Nothing is left half done: a Caddy that cannot move puts .env back.
        var move = ps1.IndexOf("docker compose up -d caddy\nif ($LASTEXITCODE -ne 0) { Undo-Move", StringComparison.Ordinal);
        Assert.True(move > 0);
        Assert.True(move < ps1.IndexOf("New-NetFirewallRule", StringComparison.Ordinal));
        Assert.True(move < ps1.IndexOf("Register-ScheduledTask", StringComparison.Ordinal));
        // A task that runs another folder is never replaced or removed.
        Assert.Contains("Task Scheduler already has '$TaskName', for the Tesria in $other. Nothing was changed.", ps1);
        Assert.Contains("runs the Tesria in $other, not this one, so it and its firewall rule were left alone.", ps1);

        var compose = Read("docker-compose.real-addresses.yml");
        Assert.Contains("\"127.0.0.1:${TESRIA_LOOPBACK_HTTP_PORT:-18080}:80\"", compose);
        Assert.Contains("\"127.0.0.1:${TESRIA_LOOPBACK_HTTPS_PORT:-18443}:443\"", compose);

        var mjs = Read("real-addresses.mjs");
        Assert.Contains("const env = (name, fallback) => args[name] || process.env[name] || fallback", mjs);
    }
}
