using CINE.LaunchHeim.Core.Game;

namespace CINE.LaunchHeim.Core.Tests;

public class VdfTests
{
  private const string LibraryFolders = """
    "libraryfolders"
    {
    	"0"
    	{
    		"path"		"/home/user/.local/share/Steam"
    		"apps"
    		{
    		}
    	}
    	"1"
    	{
    		"path"		"/mnt/games/SteamLibrary" // the second drive
    		"apps"
    		{
    			"892970"		"4221716687"
    		}
    	}
    }
    """;

  [Fact]
  public void Reads_nested_sections_and_values()
  {
    var root = VdfNode.Parse(LibraryFolders);

    var folders = root["libraryfolders"]!;
    Assert.Equal(2, folders.Children.Count);
    Assert.Equal("/mnt/games/SteamLibrary", folders["1"]!.Value("path"));
    Assert.Equal("4221716687", folders["1"]!["apps"]!.Value("892970"));
  }

  [Fact]
  public void Keys_are_case_insensitive_like_in_steam()
  {
    var root = VdfNode.Parse("\"AppState\" { \"installdir\" \"Valheim\" }");

    Assert.Equal("Valheim", root["appstate"]!.Value("InstallDir"));
  }

  [Fact]
  public void Unescapes_backslashes_in_quoted_strings()
  {
    var root = VdfNode.Parse("\"a\" { \"path\" \"D:\\\\Games\\\\Steam\" }");

    Assert.Equal(@"D:\Games\Steam", root["a"]!.Value("path"));
  }

  [Fact]
  public void Finds_valheim_in_a_secondary_library()
  {
    using var temp = new TempDirectory();
    var steam = temp.Combine("steam");
    var library = temp.Combine("games");
    Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
    File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
      $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{steam}\" }} \"1\" {{ \"path\" \"{library}\" }} }}");
    Directory.CreateDirectory(Path.Combine(library, "steamapps", "common", "Valheim"));
    File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_892970.acf"), "\"AppState\" { \"installdir\" \"Valheim\" }");
    File.WriteAllText(Path.Combine(library, "steamapps", "common", "Valheim", "valheim.x86_64"), "");

    var found = new SteamLibraryLocator([steam]).FindValheim();

    Assert.Equal(Path.Combine(library, "steamapps", "common", "Valheim"), found);
  }

  [Fact]
  public void Returns_null_when_valheim_is_not_installed()
  {
    using var temp = new TempDirectory();

    Assert.Null(new SteamLibraryLocator([temp.Combine("nowhere")]).FindValheim());
  }
}
