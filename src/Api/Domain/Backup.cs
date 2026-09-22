namespace Tesria.Api.Domain;

/// <summary>
/// The names shared by the app and the two backup sidecars (dev-plan 9.1).
/// These strings are written by bash in <c>deploy/backup</c> and
/// <c>deploy/pgbackrest</c>, so they are plain text columns rather than
/// integer enums: a row read in psql has to mean something without the C#.
/// </summary>
public static class BackupNames
{
    /// <summary>The <c>backup</c> sidecar: pg_dump plus the uploads archive.</summary>
    public const string Logical = "logical";

    /// <summary>The <c>pgbackrest</c> sidecar: physical backups and WAL archiving.</summary>
    public const string Physical = "physical";

    public static readonly string[] Agents = [Logical, Physical];

    public const string KindBackup = "backup";
    public const string KindRestoreTest = "restore-test";

    /// <summary>
    /// Copy the latest backups to a removable drive on demand (dev-plan 9.2
    /// step 4). Not scheduled: a drive is absent most of the time, so this
    /// runs when somebody has plugged one in and asked.
    /// </summary>
    public const string KindCopyOffsite = "copy-offsite";

    public const string TriggerScheduled = "scheduled";
    public const string TriggerManual = "manual";
    public const string TriggerStartup = "startup";

    public const string StatusRequested = "requested";
    public const string StatusRunning = "running";
    public const string StatusSucceeded = "succeeded";
    public const string StatusFailed = "failed";

    public const string RemovedByRetention = "retention";
    public const string RemovedMissing = "missing";
}

/// <summary>
/// One row per backup sidecar, written only by that sidecar: its heartbeat,
/// schedule, disk and the retention policy it last applied. The app role
/// can read it and nothing else (<c>DatabaseRoles.ReadOnlyTables</c>).
/// </summary>
public class BackupAgent
{
    /// <summary><see cref="BackupNames.Logical"/> or <see cref="BackupNames.Physical"/>.</summary>
    public required string Name { get; set; }

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>
    /// When the next scheduled backup is due. Persisted, so a restart picks
    /// the schedule up where it was instead of starting a new one.
    /// </summary>
    public DateTimeOffset? NextRunAt { get; set; }

    public int IntervalHours { get; set; }

    /// <summary>Physical only: a new full backup once the newest is this old.</summary>
    public int? FullEveryDays { get; set; }

    public string? ToolVersion { get; set; }
    public long? VolumeFreeBytes { get; set; }
    public long? VolumeTotalBytes { get; set; }

    /// <summary>
    /// What this agent's own backups occupy on that volume (dev-plan 9.3).
    /// The third number the space chart needs: "everything else" is total
    /// minus free minus this, and without it the chart could only show used
    /// against free, which says nothing about whether the backups are the
    /// thing filling the disk.
    /// </summary>
    public long? VolumeBackupBytes { get; set; }

    /// <summary>
    /// What the live wiki itself occupies, as far as this agent can see it:
    /// the attachments for the logical agent, the database directory for the
    /// physical one (dev-plan 9.3). Shown as its own slice, because "how much
    /// is the wiki and how much is its backups" is the question people
    /// actually bring to this chart.
    /// </summary>
    public long? VolumeWikiBytes { get; set; }

    /// <summary>
    /// The filesystem the volume is on, as <c>df</c> names it. Two agents
    /// that share one filesystem must draw one chart rather than two of the
    /// same disk, and normally they do share one.
    /// </summary>
    public string? VolumeFilesystem { get; set; }

    /// <summary>Physical only: when Postgres last archived a WAL segment (pg_stat_archiver).</summary>
    public DateTimeOffset? WalArchivedAt { get; set; }

    /// <summary>The policy this agent last applied. Null until it applies one.</summary>
    public bool? AppliedRetentionEnabled { get; set; }
    public int? AppliedKeepCount { get; set; }
    public int? AppliedKeepDays { get; set; }

    /// <summary>
    /// When this agent first saw a policy stricter than the applied one. The
    /// stricter policy takes effect <see cref="Infrastructure.Backups.BackupRetention.Grace"/>
    /// later; until then nothing either policy would keep is removed.
    /// </summary>
    public DateTimeOffset? PolicyObservedAt { get; set; }

    /// <summary>The agent's last notable log line, for the status card.</summary>
    public string? Message { get; set; }
}

/// <summary>
/// The inventory: one row per backup that exists or once existed. The disk
/// (or pgBackRest's own catalogue) is the truth; the sidecar mirrors it here
/// every minute. Rows are never deleted, so a removed backup stays as history.
/// </summary>
public class Backup
{
    public Guid Id { get; set; }

    public required string Agent { get; set; }

    /// <summary>
    /// Logical: the cycle stamp shared by the dump and the uploads archive,
    /// e.g. <c>20260917T050527Z</c>. Physical: pgBackRest's label, e.g.
    /// <c>20260917-034639F</c> or <c>20260917-034639F_20260918-040000I</c>.
    /// </summary>
    public required string Label { get; set; }

    /// <summary><c>dump</c>, <c>full</c>, <c>diff</c> or <c>incr</c>.</summary>
    public required string Type { get; set; }

    /// <summary>Physical: the backup this one depends on.</summary>
    public string? Prior { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>File names (logical) or WAL range and LSNs (physical).</summary>
    public string? DetailJson { get; set; }

    /// <summary>Logical: the cycle includes an uploads archive.</summary>
    public bool HasUploads { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }

    /// <summary><c>retention</c>, or <c>missing</c> when the files went without a retention run.</summary>
    public string? RemovedReason { get; set; }

    public DateTimeOffset? LastVerifiedAt { get; set; }
    public bool? LastVerifyOk { get; set; }

    /// <summary>Physical: the full backup a diff or incr belongs to.</summary>
    public string? FullLabel => Type is "diff" or "incr" && Label.IndexOf('_') is > 0 and var i ? Label[..i] : null;
}

/// <summary>
/// One row per configured offsite target (dev-plan 9.2): cloud, NAS or
/// removable. Written only by the backup sidecars, read by the app.
///
/// <para>The point of this table is that <b>the app never sees a secret</b>.
/// Offsite credentials live in <c>.env</c> and are read by the sidecars
/// alone; what reaches the database is this row, in which a key or a
/// passphrase is a short fingerprint and nothing else. There is deliberately
/// no code path that can return a value, because there is no value here to
/// return.</para>
/// </summary>
public class BackupTarget
{
    /// <summary>cloud | nas | removable.</summary>
    public required string Slot { get; set; }

    /// <summary>
    /// <c>database</c> or <c>files</c>: a slot can hold both, and they are
    /// different repositories written by different sidecars. The database
    /// goes to a pgBackRest repository (cloud only, since a mounted path
    /// cannot safely be one); the uploads and the logical dumps go to a
    /// restic repository, which every slot can have. Keeping them as two
    /// rows rather than two sets of columns is also what lets the cloud card
    /// chart one against the other (9.3).
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>What kind of storage: s3, b2, posix. Null when the slot is off.</summary>
    public string? Type { get; set; }

    /// <summary>Endpoint for a cloud target, mount path for the others.</summary>
    public string? Location { get; set; }

    public string? Bucket { get; set; }
    public string? Prefix { get; set; }

    /// <summary>Whether this slot is configured and usable.</summary>
    public bool Enabled { get; set; }

    /// <summary>Why it is not usable, in words fit for the screen.</summary>
    public string? Problem { get; set; }

    /// <summary>
    /// Whether the target was actually there on the last pass. Separate from
    /// <see cref="Enabled"/>, because for a path target the two are different
    /// questions: a removable drive is configured and absent most of the
    /// time, which is normal, while a network drive that is absent is a
    /// problem. Null for a target where presence is not a question.
    /// </summary>
    public bool? Present { get; set; }

    /// <summary>SHA-256, first 16 hex, of the storage key. Never the key.</summary>
    public string? KeyFingerprint { get; set; }

    /// <summary>SHA-256, first 16 hex, of the passphrase. Never the passphrase.</summary>
    public string? PassphraseFingerprint { get; set; }

    public DateTimeOffset? LastBackupAt { get; set; }
    public DateTimeOffset? LastWalAt { get; set; }
    public DateTimeOffset? LastVerifyAt { get; set; }
    public long? BytesStored { get; set; }

    /// <summary>
    /// WAL segments waiting to be archived. The leading indicator: a
    /// repository that has gone away shows up here long before
    /// <c>archive-push-queue-max</c> trips, and tripping it costs
    /// point-in-time recovery on the *local* repository too, not only this
    /// one (see 9.2's archive-push findings).
    /// </summary>
    public int? WalBacklogFiles { get; set; }

    /// <summary>
    /// When this target's copy was last restored for real, and whether it
    /// worked (dev-plan 9.2 step 6). Distinct from <see cref="LastVerifyAt"/>,
    /// which is the repository checking itself: this is a dump taken back out,
    /// loaded into a throwaway database and counted. It is the only check that
    /// answers the question the backups exist for.
    /// </summary>
    public DateTimeOffset? LastDrillAt { get; set; }
    public bool? LastDrillOk { get; set; }

    /// <summary>The sidecar's last word on this target, for the status card.</summary>
    public string? Message { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// The queue and the run log. The app appends <c>requested</c> rows (Back up
/// now, Test restore); the sidecars append their own scheduled runs and own
/// every update. Append-only for the app role.
/// </summary>
public class BackupJob
{
    public Guid Id { get; set; }
    public required string Agent { get; set; }
    public required string Kind { get; set; }
    public required string Trigger { get; set; }
    public required string Status { get; set; }

    /// <summary>Restore tests: the label of the backup to restore.</summary>
    public string? Target { get; set; }

    public DateTimeOffset RequestedAt { get; set; }
    public Guid? RequestedById { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? Error { get; set; }

    /// <summary>What was produced, what retention removed and why, orphans cleaned, tables restored.</summary>
    public string? ResultJson { get; set; }

    /// <summary>The last 40 lines of the job's output.</summary>
    public string? LogTail { get; set; }
}
