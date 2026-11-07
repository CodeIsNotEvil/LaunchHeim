namespace CINE.LaunchHeim.Core.Servers.Backups;

/// <summary>Where a server's backups go besides the local folder.</summary>
public enum BackupCloud
{
  None,

  /// <summary>An rclone remote (<c>remote:path</c>): Google Drive, OneDrive, Nextcloud/WebDAV, S3, a NAS over SFTP …</summary>
  Rclone,

  /// <summary>Proton Drive through Proton's own CLI, <c>proton-drive</c> (June 2026), signed in once with <c>proton-drive auth login</c>.</summary>
  ProtonDrive,
}

/// <summary>A server's backup settings, part of its profile (<see cref="ServerProfile.Backup"/>).</summary>
public sealed class BackupSettings
{
  /// <summary>Back up on a schedule while LaunchHeim runs (and the server does).</summary>
  public bool Scheduled { get; set; }

  public int IntervalHours { get; set; } = 6;

  /// <summary>Back up after every clean stop of a local server, when the world was just saved.</summary>
  public bool AfterStop { get; set; } = true;

  /// <summary>
  /// The folder backups are written to: another drive, or a NAS share mounted on this PC. Null for
  /// LaunchHeim's own <c>backups/&lt;server&gt;</c> in its data folder.
  /// </summary>
  public string? Folder { get; set; }

  public BackupCloud Cloud { get; set; } = BackupCloud.None;

  /// <summary>
  /// Where in the cloud: <c>remote:Valheim/backups</c> for rclone, a Drive folder such as
  /// <c>/my-files/Valheim backups</c> for Proton Drive.
  /// </summary>
  public string CloudPath { get; set; } = "";

  /// <summary>The newest backups kept whatever their age.</summary>
  public int KeepLast { get; set; } = 10;

  /// <summary>Beyond those, the newest backup of each of the last days.</summary>
  public int KeepDays { get; set; } = 14;

  /// <summary>Also back up <c>BepInEx/config</c>: the mods' settings, which take time to tune.</summary>
  public bool IncludeConfigs { get; set; } = true;

  /// <summary>The rclone or proton-drive program, when it isn't on PATH; null to look for it.</summary>
  public string? ToolPath { get; set; }

  public DateTimeOffset? LastBackupAt { get; set; }

  /// <summary>What's missing or wrong, by the backup form's fields (see <see cref="ServerProfile.Validate"/>).</summary>
  public IReadOnlyList<FieldProblem> Validate()
  {
    var problems = new List<FieldProblem>();
    if (IntervalHours is < 1 or > 24 * 7)
    {
      problems.Add(new("backupInterval", "Between 1 hour and a week."));
    }

    if (KeepLast < 1)
    {
      problems.Add(new("backupKeepLast", "Keep at least one."));
    }

    if (KeepDays < 0)
    {
      problems.Add(new("backupKeepDays", "Zero or more days."));
    }

    if (Cloud != BackupCloud.None && string.IsNullOrWhiteSpace(CloudPath))
    {
      problems.Add(new("backupCloudPath", Cloud == BackupCloud.Rclone
        ? "Enter the rclone remote, such as gdrive:Valheim (set it up with rclone config)."
        : "Enter the Proton Drive folder, such as /my-files/Valheim backups."));
    }
    else if (Cloud == BackupCloud.ProtonDrive && !CloudPath.Trim().StartsWith('/'))
    {
      problems.Add(new("backupCloudPath", "A Proton Drive folder starts with /my-files/."));
    }
    else if (Cloud == BackupCloud.Rclone && !CloudPath.Contains(':') && !Path.IsPathRooted(CloudPath))
    {
      problems.Add(new("backupCloudPath", "An rclone remote looks like name:folder."));
    }

    if (!string.IsNullOrWhiteSpace(ToolPath) && !File.Exists(ToolPath))
    {
      problems.Add(new("backupToolPath", "That program isn't there anymore."));
    }

    if (!string.IsNullOrWhiteSpace(Folder) && !Path.IsPathRooted(Folder))
    {
      problems.Add(new("backupFolder", "Choose a whole folder path."));
    }

    return problems;
  }
}
