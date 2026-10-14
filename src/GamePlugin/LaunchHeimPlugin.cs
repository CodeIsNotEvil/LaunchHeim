using System;
using System.Linq;
using BepInEx;

namespace CINE.LaunchHeim.GamePlugin;

/// <summary>
/// LaunchHeim's plugin inside Valheim. LaunchHeim copies it into the instance it starts and tells it
/// on the command line what to do; without those arguments it does nothing.
/// </summary>
/// <remarks>
/// <para>
/// Two jobs: <see cref="InstantStart"/> presses Start in the menus, and <see cref="Achievements"/> keeps
/// Steam achievements working while mods mark the game as modded.
/// </para>
/// <para>
/// Valheim's classes are found by name at run time (<see cref="Game"/>), so this compiles without the
/// game. A game update that renames them only makes a job give up, which the BepInEx log says.
/// </para>
/// </remarks>
[BepInPlugin("io.github.codeisnotevil.launchheim", "LaunchHeim", "1.1.0")]
public sealed class LaunchHeimPlugin : BaseUnityPlugin
{
  private void Awake()
  {
    var arguments = Environment.GetCommandLineArgs();
    if (arguments.Contains(InstantStart.Argument))
    {
      StartCoroutine(new InstantStart(Logger).Run(joinsServer: arguments.Contains("+connect")));
    }

    var index = Array.IndexOf(arguments, Achievements.Argument);
    if (index >= 0 && index + 1 < arguments.Length)
    {
      StartCoroutine(new Achievements(Logger, keep: arguments[index + 1] == "1").Run());
    }
  }
}

/// <summary>Finds Valheim's classes, which this plugin can't reference at compile time.</summary>
internal static class Game
{
  public static Type? FindType(string name) =>
    AppDomain.CurrentDomain.GetAssemblies()
      .Where(a => a.GetName().Name is "assembly_valheim" or "assembly_utils")
      .Select(a => a.GetType(name, throwOnError: false))
      .FirstOrDefault(t => t is not null);
}
