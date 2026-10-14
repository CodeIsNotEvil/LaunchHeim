using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Saves;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Tests;

public class PlayTargetsTests
{
  private static readonly Instance Modded = new() { Id = "modded-id", Name = "Modded" };
  private static readonly Instance Other = new() { Id = "other-id", Name = "Other" };
  private static readonly ValheimServer Server = new("Our Server", "10.0.0.1:2456", IsFavorite: true, IsRecent: false);
  private static readonly ValheimWorld World = new("Midgard", SaveSource.Cloud, DateTime.UtcNow);

  private static readonly PlayLibrary Library = new(
    [Modded, Other],
    [new ValheimCharacter("ragnar", SaveSource.Cloud, DateTime.UtcNow), new ValheimCharacter("freya", SaveSource.Local, DateTime.UtcNow)],
    [Server],
    [World, World with { Source = SaveSource.Local }]);

  private static AppSettings Settings(LastPlay? last = null) => new()
  {
    LastPlay = last,
    PlayChoices =
    {
      [Server.Key] = new PlayChoice { Character = "ragnar", InstanceId = Modded.Id, Password = "hunter22" },
      [World.Key] = new PlayChoice { Character = "freya", InstanceId = null },
    },
  };

  private static PlayTarget Resolve(PlayRequest request, AppSettings? settings = null) =>
    PlayTargets.Resolve(request, settings ?? Settings(), Library);

  [Fact]
  public void Nothing_played_yet_is_vanilla() =>
    Assert.Equal(PlayTarget.VanillaGame, Resolve(new PlayRequest()));

  [Fact]
  public void The_last_server_is_joined_with_its_remembered_choice()
  {
    var target = Resolve(new PlayRequest(), Settings(new LastPlay { DestinationKey = Server.Key, DestinationName = Server.Name }));

    Assert.Same(Modded, target.Instance);
    Assert.Equal(["+connect", "10.0.0.1:2456", "-password", "hunter22"], target.Direct!.Arguments);
    Assert.Equal("ragnar", target.Direct.Prefs[ValheimPrefs.Character]);
    Assert.Equal("Our Server · Modded", target.Describe());
  }

  [Fact]
  public void The_last_world_is_selected_in_the_menus()
  {
    var target = Resolve(new PlayRequest(), Settings(new LastPlay { DestinationKey = World.Key, DestinationName = World.Name }));

    Assert.Null(target.Instance);
    Assert.Empty(target.Direct!.Arguments);
    Assert.Equal("Midgard", target.Direct.Prefs[ValheimPrefs.World]);
    Assert.Equal("freya", target.Direct.Prefs[ValheimPrefs.Character]);
  }

  [Fact]
  public void A_server_gone_from_valheims_recent_list_is_still_joined_by_its_address()
  {
    var settings = Settings(new LastPlay { DestinationKey = "server:10.9.9.9:2456", DestinationName = "Old Server" });
    settings.PlayChoices["server:10.9.9.9:2456"] = new PlayChoice { Character = "ragnar" };

    var target = Resolve(new PlayRequest(), settings);

    Assert.Equal(["+connect", "10.9.9.9:2456"], target.Direct!.Arguments);
    Assert.Equal("Old Server", target.Direct.Name);
  }

  [Fact]
  public void A_world_gone_from_valheims_lists_is_an_error() =>
    Assert.Throws<LaunchException>(() => Resolve(new PlayRequest(), Settings(new LastPlay { DestinationKey = "world:gone", DestinationName = "Gone" })));

  [Fact]
  public void The_last_instance_is_played_and_a_deleted_one_falls_back_to_vanilla()
  {
    Assert.Same(Other, Resolve(new PlayRequest(), Settings(new LastPlay { InstanceId = Other.Id })).Instance);
    Assert.Equal(PlayTarget.VanillaGame, Resolve(new PlayRequest(), Settings(new LastPlay { InstanceId = "deleted" })));
  }

  [Fact]
  public void An_instance_is_found_by_name_or_id_and_played_without_a_destination()
  {
    Assert.Equal(new PlayTarget(Modded, null, null), Resolve(new PlayRequest { Instance = "modded" }));
    Assert.Equal(new PlayTarget(Other, null, null), Resolve(new PlayRequest { Instance = "other-id" }));
    Assert.Throws<LaunchException>(() => Resolve(new PlayRequest { Instance = "Missing" }));
  }

  [Fact]
  public void Parts_of_the_remembered_choice_can_be_replaced()
  {
    var target = Resolve(new PlayRequest { Server = "our server", Vanilla = true, Character = "Freya" });

    Assert.Null(target.Instance);
    Assert.Equal("freya", target.Choice!.Character);
    Assert.Null(target.Choice.InstanceId);
    // The password is still the remembered one.
    Assert.Equal("hunter22", target.Choice.Password);
  }

  [Fact]
  public void An_empty_password_forgets_the_remembered_one()
  {
    var target = Resolve(new PlayRequest { Server = Server.Address, Password = "" });

    Assert.Null(target.Choice!.Password);
    Assert.Equal(["+connect", "10.0.0.1:2456"], target.Direct!.Arguments);
  }

  [Fact]
  public void A_server_or_world_played_the_first_time_needs_a_character_and_a_setup()
  {
    Assert.Throws<LaunchException>(() => Resolve(new PlayRequest { Server = "10.5.5.5:2456", Instance = "Modded" }));
    Assert.Throws<LaunchException>(() => Resolve(new PlayRequest { Server = "10.5.5.5:2456", Character = "ragnar" }));

    var target = Resolve(new PlayRequest { Server = "10.5.5.5:2456", Character = "ragnar", Instance = "Modded" });
    Assert.Equal("server:10.5.5.5:2456", target.Direct!.Key);
  }

  [Fact]
  public void A_remembered_instance_that_was_deleted_is_not_played_vanilla()
  {
    var settings = Settings();
    settings.PlayChoices[World.Key].InstanceId = "deleted";

    Assert.Throws<LaunchException>(() => Resolve(new PlayRequest { World = "Midgard" }, settings));
  }

  [Theory]
  [InlineData(true, "Modded", null, null)]
  [InlineData(false, null, "Our Server", "Midgard")]
  [InlineData(false, null, null, null, "ragnar")]
  public void Contradicting_options_are_refused(bool vanilla, string? instance, string? server, string? world, string? character = null) =>
    Assert.Throws<LaunchException>(() => Resolve(new PlayRequest { Vanilla = vanilla, Instance = instance, Server = server, World = world, Character = character }));

  [Fact]
  public void Becomes_the_last_play_once_started()
  {
    var target = Resolve(new PlayRequest { World = "Midgard" });

    var last = target.ToLastPlay();

    Assert.Equal(World.Key, last.DestinationKey);
    Assert.Equal("Midgard", last.DestinationName);
    Assert.Null(last.InstanceId);
    Assert.Equal(Modded.Id, new PlayTarget(Modded, null, null).ToLastPlay().InstanceId);
  }
}
