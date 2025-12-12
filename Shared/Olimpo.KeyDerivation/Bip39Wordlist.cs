using System;
using System.IO;
using System.Reflection;

namespace Olimpo.KeyDerivation;

/// <summary>
/// Provides access to the BIP-39 English wordlist (2048 words).
/// The wordlist is loaded from an embedded resource.
/// </summary>
public static class Bip39Wordlist
{
    private static readonly Lazy<string[]> _words = new Lazy<string[]>(LoadWordlist);

    /// <summary>
    /// Gets the BIP-39 English wordlist (2048 words).
    /// </summary>
    public static string[] Words => _words.Value;

    /// <summary>
    /// Gets the word at the specified index (0-2047).
    /// </summary>
    public static string GetWord(int index)
    {
        if (index < 0 || index >= 2048)
            throw new ArgumentOutOfRangeException(nameof(index), "Index must be between 0 and 2047");

        return Words[index];
    }

    /// <summary>
    /// Gets the index of a word in the wordlist, or -1 if not found.
    /// </summary>
    public static int GetIndex(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return -1;

        word = word.ToLowerInvariant().Trim();

        for (int i = 0; i < Words.Length; i++)
        {
            if (Words[i] == word)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Returns true if the word is in the BIP-39 wordlist.
    /// </summary>
    public static bool Contains(string word) => GetIndex(word) >= 0;

    private static string[] LoadWordlist()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "Olimpo.KeyDerivation.english.txt";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException(
                $"Could not find embedded resource '{resourceName}'. " +
                $"Available resources: {string.Join(", ", assembly.GetManifestResourceNames())}");
        }

        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();

        var words = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length != 2048)
        {
            throw new InvalidOperationException(
                $"BIP-39 wordlist must contain exactly 2048 words, but found {words.Length}");
        }

        // Normalize all words to lowercase
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = words[i].ToLowerInvariant().Trim();
        }

        return words;
    }
}
