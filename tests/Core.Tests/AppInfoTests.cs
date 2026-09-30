using System.Xml.Linq;

namespace CINE.LaunchHeim.Core.Tests;

public class AppInfoTests
{
  // Core once fell back to 1.0.0 because <Version> was only set on the Desktop project.
  [Fact]
  public void The_reported_version_is_the_release_version_from_directory_build_props()
  {
    var props = new DirectoryInfo(AppContext.BaseDirectory);
    while (!File.Exists(Path.Combine(props.FullName, "Directory.Build.props")))
    {
      props = props.Parent ?? throw new FileNotFoundException("Directory.Build.props not found above the test output.");
    }

    var version = XDocument.Load(Path.Combine(props.FullName, "Directory.Build.props")).Descendants("Version").Single().Value;

    Assert.Equal(version, AppInfo.Version);
    Assert.Contains($"LaunchHeim/{version} ", AppInfo.UserAgent);
  }
}
