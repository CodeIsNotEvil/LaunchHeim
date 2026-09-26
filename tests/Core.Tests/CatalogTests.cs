using CINE.LaunchHeim.Core.Catalogs;
using CINE.LaunchHeim.Core.Catalogs.Nexus;
using CINE.LaunchHeim.Core.Catalogs.Thunderstore;

namespace CINE.LaunchHeim.Core.Tests;

public class ThunderstoreSearchTests
{
  internal static ThunderstorePackage Package(
    string fullName,
    long downloads = 0,
    int rating = 0,
    bool deprecated = false,
    bool nsfw = false,
    bool pinned = false,
    string description = "",
    string[]? versions = null,
    string[]? dependencies = null)
  {
    var owner = fullName[..fullName.IndexOf('-')];
    var name = fullName[(fullName.IndexOf('-') + 1)..];
    var list = (versions ?? ["1.0.0"])
      .Select(v => new ThunderstoreVersion(v, dependencies ?? [], downloads, DateTimeOffset.UnixEpoch, 100))
      .ToArray();
    return new ThunderstorePackage(fullName, owner, name, $"https://thunderstore.io/c/valheim/p/{owner}/{name}/",
      DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, rating, deprecated, nsfw, pinned, [], description, null, downloads, list);
  }

  private static readonly ThunderstorePackage[] Packages =
  [
    Package("denikson-BepInExPack_Valheim", downloads: 100, pinned: true),
    Package("ValheimModding-Jotunn", downloads: 5000, description: "The Valheim library"),
    Package("Someone-PlantEverything", downloads: 900, description: "Plant berries"),
    Package("Advize-PlantEasily", downloads: 400),
    Package("Old-PlantThings", downloads: 99999, deprecated: true),
    Package("Spicy-PlantNsfw", downloads: 10, nsfw: true),
  ];

  private static IEnumerable<string> Search(string text, ModSort sort = ModSort.Relevance, bool nsfw = false) =>
    ThunderstoreCatalog.Search(Packages, new ModSearchQuery(text, sort, 0, 50, nsfw)).Items.Select(i => i.Id);

  [Fact]
  public void Without_a_query_pinned_packages_come_first()
  {
    Assert.Equal("denikson-BepInExPack_Valheim", Search("", ModSort.Downloads).First());
  }

  [Fact]
  public void Deprecated_packages_sink_to_the_bottom_even_with_more_downloads()
  {
    Assert.Equal("Old-PlantThings", Search("plant").Last());
  }

  [Fact]
  public void Name_matches_rank_above_description_matches()
  {
    var results = Search("plant").ToList();

    Assert.Equal(["Advize-PlantEasily", "Someone-PlantEverything"], results.Take(2).Order());
  }

  [Fact]
  public void Every_term_has_to_match()
  {
    Assert.Equal(["Someone-PlantEverything"], Search("plant berries"));
  }

  [Fact]
  public void Nsfw_packages_are_hidden_unless_asked_for()
  {
    Assert.DoesNotContain("Spicy-PlantNsfw", Search("plant"));
    Assert.Contains("Spicy-PlantNsfw", Search("plant", nsfw: true));
  }

  [Fact]
  public void Pages_are_counted_from_all_matches()
  {
    var page = ThunderstoreCatalog.Search(Packages, new ModSearchQuery("", ModSort.Downloads, 1, 2));

    Assert.Equal(5, page.TotalCount);
    Assert.Equal(3, page.PageCount);
    Assert.Equal(2, page.Items.Count);
  }

  [Theory]
  [InlineData("denikson-BepInExPack_Valheim-5.4.2202", "denikson-BepInExPack_Valheim", "5.4.2202")]
  [InlineData("ValheimModding-Jotunn-2.30.2", "ValheimModding-Jotunn", "2.30.2")]
  public void Dependency_strings_split_into_package_and_version(string input, string fullName, string version)
  {
    Assert.Equal(new DependencyString(fullName, version), DependencyString.Parse(input));
  }

  [Theory]
  [InlineData("NoVersion")]
  [InlineData("Owner-Name")]
  [InlineData("trailing-")]
  public void Malformed_dependency_strings_are_rejected(string input)
  {
    Assert.Null(DependencyString.Parse(input));
  }

  [Theory]
  [InlineData("1.10.0", "1.9.0", 1)]
  [InlineData("2.0", "2.0.0", 0)]
  [InlineData("v1.2", "1.2.0", 0)]
  [InlineData("1", "1.0.1", -1)]
  [InlineData("5.4.2202", "5.4.2351", -1)]
  public void Versions_compare_numerically(string a, string b, int expectedSign)
  {
    Assert.Equal(expectedSign, Math.Sign(ModVersion.Compare(a, b)));
  }
}

public class NexusTests
{
  [Fact]
  public void Parses_a_mod_manager_download_link()
  {
    Assert.True(NxmLink.TryParse("nxm://valheim/mods/1042/files/12345?key=abc&expires=1790000000&user_id=42", out var link));

    Assert.Equal(new NxmLink("valheim", "1042", "12345", "abc", "1790000000", "42"), link);
  }

  [Theory]
  [InlineData("https://www.nexusmods.com/valheim/mods/1042")]
  [InlineData("nxm://valheim/mods/abc/files/1")]
  [InlineData("nxm://valheim/collections/xyz/revisions/1")]
  [InlineData("")]
  public void Rejects_anything_else(string input)
  {
    Assert.False(NxmLink.TryParse(input, out _));
  }

  [Fact]
  public void Bbcode_becomes_qt_rich_text()
  {
    var html = BbCode.ToHtml("[b]Bold[/b]<br />[url=https://example.com]link[/url]\n[list][*]one[*]two[/list]");

    Assert.Contains("<b>Bold</b>", html);
    Assert.Contains("<a href=\"https://example.com\">link</a>", html);
    Assert.Contains("<ul><li>one<li>two</ul>", html);
  }

  [Fact]
  public void Author_html_and_script_links_do_not_survive()
  {
    var html = BbCode.ToHtml("<script>alert(1)</script>[url=javascript:alert(1)]x[/url][spoiler]hidden[/spoiler]");

    Assert.DoesNotContain("<script>", html);
    Assert.Contains("href=\"#\"", html);
    Assert.DoesNotContain("[spoiler]", html);
  }
}
