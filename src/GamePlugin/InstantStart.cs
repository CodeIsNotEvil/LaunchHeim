using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace CINE.LaunchHeim.GamePlugin;

/// <summary>
/// Presses Start in Valheim's menus for LaunchHeim: straight into a world, or onto a server without
/// stopping at the character selection.
/// </summary>
/// <remarks>
/// <para>
/// It runs only when the game was started with <see cref="Argument"/>, which LaunchHeim only passes
/// for a server or world whose "Start instantly" is on. LaunchHeim has already set the PlayerPrefs the
/// menus select from (<c>profile</c>, and <c>world</c> for a world) and passed <c>+connect</c> for a server.
/// </para>
/// <para>
/// Only the game's own menu actions are called, the ones its buttons call (<c>FejdStartup.OnStartGame</c>,
/// <c>OnCharacterStart</c>, <c>OnWorldStart</c>), so its checks still run: a corrupt world's backup prompt,
/// the cloud storage warning, a server's password. Before pressing anything it checks that the game
/// selected what LaunchHeim asked for. When it didn't (a character or world that's gone), or the menu
/// isn't ready in time (a popup waiting for an answer), it leaves the menu to the player.
/// </para>
/// </remarks>
internal sealed class InstantStart(ManualLogSource logger)
{
  public const string Argument = "-launchheim-instant";

  // A server is looked up through Steam before the character selection opens, which can take a while.
  private static readonly float ServerTimeout = 180f;
  private static readonly float WorldTimeout = 120f;

  /// <summary>The menu work, as a coroutine for the plugin to run.</summary>
  public IEnumerator Run(bool joinsServer)
  {
    logger.LogInfo(joinsServer ? "Instant start: joining the server." : "Instant start: opening the world.");
    Menu? menu = null;
    var deadline = Time.realtimeSinceStartup + (joinsServer ? ServerTimeout : WorldTimeout);
    while (Time.realtimeSinceStartup < deadline)
    {
      menu ??= Menu.Find();
      if (menu is not null && menu.IsReady(joinsServer))
      {
        break;
      }

      yield return new WaitForSecondsRealtime(0.25f);
    }

    if (menu is null || !menu.IsReady(joinsServer))
    {
      logger.LogWarning("Instant start: the menu wasn't ready in time, so it's left to you.");
      yield break;
    }

    // One frame for the menu to finish what made it ready (its fade, the character preview).
    yield return null;
    if (!menu.HasCharacterSelected())
    {
      logger.LogWarning($"Instant start: the game didn't select the character \"{menu.WantedCharacter}\", so it's left to you.");
      yield break;
    }

    if (joinsServer)
    {
      // The character selection's Start: with a join queued it joins instead of opening the worlds.
      menu.Call("OnCharacterStart");
      yield break;
    }

    menu.Call("OnStartGame");
    yield return null;
    // Opens the world list, which selects the world in the PlayerPrefs.
    menu.Call("OnCharacterStart");
    yield return null;
    if (!menu.HasWorldSelected())
    {
      logger.LogWarning($"Instant start: the game didn't select the world \"{menu.WantedWorld}\", so it's left to you.");
      yield break;
    }

    menu.Call("OnWorldStart");
  }

  /// <summary>Valheim's main menu (<c>FejdStartup</c>), read and driven by reflection.</summary>
  private sealed class Menu
  {
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private readonly object _startup;
    private readonly Type _type;
    private readonly Type? _popup;
    private readonly Type? _cinematics;
    private readonly Type? _prefs;

    private Menu(object startup, Type type, Type? popup, Type? cinematics, Type? prefs)
    {
      _startup = startup;
      _type = type;
      _popup = popup;
      _cinematics = cinematics;
      _prefs = prefs;
    }

    public static Menu? Find()
    {
      var type = Game.FindType("FejdStartup");
      if (type is null)
      {
        return null;
      }

      var startup = type.GetProperty("instance", Any)?.GetValue(null);
      return startup is null ? null : new Menu(startup, type, Game.FindType("UnifiedPopup"), Game.FindType("CinematicsManager"), Game.FindType("PlatformPrefs"));
    }

    public string WantedCharacter => Pref("profile");

    public string WantedWorld => Pref("world");

    /// <summary>
    /// Shown, done with its intro and with nothing waiting for an answer. For a server, its join is
    /// queued and the character selection is open, which happens once Steam has found the server.
    /// </summary>
    public bool IsReady(bool joinsServer)
    {
      if (_cinematics is not null && StaticBool(_cinematics, "IsStartedPlaying"))
      {
        // The intro film: what instant start is for is not having to sit through it.
        _cinematics.GetMethod("Stop", Any, null, Type.EmptyTypes, null)?.Invoke(null, null);
        return false;
      }

      if ((_popup is not null && StaticBool(_popup, "IsVisible")) || Field("m_profiles") is null)
      {
        return false;
      }

      if (!joinsServer)
      {
        return Field("m_mainMenu") is GameObject main && main.activeInHierarchy;
      }

      var queued = Field("m_queuedJoinServer");
      var valid = queued?.GetType().GetProperty("IsValid", Any)?.GetValue(queued) as bool? ?? false;
      return valid && Field("m_characterSelectScreen") is GameObject select && select.activeInHierarchy;
    }

    public bool HasCharacterSelected()
    {
      if (Field("m_profiles") is not IList profiles || Field("m_profileIndex") is not int index || index < 0 || index >= profiles.Count)
      {
        return false;
      }

      var profile = profiles[index];
      var fileName = profile?.GetType().GetMethod("GetFilename", Any, null, Type.EmptyTypes, null)?.Invoke(profile, null) as string;
      return WantedCharacter.Length > 0 && string.Equals(fileName, WantedCharacter, StringComparison.Ordinal);
    }

    public bool HasWorldSelected()
    {
      var world = Field("m_world");
      var name = world?.GetType().GetField("m_name", Any)?.GetValue(world) as string;
      return WantedWorld.Length > 0 && string.Equals(name, WantedWorld, StringComparison.Ordinal);
    }

    public void Call(string method) => _type.GetMethod(method, Any, null, Type.EmptyTypes, null)?.Invoke(_startup, null);

    private object? Field(string name) => _type.GetField(name, Any)?.GetValue(_startup);

    private string Pref(string key) =>
      _prefs?.GetMethod("GetString", Any, null, [typeof(string), typeof(string)], null)?.Invoke(null, [key, ""]) as string ?? "";

    private static bool StaticBool(Type type, string method) =>
      type.GetMethod(method, Any, null, Type.EmptyTypes, null)?.Invoke(null, null) as bool? ?? false;

  }
}
