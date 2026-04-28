using System.Text;

namespace Xabbo.Utility;

public static class KeywordParser
{
    public static List<string> Parse(string filterText)
    {
        var keywords = new List<string>();
        var inQuotes = false;
        var currentWord = new StringBuilder();

        for (int i = 0; i < filterText.Length; i++)
        {
            char c = filterText[i];

            if (c == '"')
            {
                if (inQuotes)
                {
                    if (currentWord.Length > 0)
                    {
                        keywords.Add(currentWord.ToString());
                        currentWord.Clear();
                    }
                }
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (currentWord.Length > 0)
                {
                    keywords.Add(currentWord.ToString());
                    currentWord.Clear();
                }
            }
            else
            {
                currentWord.Append(c);
            }
        }

        if (currentWord.Length > 0)
        {
            keywords.Add(currentWord.ToString());
        }

        return keywords;
    }
}
