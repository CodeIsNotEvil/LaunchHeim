using System.Text;

namespace CINE.LaunchHeim.Core.Game;

/// <summary>
/// Just enough of Valve's text KeyValues format to read <c>libraryfolders.vdf</c> and app manifests.
/// </summary>
/// <remarks>
/// Keys are case-insensitive in Valve's own parser, so they are here too. Conditionals
/// (<c>[$WIN32]</c>) and <c>#include</c> never show up in these two files and are not supported.
/// </remarks>
public sealed class VdfNode
{
  public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
  public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

  public VdfNode? this[string key] => Children.GetValueOrDefault(key);

  public string? Value(string key) => Values.GetValueOrDefault(key);

  public static VdfNode Parse(string text)
  {
    var root = new VdfNode();
    var stack = new Stack<VdfNode>();
    stack.Push(root);
    string? pendingKey = null;

    foreach (var token in Tokenize(text))
    {
      switch (token)
      {
        case { IsOpen: true }:
          var child = new VdfNode();
          stack.Peek().Children[pendingKey ?? ""] = child;
          stack.Push(child);
          pendingKey = null;
          break;
        case { IsClose: true }:
          if (stack.Count > 1)
          {
            stack.Pop();
          }
          pendingKey = null;
          break;
        default:
          if (pendingKey is null)
          {
            pendingKey = token.Text;
          }
          else
          {
            stack.Peek().Values[pendingKey] = token.Text;
            pendingKey = null;
          }
          break;
      }
    }

    return root;
  }

  private readonly record struct Token(string Text, bool IsOpen = false, bool IsClose = false);

  private static IEnumerable<Token> Tokenize(string text)
  {
    var i = 0;
    while (i < text.Length)
    {
      var c = text[i];
      if (char.IsWhiteSpace(c))
      {
        i++;
      }
      else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
      {
        while (i < text.Length && text[i] != '\n')
        {
          i++;
        }
      }
      else if (c == '{')
      {
        i++;
        yield return new Token("{", IsOpen: true);
      }
      else if (c == '}')
      {
        i++;
        yield return new Token("}", IsClose: true);
      }
      else if (c == '"')
      {
        var sb = new StringBuilder();
        i++;
        while (i < text.Length && text[i] != '"')
        {
          // Paths in libraryfolders.vdf are written with escaped backslashes on Windows.
          if (text[i] == '\\' && i + 1 < text.Length)
          {
            i++;
            sb.Append(text[i] switch { 'n' => '\n', 't' => '\t', _ => text[i] });
          }
          else
          {
            sb.Append(text[i]);
          }
          i++;
        }
        i++;
        yield return new Token(sb.ToString());
      }
      else
      {
        var start = i;
        while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '"'))
        {
          i++;
        }
        yield return new Token(text[start..i]);
      }
    }
  }
}
