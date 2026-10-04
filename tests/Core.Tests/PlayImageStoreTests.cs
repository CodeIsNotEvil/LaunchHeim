using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Tests;

public class PlayImageStoreTests : IDisposable
{
  private readonly TempDirectory _temp = new();
  private readonly PlayImageStore _store;

  public PlayImageStoreTests() => _store = new PlayImageStore(_temp.AppPaths);

  public void Dispose() => _temp.Dispose();

  private string Picture(string name, string content = "png")
  {
    var path = _temp.Combine("Pictures", name);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
    return path;
  }

  [Fact]
  public void Import_copies_the_picture_into_the_data_folder()
  {
    var source = Picture("Server.PNG", "pixels");

    var name = _store.Import(source);

    Assert.EndsWith(".png", name);
    Assert.Equal(_temp.Combine("data", "play-images", name), _store.PathOf(name));
    Assert.Equal("pixels", File.ReadAllText(_store.PathOf(name)!));

    // The copy stays when the original goes.
    File.Delete(source);
    Assert.NotNull(_store.PathOf(name));
  }

  [Fact]
  public void Every_import_gets_a_new_name_so_QML_does_not_show_a_cached_picture()
  {
    var source = Picture("world.jpg");

    Assert.NotEqual(_store.Import(source), _store.Import(source));
  }

  [Fact]
  public void Import_rejects_files_that_are_not_images()
  {
    var source = Picture("notes.txt");

    Assert.Throws<ArgumentException>(() => _store.Import(source));
    Assert.False(Directory.Exists(_store.Directory));
  }

  [Fact]
  public void Delete_removes_the_copy()
  {
    var name = _store.Import(Picture("a.webp"));

    _store.Delete(name);

    Assert.Null(_store.PathOf(name));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("..")]
  [InlineData("../settings.json")]
  public void Names_from_a_hand_edited_settings_file_never_leave_the_store(string? name)
  {
    // A file next to the store that a crafted name would point at.
    var outside = _temp.Combine("data", "settings.json");
    Directory.CreateDirectory(_store.Directory);
    File.WriteAllText(outside, "{}");

    Assert.Null(_store.PathOf(name));
    _store.Delete(name);
    Assert.True(File.Exists(outside));
  }

  [Fact]
  public void A_picture_deleted_by_hand_counts_as_none()
  {
    var name = _store.Import(Picture("b.gif"));
    File.Delete(Path.Combine(_store.Directory, name));

    Assert.Null(_store.PathOf(name));
  }
}
