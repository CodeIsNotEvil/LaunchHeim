using System.Globalization;
using System.Runtime.CompilerServices;
using Qml.Net;

namespace CINE.LaunchHeim.Desktop.ViewModels;

/// <summary>
/// Base for everything QML binds to. Qml.Net exposes public members camelCased (<c>IsBusy</c> becomes
/// <c>isBusy</c>) and turns <see cref="NotifySignalAttribute"/> into <c>isBusyChanged</c> signals.
/// </summary>
/// <remarks>
/// <para>
/// Read-only properties carry a <see cref="NotifySignalAttribute"/> too, although it is never raised:
/// Qml.Net cannot mark a property CONSTANT, and without a notify signal Qt warns about every binding
/// expression that reads one.
/// </para>
/// <para>
/// Signals must be raised on the Qt thread. Qml.Net installs a SynchronizationContext for it, so any
/// code that awaits from a QML-invoked method is back on that thread afterwards; background work runs
/// inside <c>Task.Run</c> and only touches view models after the await.
/// </para>
/// </remarks>
public abstract class ViewModel
{
  protected bool Set<T>(ref T field, T value, [CallerMemberName] string property = "")
  {
    if (EqualityComparer<T>.Default.Equals(field, value))
    {
      return false;
    }

    field = value;
    this.ActivateNotifySignal(property);
    return true;
  }

  protected void Raise(string property) => this.ActivateNotifySignal(property);
}

internal static class Format
{
  public static string Count(long value) => value switch
  {
    >= 1_000_000 => (value / 1_000_000.0).ToString(value >= 10_000_000 ? "0" : "0.#", CultureInfo.InvariantCulture) + "M",
    >= 1_000 => (value / 1_000.0).ToString(value >= 10_000 ? "0" : "0.#", CultureInfo.InvariantCulture) + "k",
    _ => value.ToString(CultureInfo.InvariantCulture),
  };

  public static string Bytes(long bytes) => bytes switch
  {
    <= 0 => "",
    >= 1024 * 1024 => (bytes / 1024.0 / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " MB",
    >= 1024 => (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB",
    _ => bytes + " B",
  };

  public static string Ago(DateTimeOffset? when)
  {
    if (when is not { } value || value == DateTimeOffset.MinValue)
    {
      return "never";
    }

    var span = DateTimeOffset.UtcNow - value;
    return span switch
    {
      { TotalMinutes: < 1 } => "just now",
      { TotalHours: < 1 } => $"{(int)span.TotalMinutes} min ago",
      { TotalDays: < 1 } => $"{(int)span.TotalHours} h ago",
      { TotalDays: < 2 } => "yesterday",
      { TotalDays: < 30 } => $"{(int)span.TotalDays} days ago",
      { TotalDays: < 365 } => $"{(int)(span.TotalDays / 30)} months ago",
      _ => value.LocalDateTime.ToString("d MMM yyyy", CultureInfo.CurrentCulture),
    };
  }

  public static string Date(DateTimeOffset when) =>
    when == DateTimeOffset.MinValue ? "" : when.LocalDateTime.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
}
