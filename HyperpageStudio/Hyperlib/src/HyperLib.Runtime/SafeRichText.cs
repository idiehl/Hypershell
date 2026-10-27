using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace HyperLib.Runtime;

/// <summary>A small Markdown subset. Raw markup is always encoded; links allow HTTP(S) only.</summary>
public static partial class SafeRichText
{
    public static string Render(string source)
    {
        var output = new StringBuilder(); var inList=false;
        foreach(var raw in source.Replace("\r\n","\n",StringComparison.Ordinal).Split('\n'))
        {
            var line=WebUtility.HtmlEncode(raw);
            line=Code().Replace(line,"<code>$1</code>");
            line=Strong().Replace(line,"<strong>$1</strong>");
            line=Emphasis().Replace(line,"<em>$1</em>");
            line=Link().Replace(line,"<a href=\"$2\" target=\"_blank\" rel=\"noopener noreferrer\">$1</a>");
            var list=line.StartsWith("- ",StringComparison.Ordinal);
            if(inList&&!list) { output.Append("</ul>");inList=false; }
            if(list) { if(!inList) {output.Append("<ul>");inList=true;} output.Append("<li>").Append(line[2..]).Append("</li>"); }
            else if(line.StartsWith("### ",StringComparison.Ordinal)) output.Append("<h3>").Append(line[4..]).Append("</h3>");
            else if(line.StartsWith("## ",StringComparison.Ordinal)) output.Append("<h2>").Append(line[3..]).Append("</h2>");
            else if(line.StartsWith("# ",StringComparison.Ordinal)) output.Append("<h2>").Append(line[2..]).Append("</h2>");
            else if(!string.IsNullOrWhiteSpace(line)) output.Append("<p>").Append(line).Append("</p>");
        }
        if(inList)output.Append("</ul>"); return output.ToString();
    }
    [GeneratedRegex("`([^`\\n]+)`")] private static partial Regex Code();
    [GeneratedRegex("\\*\\*([^*\\n]+)\\*\\*")] private static partial Regex Strong();
    [GeneratedRegex("(?<!\\*)\\*([^*\\n]+)\\*(?!\\*)")] private static partial Regex Emphasis();
    [GeneratedRegex("\\[([^\\]\\n]+)\\]\\((https?://[^\\s)]+)\\)", RegexOptions.IgnoreCase)] private static partial Regex Link();
}
