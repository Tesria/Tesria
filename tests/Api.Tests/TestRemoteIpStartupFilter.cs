using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Tesria.Api.Tests;

/// <summary>
/// Gives each test request a connection address.
///
/// The in-memory test server has no socket, so <c>RemoteIpAddress</c> is
/// null and the forwarded-headers middleware has nothing to decide trust
/// against. This runs ahead of the app's own pipeline and sets the address
/// from an <c>X-Test-Remote-Ip</c> header (default: loopback, which the app
/// trusts as a proxy). A test can therefore act as the proxy, set
/// <c>X-Forwarded-For</c> and be believed, or as an untrusted stranger by
/// naming a public address here and showing its forwarded headers are ignored.
/// </summary>
public sealed class TestRemoteIpStartupFilter : IStartupFilter
{
    public const string Header = "X-Test-Remote-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress =
                context.Request.Headers.TryGetValue(Header, out var raw) && IPAddress.TryParse(raw, out var ip)
                    ? ip
                    : IPAddress.Loopback;
            return nextMiddleware(context);
        });
        next(app);
    };
}
