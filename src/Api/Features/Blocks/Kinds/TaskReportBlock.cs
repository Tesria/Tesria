using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Blocks.Kinds;

/// <summary>
/// Task report: action items across a scope, optionally only the ones
/// assigned to someone. Reads the Wave C `assigneeId` attribute, which the
/// editor keeps in step with the mention inside each task — the reason that
/// attribute exists at all is so this is a filter rather than a tree walk
/// over every page's document.
/// </summary>
public sealed class TaskReportBlock : IDynamicBlockKind
{
    public string Kind => "task-report";

    public async Task<BlockResult> RenderAsync(BlockContext ctx, CancellationToken ct)
    {
        var scope = ctx.Enum("scope", "tree", "tree", "space", "all");
        var status = ctx.Enum("status", "open", "open", "done", "all");
        var assignee = ctx.Str("assignee", "any");
        var limit = ctx.Int("limit", 25, 1, 100);

        Guid? wanted = null;
        if (assignee == "me")
        {
            // An anonymous reader has no "me" — an empty report, not an error.
            if (ctx.CurrentUserId is not { } me) return Empty("Sign in to see tasks assigned to you.");
            wanted = me;
        }
        else if (assignee != "any")
        {
            if (!Guid.TryParse(assignee, out var id))
                throw new BlockParamException("assignee", "'assignee' must be 'any', 'me', or a user id.");
            wanted = id;
        }

        // Visibility first, content second: a restricted page's tasks must not
        // reach the walk at all.
        var candidates = await PageScope.CandidatesAsync(ctx, scope, ct);
        var visible = await ctx.VisibleAsync(candidates, c => c.Id, int.MaxValue, ct);
        var ids = visible.Select(v => v.Id).ToList();

        var contents = await ctx.Db.Pages.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.CurrentVersion != null)
            .Select(p => new { p.Id, p.CurrentVersion!.ContentJson })
            .ToListAsync(ct);
        var pageById = visible.ToDictionary(v => v.Id);

        var items = new List<BlockItem>();
        foreach (var page in contents)
        {
            foreach (var task in BlockDocuments.Tasks(page.ContentJson))
            {
                if (status == "open" && task.Checked) continue;
                if (status == "done" && !task.Checked) continue;
                if (wanted is { } w && task.AssigneeId != w) continue;
                if (task.Text.Length == 0) continue;

                var p = pageById[page.Id];
                items.Add(new BlockItem(task.Text, ctx.HrefFor(p.SpaceKey, p.Id),
                    Cells: new Dictionary<string, BlockCell>
                    {
                        ["done"] = BlockCell.Done(task.Checked),
                        ["task"] = BlockCell.Of(task.Text),
                        ["who"] = BlockCell.Of(task.AssigneeName ?? ""),
                        ["page"] = BlockCell.Of(p.Title, ctx.HrefFor(p.SpaceKey, p.Id)),
                    }));
                if (items.Count >= limit) break;
            }
            if (items.Count >= limit) break;
        }

        return BlockResult.Table(Kind,
            [new("done", "✓"), new("task", "Task"), new("who", "Assignee"), new("page", "Page")],
            items, empty: "No matching action items.");

        BlockResult Empty(string why) => BlockResult.Table(Kind,
            [new("done", "✓"), new("task", "Task"), new("who", "Assignee"), new("page", "Page")], [], empty: why);
    }
}
