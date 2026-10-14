using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace CINE.LaunchHeim.GamePlugin;

/// <summary>
/// Keeps Steam achievements working while modded, with the switch Valheim itself offers for it.
/// </summary>
/// <remarks>
/// <para>
/// Valheim gives no achievements once the game counts as cheated, and a mod setting <c>Game.isModded</c>
/// (as Iron Gate asks mods to) is enough for that (<c>Achievements.IsCheatedAtAll</c>). The game's dev
/// command <c>yesiuseddevcommandsbutiwantmyachievementsanyway 1</c> allows them anyway: it stores
/// <c>bypasscheatchecks 1</c> among the character's unique keys, which <c>CanGetAchievements</c> checks.
/// The same switch also lets items and pieces made with dev commands count; that is how the game made it.
/// </para>
/// <para>
/// With LaunchHeim's setting on, this sets that key on every character that enters a world, which the
/// game then saves with the character, as after the command. A <see cref="Marker"/> key records that
/// LaunchHeim did it, so switching the setting off removes the key only where LaunchHeim set it, never
/// where the player typed the command themselves.
/// </para>
/// </remarks>
internal sealed class Achievements(ManualLogSource logger, bool keep)
{
  /// <summary>Followed by <c>1</c> (keep achievements) or <c>0</c> (undo what LaunchHeim set).</summary>
  public const string Argument = "-launchheim-achievements";

  private const string BypassKey = "bypasscheatchecks";
  private const string Marker = "launchheimbypasscheatchecks";
  private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

  /// <summary>Looks after each character as it enters a world, for the whole session.</summary>
  public IEnumerator Run()
  {
    var playerType = Game.FindType("Player");
    var localPlayer = playerType?.GetField("m_localPlayer", Any);
    var tryGet = playerType?.GetMethod("TryGetUniqueKeyValue", Any);
    var add = playerType?.GetMethod("AddUniqueKeyValue", Any);
    var remove = playerType?.GetMethod("RemoveUniqueKeyValue", Any);
    if (localPlayer is null || tryGet is null || add is null || remove is null)
    {
      logger.LogWarning("Achievements: Valheim's player keys weren't found, so nothing is changed.");
      yield break;
    }

    object? handled = null;
    while (true)
    {
      var player = localPlayer.GetValue(null) as Object;
      // Unity's own null check: a destroyed player (back in the menu) compares equal to null.
      if (player != null && !ReferenceEquals(player, handled))
      {
        handled = player;
        Apply(player, tryGet, add, remove);
      }

      yield return new WaitForSecondsRealtime(1f);
    }
  }

  private void Apply(object player, MethodInfo tryGet, MethodInfo add, MethodInfo remove)
  {
    string? Get(string key)
    {
      var arguments = new object?[] { key, null };
      return tryGet.Invoke(player, arguments) is true ? arguments[1] as string : null;
    }

    if (keep)
    {
      if (Get(BypassKey) == "1")
      {
        return;
      }

      add.Invoke(player, [BypassKey, "1"]);
      add.Invoke(player, [Marker, "1"]);
      logger.LogInfo("Achievements: kept for this character, as with yesiuseddevcommandsbutiwantmyachievementsanyway 1.");
    }
    else if (Get(Marker) is not null)
    {
      remove.Invoke(player, [BypassKey]);
      remove.Invoke(player, [Marker]);
      logger.LogInfo("Achievements: LaunchHeim's setting is off, so its achievement switch was removed from this character.");
    }
  }
}
