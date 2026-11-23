using FlowBlox.SequenceDetection.Data;
using FlowBlox.SequenceDetection.Diagnostics;
using FlowBlox.SequenceDetection.Model;
using FlowBlox.SequenceDetection.Util;
using System.Text.RegularExpressions;

namespace FlowBlox.SequenceDetection
{
    internal class SequenceDetectionScanner
    {
        static readonly Regex RegexFindSequences = new Regex(
            @"[a-zA-Z0-9äöüÄÖÜß]+|[^\r\na-zA-Z0-9äöüÄÖÜß]+|\r\n|\r|\n",
            RegexOptions.Compiled | RegexOptions.Multiline);

        static readonly Regex RegexFindSequencesReverse = new Regex(
            RegexFindSequences.ToString(),
            RegexFindSequences.Options | RegexOptions.RightToLeft);

        const int SequenceLimit = 500;
        const double AverageLengthTolerance = 0.75;
        const int CombinationRange = 2;
            
        private readonly IEnumerable<SequenceDetectionInputEntry> _relatedInputEntries;
        private readonly IEnumerable<string> _relatedContents;
        private readonly CancellationTokenSource? _cancellationToken;

        public SequenceDetectionScanner(IEnumerable<SequenceDetectionInputEntry> relatedInputEntries, CancellationTokenSource? cancellationToken = null)
        {
            this._relatedInputEntries = relatedInputEntries;    
            this._relatedContents = relatedInputEntries.Select(x => x.Content);
            this._cancellationToken = cancellationToken;
        }

        public SequenceDetectionPattern? FindPatternForMockup(string content, AdjustableMatch match, long count, SequenceDetectionPattern? parent = null, int layer = 0)
        {
            string contentUntilMatch = content.Substring(0, match.Index);
            string contentAfterMatch = content.Substring(match.Index + match.Length);
            var listOfAllPhrases = CreateListOfAllPhrases(
                contentUntilMatch,
                contentAfterMatch,
                countAfterwards: count > 1);
            var orderedByDesc = listOfAllPhrases.OrderByDescending(x => x.Score);
            SequenceDetectionTraceWriter.Write(
                $"Layer {layer}: match index {match.Index}, {listOfAllPhrases.Count} phrase candidate(s).");
            foreach (var phraseEntry in orderedByDesc)
            {
                if (_cancellationToken != null &&
                    _cancellationToken.IsCancellationRequested)
                    return null;

                string contentUntilEntry = content.Substring(0, phraseEntry.Index);
                    
                if (!contentUntilEntry.Contains(phraseEntry.Phrase!))
                {
                    SequenceDetectionTraceWriter.Write(
                        $"Layer {layer}: trying unique phrase '{phraseEntry.Phrase}' " +
                        $"at index {phraseEntry.Index}, distance {phraseEntry.Distance}, score {phraseEntry.Score:R}.");
                    var sequenceDetection = new SequenceDetectionPattern(parent)
                    {
                        Sequence = phraseEntry.Phrase
                    };
                    if (phraseEntry.Distance > 0)
                    {
                        var newIndex = phraseEntry.Index + phraseEntry.Length;
                        string newContent = content.Substring(newIndex);
                        var subSequence = FindPatternForMockup(newContent, match.CopyAndAdjust(-newIndex), count, sequenceDetection, layer + 1);
                        if (subSequence == null)
                        {
                            SequenceDetectionTraceWriter.Write(
                                $"Layer {layer}: phrase '{phraseEntry.Phrase}' has no continuation; trying the next candidate.");
                            DetachFromParent(sequenceDetection);
                            continue;
                        }
                    }
                    else
                    {
                        if (!AddTerminationSequence(sequenceDetection, contentAfterMatch, match.Value, TerminationSequenceMode.ValueTerminationSequence))
                        {
                            DetachFromParent(sequenceDetection);
                            continue;
                        }
                    }
                    if (!sequenceDetection.HasIterationTerminationSequence && count > 1)
                    {
                        if (phraseEntry.CouldMatchRequiredCount(count))
                            AddIterationTerminationSequence(sequenceDetection, match, phraseEntry.Phrase!, contentAfterMatch, count);

                        if (layer == 0 && !sequenceDetection.HasIterationTerminationSequence)
                            continue;
                    }
                    
                    return sequenceDetection;
                }
            }
            SequenceDetectionTraceWriter.Write($"Layer {layer}: no usable phrase candidate found.");
            return default;
        }

        private static void DetachFromParent(SequenceDetectionPattern pattern)
        {
            if (pattern.Parent != null && ReferenceEquals(pattern.Parent.Next, pattern))
                pattern.Parent.Next = null;
        }

        private bool AddIterationTerminationSequence(SequenceDetectionPattern pattern, AdjustableMatch match, string phrase, string contentAfterMatch, long count)
        {
            Regex regex = new Regex(RegexUtil.ExcapeRegexValue(phrase), RegexOptions.Multiline);
            var matchesAfterMatch = regex.Matches(contentAfterMatch);
            long lastMatchIndex = count - 2;
            if (matchesAfterMatch.Count > lastMatchIndex)
            {
                var lastMatch = matchesAfterMatch.ElementAt((int)lastMatchIndex);
                List<string> _result = new List<string>();
                int lastResultEnds = -1;
                string _content = contentAfterMatch.Substring(lastMatch.Index + lastMatch.Length);

                if (pattern.Next == null)
                {
                    if (string.IsNullOrEmpty(pattern.ValueTerminationSequence))
                        throw new InvalidOperationException("The pattern must have a value termination sequence or a next detection pattern");

                    var indexOfTerminationSequence = _content.IndexOf(pattern.ValueTerminationSequence);
                    if (indexOfTerminationSequence >= 0)
                    {
                        _result.Add(_content.Substring(0, indexOfTerminationSequence));
                        lastResultEnds = indexOfTerminationSequence + pattern.ValueTerminationSequence.Length;
                    }
                }
                else
                {
                    SequenceSearch.Instance.SearchFor(_content, pattern.Next, ref _result, ref lastResultEnds);
                }
                if (lastResultEnds >= 0)
                {
                    var lastResultValue = _result.First();
                    if (IsAverageLengthMatch(lastResultValue, match.Value, AverageLengthTolerance))
                    {
                        var contentBetweenEndOfMatchAndTerminationSequence = contentAfterMatch.Substring(0, lastMatch.Index + lastMatch.Length + lastResultValue.Length);
                        AddTerminationSequence(pattern, _content.Substring(lastResultEnds), contentBetweenEndOfMatchAndTerminationSequence, TerminationSequenceMode.IterationTerminationSequence);
                        return true;
                    }
                }
            }
            return false;
        }

        private bool IsAverageLengthMatch(string newString, string baseString, double tolerance)
        {
            if (string.IsNullOrEmpty(baseString))
                return false;

            double averageLength = baseString.Length;
            double deviation = Math.Abs(newString.Length - averageLength) / averageLength;
            return (deviation <= tolerance);
        }

        private enum TerminationSequenceMode
        {
            ValueTerminationSequence,
            IterationTerminationSequence
        }

        private bool AddTerminationSequence(SequenceDetectionPattern pattern, string contentAfterMatch, string value, TerminationSequenceMode mode)
        {
            var listOfAfterwardsPhrases = CreateListOfAllPhrases(contentAfterMatch, rightMode: true);
            var orderByDesc = listOfAfterwardsPhrases.OrderByDescending(x => x.Score);
            SequenceDetectionTraceWriter.Write(
                $"Termination ({mode}): {listOfAfterwardsPhrases.Count} candidate(s), " +
                $"{listOfAfterwardsPhrases.Count(x => x.Distance == 0)} at distance zero, " +
                $"content starts with '{CreateTracePreview(contentAfterMatch)}'.");
            foreach (var afterwardsPhraseEntry in orderByDesc.Where(x =>
                mode != TerminationSequenceMode.ValueTerminationSequence ||
                x.Distance == 0))
            {
                if (_cancellationToken != null &&
                    _cancellationToken.IsCancellationRequested)
                    return false;

                if (!value.Contains(afterwardsPhraseEntry.Phrase!))
                {
                    if (mode == TerminationSequenceMode.ValueTerminationSequence)
                        pattern.ValueTerminationSequence = afterwardsPhraseEntry.Phrase;
                    else
                    {
                        pattern.IterationTerminationSequence = afterwardsPhraseEntry.Phrase;

                        if (_relatedInputEntries.Any() &&
                            !_relatedInputEntries.All(x => SequenceSearch.Instance.IsSuccessful(x.Content, pattern.Root, x.Count)))
                        {
                            string phrases = string.Join("\n", orderByDesc.Select(x => x.Phrase));
                            pattern.IterationTerminationSequence = string.Empty;
                            continue;
                        }
                    }
                    return true;
                }
            }

            return false;
        }

        private static string CreateTracePreview(string value)
        {
            var preview = value.Substring(0, Math.Min(value.Length, 80));
            return preview
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
        }

        private void AddCombinations(
            string content,
            string contentAfterMatch,
            bool rightMode,
            IReadOnlyList<Match> matchCollection,
            int index,
            int range,
            Dictionary<string, int> phraseToCount,
            List<PhraseEntry> phrases,
            bool countAfterwards)
        {
            var relevantMatches = matchCollection.Skip(index - range).Take(range * 2 + 1);
            for (int length = relevantMatches.Count(); length > 0; length--)
            {
                for (int i = 0; i < length; i++)
                {
                    if (_cancellationToken?.IsCancellationRequested == true)
                        return;

                    var matchesToCombine = relevantMatches.Skip(i).Take(length);
                    if (matchesToCombine.Count() < length)
                        break;

                    int mIndex = matchesToCombine.First().Index;
                    int mLength = matchesToCombine.Sum(x => x.Length);
                    int mEnds = mIndex + mLength;

                    string phrase = string.Concat(matchesToCombine.Select(x => x.Value)).Trim();

                    if (string.IsNullOrEmpty(phrase))
                        continue;

                    if (phrase.Contains('\r') || phrase.Contains('\n'))
                        continue;

                    if (phrases.Any(x => x.Index == mIndex && x.Length == mLength))
                        continue;

                    if (!_relatedContents.Any() ||
                        _relatedContents.Any(x => x.Contains(phrase)))
                    {
                        phrases.Add(new PhraseEntry(phraseToCount)
                        {
                            Phrase = phrase,
                            Distance = !rightMode ? content.Length - mEnds : mIndex,
                            Index = mIndex,
                            Length = mLength,
                            // A single expected result needs no iteration boundary or tail occurrence counts.
                            NumberOfPhrasesInAfterwards = countAfterwards && contentAfterMatch.Length > 0
                                ? RegexUtil.CountOccurences(contentAfterMatch, phrase)
                                : 0,
                        });
                            
                        if (!phraseToCount.ContainsKey(phrase))
                            phraseToCount[phrase] = 1;
                        else
                            phraseToCount[phrase]++;
                    }
                }
            }
        }

        private List<Match> GetRelevantTokenMatches(string content, bool rightMode)
        {
            var regex = rightMode ? RegexFindSequences : RegexFindSequencesReverse;
            var matches = new List<Match>();
            // Keep neighboring tokens needed by combinations at the edge of the existing search window.
            var limit = SequenceLimit + CombinationRange;
            for (var match = regex.Match(content); match.Success && matches.Count < limit; match = match.NextMatch())
            {
                if (_cancellationToken?.IsCancellationRequested == true)
                    break;

                matches.Add(match);
            }

            if (!rightMode)
                matches.Reverse();

            return matches;
        }

        private List<PhraseEntry> CreateListOfAllPhrases(
            string content,
            string contentAfterMatch = "",
            long requiredCount = 0,
            bool rightMode = false,
            bool countAfterwards = true)
        {
            var matchCollection = GetRelevantTokenMatches(content, rightMode);
            var result = new List<PhraseEntry>();
            Dictionary<string, int> phraseToCount = new Dictionary<string, int>();
            if (rightMode)
            {
                for (int index = 0; index < Math.Min(matchCollection.Count, SequenceLimit); index++)
                {
                    if (_cancellationToken?.IsCancellationRequested == true)
                        break;

                    AddCombinations(
                        content,
                        contentAfterMatch,
                        rightMode,
                        matchCollection,
                        index,
                        CombinationRange,
                        phraseToCount,
                        result,
                        countAfterwards);
                }
            }
            else
            {
                for (int index = matchCollection.Count - 1; index >= Math.Max(matchCollection.Count - SequenceLimit, 0); index--)
                {
                    if (_cancellationToken?.IsCancellationRequested == true)
                        break;

                    AddCombinations(
                        content,
                        contentAfterMatch,
                        rightMode,
                        matchCollection,
                        index,
                        CombinationRange,
                        phraseToCount,
                        result,
                        countAfterwards);
                }
            }
            result.ForEach(x => x.CalculateScore(requiredCount, rightMode));
            return result;
        }
    }   
}
