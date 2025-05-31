using System.Text.RegularExpressions;

namespace HushShared.Converters;

public static class Shorteners
{
    public  static string GenerateShortCaption(string caption)
    {
        if (string.IsNullOrEmpty(caption))
        {
            return string.Empty;
        }

        string shortCaption = "";
        if (caption.Contains(" "))
        {
            var words = caption.Split(' ');
            for (var i = 0; i < Math.Min(2, words.Length); i++)
            {
                if (!string.IsNullOrEmpty(words[i]))
                {
                    shortCaption += words[i].Substring(0, 1);
                }
            }
        }
        else
        {
            shortCaption += caption.Substring(0, 1);
            for (int i = 1; i < caption.Length; i++)
            {
                if (char.IsUpper(caption[i]))
                {
                    shortCaption += caption[i];
                    break;
                }
            }
        }

        // Add number if exists
        var number = Regex.Match(caption, @"\d+$").Value;
        if (!string.IsNullOrEmpty(number))
        {
            shortCaption += number.Substring(0, 1);
        }

        return shortCaption.ToUpperInvariant().Substring(0, Math.Min(3, shortCaption.Length));
    }
}
