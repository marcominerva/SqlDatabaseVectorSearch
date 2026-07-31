using System.Diagnostics;
using System.Text;

namespace SqlDatabaseVectorSearch.TextChunkers.Implementations;

/// <summary>
/// Split text in chunks, attempting to leave meaning intact.
/// For plain text, split looking at new lines first, then periods, and so on.
/// For markdown, split looking at punctuation first, and so on.
/// </summary>
internal static class PlainTextChunker
{
    /// <summary>
    /// Represents a list of strings with token count.
    /// Used to reduce the number of calls to the tokenizer.
    /// </summary>
    private sealed class StringListWithTokenCount(TokenCounter? tokenCounter)
    {
        private readonly TokenCounter? tokenCounter = tokenCounter;

        public void Add(string value) => Values.Add((value, tokenCounter is null ? GetDefaultTokenCount(value.Length) : tokenCounter(value)));

        public void Add(string value, int tokenCount) => Values.Add((value, tokenCount));

        public void AddRange(StringListWithTokenCount range) => Values.AddRange(range.Values);

        public void RemoveRange(int index, int count) => Values.RemoveRange(index, count);

        public int Count => Values.Count;

        public List<string> ToStringList() => Values.Select(v => v.Value).ToList();

        private List<(string Value, int TokenCount)> Values { get; } = [];

        public string ValueAt(int i) => Values[i].Value;

        public int TokenCountAt(int i) => Values[i].TokenCount;
    }

    /// <summary>
    /// Delegate for counting tokens in a string.
    /// </summary>
    /// <param name="input">The input string to count tokens in.</param>
    /// <returns>The number of tokens in the input string.</returns>
    public delegate int TokenCounter(string input);

    private static readonly string?[] plainTextSplitOptions = ["\n", ".。．", "?!", ";", ":", ",，、", ")]}", " ", "-", null];
    private static readonly string?[] markdownSplitOptions = [".\u3002\uFF0E", "?!", ";", ":", ",\uFF0C\u3001", ")]}", " ", "-", "\n\r", null];

    /// <summary>
    /// Split plain text into lines.
    /// </summary>
    /// <param name="text">Text to split</param>
    /// <param name="maxTokensPerLine">Maximum number of tokens per line.</param>
    /// <param name="tokenCounter">Function to count tokens in a string. If not supplied, the default counter will be used.</param>
    /// <returns>List of lines.</returns>
    public static List<string> SplitPlainTextLines(string text, int maxTokensPerLine, TokenCounter? tokenCounter = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateMaxTokens(maxTokensPerLine, nameof(maxTokensPerLine));

        return InternalSplitLines(text, maxTokensPerLine, trim: true, plainTextSplitOptions, tokenCounter);
    }

    /// <summary>
    /// Split markdown text into lines.
    /// </summary>
    /// <param name="text">Text to split</param>
    /// <param name="maxTokensPerLine">Maximum number of tokens per line.</param>
    /// <param name="tokenCounter">Function to count tokens in a string. If not supplied, the default counter will be used.</param>
    /// <returns>List of lines.</returns>
    public static List<string> SplitMarkdownLines(string text, int maxTokensPerLine, TokenCounter? tokenCounter = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateMaxTokens(maxTokensPerLine, nameof(maxTokensPerLine));

        return InternalSplitLines(text, maxTokensPerLine, trim: true, markdownSplitOptions, tokenCounter);
    }

    /// <summary>
    /// Split plain text into paragraphs.
    /// </summary>
    /// <param name="lines">Lines of text.</param>
    /// <param name="maxTokensPerParagraph">Maximum number of tokens per paragraph.</param>
    /// <param name="overlapTokens">Number of tokens to overlap between paragraphs.</param>
    /// <param name="chunkHeader">Text to be prepended to each individual chunk.</param>
    /// <param name="tokenCounter">Function to count tokens in a string. If not supplied, the default counter will be used.</param>
    /// <returns>List of paragraphs.</returns>
    public static List<string> SplitPlainTextParagraphs(IEnumerable<string> lines, int maxTokensPerParagraph, int overlapTokens = 0, string? chunkHeader = null, TokenCounter? tokenCounter = null)
        => InternalSplitTextParagraphs(lines, maxTokensPerParagraph, overlapTokens, chunkHeader,
            static (text, maxTokens, tokenCounter) => InternalSplitLines(text, maxTokens, trim: false, plainTextSplitOptions, tokenCounter), tokenCounter);

    /// <summary>
    /// Split markdown text into paragraphs.
    /// </summary>
    /// <param name="lines">Lines of text.</param>
    /// <param name="maxTokensPerParagraph">Maximum number of tokens per paragraph.</param>
    /// <param name="overlapTokens">Number of tokens to overlap between paragraphs.</param>
    /// <param name="chunkHeader">Text to be prepended to each individual chunk.</param>
    /// <param name="tokenCounter">Function to count tokens in a string. If not supplied, the default counter will be used.</param>
    /// <returns>List of paragraphs.</returns>
    public static List<string> SplitMarkdownParagraphs(IEnumerable<string> lines, int maxTokensPerParagraph, int overlapTokens = 0, string? chunkHeader = null, TokenCounter? tokenCounter = null)
        => InternalSplitTextParagraphs(lines, maxTokensPerParagraph, overlapTokens, chunkHeader,
            static (text, maxTokens, tokenCounter) => InternalSplitLines(text, maxTokens, trim: false, markdownSplitOptions, tokenCounter), tokenCounter);

    private static List<string> InternalSplitTextParagraphs(IEnumerable<string> lines, int maxTokensPerParagraph, int overlapTokens, string? chunkHeader, Func<string, int, TokenCounter?, List<string>> longLinesSplitter, TokenCounter? tokenCounter)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ValidateMaxTokens(maxTokensPerParagraph, nameof(maxTokensPerParagraph));

        if (overlapTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(overlapTokens), "overlapTokens cannot be negative.");
        }

        if (maxTokensPerParagraph <= overlapTokens)
        {
            throw new ArgumentException("overlapTokens cannot be larger than or equal to maxTokensPerParagraph.", nameof(overlapTokens));
        }

        // Optimize empty inputs if we can efficiently determine they're empty.
        if (lines is ICollection<string> c && c.Count == 0)
        {
            return [];
        }

        var chunkHeaderTokens = chunkHeader is { Length: > 0 } ? GetTokenCount(chunkHeader, tokenCounter) : 0;
        var adjustedMaxTokensPerParagraph = maxTokensPerParagraph - overlapTokens - chunkHeaderTokens;
        if (adjustedMaxTokensPerParagraph <= 0)
        {
            throw new ArgumentException("chunkHeader and overlapTokens must leave room for paragraph content.", nameof(chunkHeader));
        }

        // Split long lines first
        var truncatedLines = lines.SelectMany(line => longLinesSplitter(NormalizeLineEndings(line), adjustedMaxTokensPerParagraph, tokenCounter));

        var paragraphs = BuildParagraph(truncatedLines, adjustedMaxTokensPerParagraph, tokenCounter);
        var processedParagraphs = ProcessParagraphs(paragraphs, adjustedMaxTokensPerParagraph, overlapTokens, chunkHeader, longLinesSplitter, tokenCounter);

        return processedParagraphs;
    }

    private static List<string> BuildParagraph(IEnumerable<string> truncatedLines, int maxTokensPerParagraph, TokenCounter? tokenCounter)
    {
        StringBuilder paragraphBuilder = new();
        List<string> paragraphs = [];

        foreach (var line in truncatedLines)
        {
            if (paragraphBuilder.Length > 0)
            {
                string? paragraph = null;

                var currentCount = GetTokenCount(line, tokenCounter) + 1;
                if (currentCount < maxTokensPerParagraph)
                {
                    currentCount += tokenCounter is null ?
                        GetDefaultTokenCount(paragraphBuilder.Length) :
                        tokenCounter(paragraph = paragraphBuilder.ToString());
                }

                if (currentCount >= maxTokensPerParagraph)
                {
                    // Complete the paragraph and prepare for the next
                    paragraph ??= paragraphBuilder.ToString();
                    paragraphs.Add(paragraph.Trim());
                    paragraphBuilder.Clear();
                }
            }

            paragraphBuilder.AppendLine(line);
        }

        if (paragraphBuilder.Length > 0)
        {
            // Add the final paragraph if there's anything remaining
            paragraphs.Add(paragraphBuilder.ToString().Trim());
        }

        return paragraphs;
    }

    private static List<string> ProcessParagraphs(List<string> paragraphs, int adjustedMaxTokensPerParagraph, int overlapTokens, string? chunkHeader, Func<string, int, TokenCounter?, List<string>> longLinesSplitter, TokenCounter? tokenCounter)
    {
        // distribute text more evenly in the last paragraphs when the last paragraph is too short.
        if (paragraphs.Count > 1)
        {
            var lastParagraph = paragraphs[^1];
            var secondLastParagraph = paragraphs[^2];

            if (GetTokenCount(lastParagraph, tokenCounter) < adjustedMaxTokensPerParagraph / 4)
            {
                var mergedParagraph = $"{secondLastParagraph} {lastParagraph}";

                if (GetTokenCount(mergedParagraph, tokenCounter) <= adjustedMaxTokensPerParagraph)
                {
                    paragraphs[^2] = mergedParagraph;
                    paragraphs.RemoveAt(paragraphs.Count - 1);
                }
            }
        }

        var processedParagraphs = new List<string>();
        var paragraphStringBuilder = new StringBuilder();

        for (var i = 0; i < paragraphs.Count; i++)
        {
            paragraphStringBuilder.Clear();

            if (chunkHeader is not null)
            {
                paragraphStringBuilder.Append(chunkHeader);
            }

            var paragraph = paragraphs[i];

            if (overlapTokens > 0 && i < paragraphs.Count - 1)
            {
                var nextParagraph = paragraphs[i + 1];
                var split = longLinesSplitter(nextParagraph, overlapTokens, tokenCounter);

                paragraphStringBuilder.Append(paragraph);

                if (split.Count != 0)
                {
                    paragraphStringBuilder.Append(' ').Append(split[0]);
                }
            }
            else
            {
                paragraphStringBuilder.Append(paragraph);
            }

            processedParagraphs.Add(paragraphStringBuilder.ToString());
        }

        return processedParagraphs;
    }

    private static List<string> InternalSplitLines(string text, int maxTokensPerLine, bool trim, string?[] splitOptions, TokenCounter? tokenCounter)
    {
        var result = new StringListWithTokenCount(tokenCounter);

        text = NormalizeLineEndings(text);
        result.Add(text);
        for (var i = 0; i < splitOptions.Length; i++)
        {
            var count = result.Count; // track where the original input left off
            var (splits2, inputWasSplit2) = Split(result, maxTokensPerLine, splitOptions[i].AsSpan(), trim, tokenCounter);
            result.AddRange(splits2);
            result.RemoveRange(0, count); // remove the original input
            if (!inputWasSplit2)
            {
                break;
            }
        }

        return result.ToStringList();
    }

    private static (StringListWithTokenCount, bool) Split(StringListWithTokenCount input, int maxTokens, ReadOnlySpan<char> separators, bool trim, TokenCounter? tokenCounter)
    {
        var inputWasSplit = false;
        StringListWithTokenCount result = new(tokenCounter);
        var count = input.Count;
        for (var i = 0; i < count; i++)
        {
            var (splits, split) = Split(input.ValueAt(i).AsSpan(), input.ValueAt(i), maxTokens, separators, trim, tokenCounter, input.TokenCountAt(i));
            result.AddRange(splits);
            inputWasSplit |= split;
        }

        return (result, inputWasSplit);
    }

    private static (StringListWithTokenCount, bool) Split(ReadOnlySpan<char> input, string? inputString, int maxTokens, ReadOnlySpan<char> separators, bool trim, TokenCounter? tokenCounter, int inputTokenCount)
    {
        Debug.Assert(inputString is null || input.SequenceEqual(inputString.AsSpan()));
        StringListWithTokenCount result = new(tokenCounter);
        var inputWasSplit = false;

        if (inputTokenCount > maxTokens)
        {
            inputWasSplit = true;

            var half = input.Length / 2;
            var cutPoint = -1;

            if (separators.IsEmpty)
            {
                cutPoint = half;
            }
            else if (input.Length > 2)
            {
                var pos = 0;
                while (true)
                {
                    var index = input[pos..^1].IndexOfAny(separators);
                    if (index < 0)
                    {
                        break;
                    }

                    index += pos;

                    if (Math.Abs(half - index) < Math.Abs(half - cutPoint))
                    {
                        cutPoint = index + 1;
                    }

                    pos = index + 1;
                }
            }

            if (cutPoint > 0)
            {
                var firstHalf = input[..cutPoint];
                var secondHalf = input[cutPoint..];
                if (trim)
                {
                    firstHalf = firstHalf.Trim();
                    secondHalf = secondHalf.Trim();
                }

                // Recursion
                var (splits1, split1) = Split(firstHalf, null, maxTokens, separators, trim, tokenCounter, GetTokenCount(firstHalf, tokenCounter));
                result.AddRange(splits1);
                var (splits2, split2) = Split(secondHalf, null, maxTokens, separators, trim, tokenCounter, GetTokenCount(secondHalf, tokenCounter));
                result.AddRange(splits2);

                inputWasSplit = split1 || split2;
                return (result, inputWasSplit);
            }
        }

        var resultString = inputString ?? input.ToString();
        var resultTokenCount = inputTokenCount;
        if (trim)
        {
            var trimmedResult = resultString.Trim();
            if (!trimmedResult.Equals(resultString, StringComparison.Ordinal))
            {
                resultString = trimmedResult;
                resultTokenCount = GetTokenCount(resultString, tokenCounter);
            }
        }

        result.Add(resultString, resultTokenCount);

        return (result, inputWasSplit);
    }

    private static int GetTokenCount(string input, TokenCounter? tokenCounter) => tokenCounter is null ? GetDefaultTokenCount(input.Length) : tokenCounter(input);

    private static int GetTokenCount(ReadOnlySpan<char> input, TokenCounter? tokenCounter) => tokenCounter is null ? GetDefaultTokenCount(input.Length) : tokenCounter(input.ToString());

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static void ValidateMaxTokens(int maxTokens, string parameterName)
    {
        if (maxTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The maximum token count must be a positive number.");
        }
    }

    private static int GetDefaultTokenCount(int length)
    {
        Debug.Assert(length >= 0);
        return length == 0 ? 0 : Math.Max(1, length >> 2);
    }
}