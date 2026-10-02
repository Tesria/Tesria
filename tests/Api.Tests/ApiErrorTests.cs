using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Refusals the framework used to send with nothing in them, or with the
/// wrong status (T5-024, T5-025).
/// </summary>
public class ApiErrorTests
{
    private record Problem(int Status, string? Detail);
    private record PageDto(Guid Id);

    [Fact]
    public async Task A_json_body_without_its_content_type_answers_415_saying_so()
    {
        // T5-025: curl's -d without -H "Content-Type: application/json" got
        // 404 with an empty body, which reads as a wrong address.
        await using var app = new TestAppFactory();
        var client = app.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        var json = $$"""{"spaceId":"{{spaceId}}","title":"Plain","contentJson":null}""";

        foreach (var type in new[] { "text/plain", "application/x-www-form-urlencoded" })
        {
            var res = await client.PostAsync("/api/pages", new StringContent(json, Encoding.UTF8, type));
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, res.StatusCode);
            var body = await res.Content.ReadFromJsonAsync<Problem>();
            Assert.Contains("Content-Type: application/json", body!.Detail);
        }

        // The same body marked as JSON goes in, and a wrong address is still a 404.
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsync("/api/pages", new StringContent(json, Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsync("/api/no-such-thing", new StringContent(json, Encoding.UTF8, "text/plain"))).StatusCode);
    }

    [Fact]
    public async Task An_attachment_over_the_limit_answers_413_with_the_limit()
    {
        // T5-024: the words were right, the status was 400.
        await using var app = new TestAppFactory();
        var client = app.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        var page = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, Title = "Big", ContentJson = (string?)null })).Content.ReadFromJsonAsync<PageDto>();

        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(new byte[26 * 1024 * 1024]);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(bytes, "file", "big.bin");
        var res = await client.PostAsync($"/api/pages/{page!.Id}/attachments", form);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);
        Assert.Contains("25 MB", await res.Content.ReadAsStringAsync());
    }
}
