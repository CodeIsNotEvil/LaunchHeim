using CINE.LaunchHeim.Core.Instances;

namespace CINE.LaunchHeim.Core.Tests;

public class InstanceIconTests : IDisposable
{
  private readonly TempDirectory _temp = new();
  private readonly InstanceStore _store;

  public InstanceIconTests() => _store = new InstanceStore(_temp.AppPaths);

  public void Dispose() => _temp.Dispose();

  private string Picture(string name)
  {
    var path = _temp.Combine(name);
    File.WriteAllBytes(path, [0x89, 0x50, 0x4e, 0x47]);
    return path;
  }

  [Fact]
  public void Without_choices_the_icon_is_automatic()
  {
    var instance = _store.Create("Survival with friends");

    Assert.Equal("SW", InstanceIcon.InitialsOf(instance));
    Assert.Contains(InstanceIcon.ColorOf(instance), InstanceIcon.AutomaticColors);
    // The same id always gets the same color, across starts.
    Assert.Equal(InstanceIcon.ColorOf(instance), InstanceIcon.AutomaticColor(instance.Id));
    Assert.Null(InstanceIcon.PicturePath(_store, instance));
  }

  [Theory]
  [InlineData("Valheim", "VA")]
  [InlineData("deep north", "DN")]
  [InlineData("  ", "?")]
  public void Automatic_initials_come_from_the_name(string name, string expected) =>
    Assert.Equal(expected, InstanceIcon.AutomaticInitials(name));

  [Theory]
  [InlineData("#DE5833", "#de5833")]
  [InlineData(" #1f9c95 ", "#1f9c95")]
  [InlineData("", null)]
  [InlineData(null, null)]
  [InlineData("red", null)]
  [InlineData("#12345", null)]
  public void Only_hex_colors_are_kept(string? color, string? expected) =>
    Assert.Equal(expected, InstanceIcon.NormalizeColor(color));

  [Theory]
  [InlineData("vh", "vh")]
  [InlineData(" V H ", "VH")]
  [InlineData("ODIN", "ODI")]
  [InlineData("", null)]
  [InlineData(null, null)]
  [InlineData("🐺🪓⚔️🛡️", "🐺🪓⚔️")]
  public void Letters_are_trimmed_to_three(string? letters, string? expected) =>
    Assert.Equal(expected, InstanceIcon.NormalizeInitials(letters));

  [Fact]
  public void A_new_picture_replaces_the_old_one_under_a_new_name()
  {
    var instance = _store.Create("Pictured");
    InstanceIcon.SetPicture(_store, instance, Picture("first.png"));
    var first = InstanceIcon.PicturePath(_store, instance)!;

    InstanceIcon.SetPicture(_store, instance, Picture("second.JPG"));
    var second = InstanceIcon.PicturePath(_store, instance)!;

    Assert.False(File.Exists(first));
    Assert.True(File.Exists(second));
    Assert.NotEqual(Path.GetFileName(first), Path.GetFileName(second));
    Assert.EndsWith(".jpg", second);
    Assert.Equal(_store.DirectoryOf(instance), Path.GetDirectoryName(second));
  }

  [Fact]
  public void Removing_the_picture_deletes_its_file()
  {
    var instance = _store.Create("Pictured");
    InstanceIcon.SetPicture(_store, instance, Picture("icon.png"));
    var path = InstanceIcon.PicturePath(_store, instance)!;

    InstanceIcon.RemovePicture(_store, instance);

    Assert.Null(instance.IconFile);
    Assert.False(File.Exists(path));
  }

  [Fact]
  public void Something_that_is_no_picture_is_refused()
  {
    var instance = _store.Create("Pictured");
    var video = _temp.Combine("clip.mp4");
    File.WriteAllText(video, "not a picture");

    Assert.Throws<ArgumentException>(() => InstanceIcon.SetPicture(_store, instance, video));
    Assert.Null(instance.IconFile);
  }

  [Fact]
  public void A_picture_outside_the_instance_folder_is_ignored()
  {
    var instance = _store.Create("Pictured");
    instance.IconFile = Path.Combine("..", "elsewhere.png");

    Assert.Null(InstanceIcon.PicturePath(_store, instance));
  }

  [Fact]
  public void A_duplicate_keeps_the_icon()
  {
    var source = _store.Create("Pictured");
    source.Color = "#2f8a5b";
    source.Initials = "PX";
    InstanceIcon.SetPicture(_store, source, Picture("icon.png"));
    _store.Save(source);

    var copy = _store.Duplicate(source, "Copy");

    Assert.Equal("#2f8a5b", copy.Color);
    Assert.Equal("PX", copy.Initials);
    Assert.NotNull(InstanceIcon.PicturePath(_store, copy));
    Assert.NotEqual(InstanceIcon.PicturePath(_store, source), InstanceIcon.PicturePath(_store, copy));
  }
}
