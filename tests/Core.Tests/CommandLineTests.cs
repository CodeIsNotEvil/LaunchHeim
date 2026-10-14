using CINE.LaunchHeim.Core.Cli;
using CINE.LaunchHeim.Core.Game;

namespace CINE.LaunchHeim.Core.Tests;

public class CommandLineTests
{
  [Theory]
  [InlineData]
  [InlineData("nxm://valheim/mods/1/files/2?key=a&expires=1&user_id=3")]
  public void Arguments_for_the_window_are_not_a_command(params string[] args) =>
    Assert.Null(CommandLine.Parse(args));

  [Fact]
  public void Play_takes_what_to_play()
  {
    var command = CommandLine.Parse(["play", "--server", "Our Server", "--instance", "Modded", "--character", "Ragnar"])!;

    Assert.Equal(CliVerb.Play, command.Verb);
    Assert.Equal(new PlayRequest { Server = "Our Server", Instance = "Modded", Character = "Ragnar" }, command.Request);
  }

  [Fact]
  public void Play_without_options_is_the_last_play() =>
    Assert.Equal(new PlayRequest(), CommandLine.Parse(["play"])!.Request);

  [Fact]
  public void Options_take_their_value_after_an_equals_sign()
  {
    var request = CommandLine.Parse(["play", "--world=Midgard", "--password=a=b", "--vanilla"])!.Request;

    Assert.Equal("Midgard", request.World);
    // Only the first '=' splits: the rest belongs to the password.
    Assert.Equal("a=b", request.Password);
    Assert.True(request.Vanilla);
  }

  [Fact]
  public void Instant_start_can_be_switched_on_or_off()
  {
    Assert.True(CommandLine.Parse(["play", "--world", "Midgard", "--instant"])!.Request.Instant);
    Assert.False(CommandLine.Parse(["play", "--world", "Midgard", "--no-instant"])!.Request.Instant);
    Assert.Null(CommandLine.Parse(["play", "--world", "Midgard"])!.Request.Instant);
  }

  [Fact]
  public void An_empty_password_can_be_given_to_forget_the_remembered_one() =>
    Assert.Equal("", CommandLine.Parse(["play", "--server", "x:1", "--password="])!.Request.Password);

  [Fact]
  public void Steam_passes_its_command_on_untouched()
  {
    var command = CommandLine.Parse(["steam", "--instance", "Modded", "/games/Valheim/valheim.x86_64", "-force-glcore", "--instance", "x"])!;

    Assert.Equal(CliVerb.Steam, command.Verb);
    Assert.Equal("Modded", command.Request.Instance);
    Assert.Equal(["/games/Valheim/valheim.x86_64", "-force-glcore", "--instance", "x"], command.SteamCommand);
  }

  [Fact]
  public void A_double_dash_starts_steams_command_too() =>
    Assert.Equal(["-odd", "/games/Valheim/valheim.x86_64"], CommandLine.Parse(["steam", "--", "-odd", "/games/Valheim/valheim.x86_64"])!.SteamCommand);

  [Theory]
  [InlineData("steam")]
  [InlineData("steam", "--vanilla")]
  [InlineData("play", "--instance")]
  [InlineData("play", "--instnace", "Modded")]
  [InlineData("play", "/games/Valheim/valheim.x86_64")]
  [InlineData("play", "--vanilla=yes")]
  [InlineData("list", "extra")]
  public void Wrong_arguments_explain_themselves(params string[] args) =>
    Assert.Throws<CliUsageException>(() => CommandLine.Parse(args));

  [Theory]
  [InlineData("help", CliVerb.Help)]
  [InlineData("--help", CliVerb.Help)]
  [InlineData("-h", CliVerb.Help)]
  [InlineData("--version", CliVerb.Version)]
  [InlineData("list", CliVerb.List)]
  public void Simple_verbs(string argument, CliVerb verb) =>
    Assert.Equal(verb, CommandLine.Parse([argument])!.Verb);
}
