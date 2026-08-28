using System;
using System.Collections.Generic;
using System.Linq;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class TranscriptAggregatorTests
    {
        private static TranscriptAggregator New() => new(0.75);

        private static TranscriptAggregator WithSnapshots(params string[][] snapshots)
        {
            var agg = New();
            foreach (var snapshot in snapshots)
                agg.AddSnapshot(snapshot);
            return agg;
        }

        [Fact]
        public void EmptyOrBlankSnapshot_IsIgnored()
        {
            var agg = New();
            agg.AddSnapshot(Array.Empty<string>());
            agg.AddSnapshot(new[] { "   ", "", "\t" });
            Assert.Equal(0, agg.Count);
        }

        [Fact]
        public void FirstSnapshot_IsAppended()
        {
            var agg = WithSnapshots(new[] { "one", "two" });
            Assert.Equal(new[] { "one", "two" }, agg.GetLines());
        }

        [Fact]
        public void Whitespace_IsNormalized_AndBlankLinesDropped()
        {
            var agg = WithSnapshots(new[] { "  hello    world  ", "   ", "\ttab\tline " });
            Assert.Equal(new[] { "hello world", "tab line" }, agg.GetLines());
        }

        [Fact]
        public void GrowingLastLine_ReplacesInsteadOfAppending()
        {
            var agg = WithSnapshots(
                new[] { "Hello" },
                new[] { "Hello world" },
                new[] { "Hello world today" });
            Assert.Equal(new[] { "Hello world today" }, agg.GetLines());
        }

        [Fact]
        public void ScrollingOverlap_AppendsOnlyTheNewLine()
        {
            // Distinct sentences (a real scroll), so the shared middle line anchors the overlap.
            var agg = WithSnapshots(
                new[] { "the quick brown fox", "jumps over the lazy" },
                new[] { "jumps over the lazy", "dog in the yard" });
            Assert.Equal(
                new[] { "the quick brown fox", "jumps over the lazy", "dog in the yard" },
                agg.GetLines());
        }

        [Fact]
        public void RepeatedSnapshot_DoesNotDuplicate()
        {
            var agg = WithSnapshots(new[] { "a", "b" }, new[] { "a", "b" });
            Assert.Equal(new[] { "a", "b" }, agg.GetLines());
        }

        [Fact]
        public void TallBoxScroll_AppendsOnlyOneLine()
        {
            // A tall caption box: the whole window scrolls by one line.
            var agg = WithSnapshots(
                new[] { "a", "b", "c", "d", "e", "f" },
                new[] { "b", "c", "d", "e", "f", "g" });
            Assert.Equal(new[] { "a", "b", "c", "d", "e", "f", "g" }, agg.GetLines());
        }

        [Fact]
        public void OcrJitter_WithinThreshold_IsAbsorbed_NotDuplicated()
        {
            var agg = WithSnapshots(new[] { "hello world" }, new[] { "hello wor1d" });
            Assert.Single(agg.GetLines());   // the OCR wobble is treated as the same line
        }

        [Fact]
        public void DroppedVolatileLine_ThenReturns_DoesNotDuplicate()
        {
            var agg = WithSnapshots(
                new[] { "A", "B", "C" },   // C is the live line
                new[] { "A", "B" },        // OCR dropped C for one frame
                new[] { "A", "B", "C" });  // C is back
            Assert.Equal(new[] { "A", "B", "C" }, agg.GetLines());
        }

        [Fact]
        public void DroppedMiddleLine_DoesNotBlockDuplicate_AndKeepsTheLine()
        {
            // C goes missing from the middle of the overlap while F is new. The already-committed
            // block before F must not be re-appended (the case the LCS reconciliation fixes).
            var agg = WithSnapshots(
                new[] { "A", "B", "C", "D", "E" },
                new[] { "B", "D", "E", "F" });
            Assert.Equal(new[] { "A", "B", "C", "D", "E", "F" }, agg.GetLines());
        }

        [Fact]
        public void DroppedMiddleLine_ThenRestored_StillNoDuplicate()
        {
            var agg = WithSnapshots(
                new[] { "A", "B", "C", "D", "E" },
                new[] { "B", "D", "E", "F" },        // C dropped, F new
                new[] { "C", "D", "E", "F", "G" });  // C restored, G new
            Assert.Equal(new[] { "A", "B", "C", "D", "E", "F", "G" }, agg.GetLines());
        }

        [Fact]
        public void RepeatedFinalizedLine_IsNotCommittedTwice()
        {
            // A finalized multi-word line the alignment leaves behind must not be appended again.
            var agg = WithSnapshots(
                new[] { "the meeting is starting", "we should begin now" },
                new[] { "the meeting is starting", "we should begin now", "we should begin now" });
            Assert.Equal(new[] { "the meeting is starting", "we should begin now" }, agg.GetLines());
        }

        [Fact]
        public void RevisedLiveLine_IsOverwritten_NotAppended()
        {
            // The last (live) line is being revised while an earlier line anchors the overlap.
            var agg = WithSnapshots(
                new[] { "1.", "2.", "5-6" },
                new[] { "2.", "5678" });
            Assert.Equal(new[] { "1.", "2.", "5678" }, agg.GetLines());
        }

        [Fact]
        public void UnrelatedContent_IsAppended()
        {
            var agg = WithSnapshots(new[] { "A", "B" }, new[] { "X", "Y" });
            Assert.Equal(new[] { "A", "B", "X", "Y" }, agg.GetLines());
        }

        [Fact]
        public void Clear_ResetsLinesAndTimes()
        {
            var agg = WithSnapshots(new[] { "a", "b" });
            agg.Clear();
            Assert.Equal(0, agg.Count);
            Assert.Empty(agg.GetLines());
            Assert.Empty(agg.GetTimedLines());
        }

        [Fact]
        public void TimedLines_StayInLockstepWithLines()
        {
            var agg = WithSnapshots(new[] { "a", "b" }, new[] { "b", "c" });
            var lines = agg.GetLines();
            var timed = agg.GetTimedLines();

            Assert.Equal(lines.Count, timed.Count);
            Assert.Equal(lines, timed.Select(t => t.Text).ToArray());
            Assert.All(timed, t => Assert.NotEqual(default, t.Time));
        }

        [Fact]
        public void SimilarityThreshold_CanBeChangedAtRuntime()
        {
            var agg = new TranscriptAggregator(0.99);   // strict: OCR jitter is treated as a new line
            agg.SimilarityThreshold = 0.5;              // loosen it live

            agg.AddSnapshot(new[] { "hello world" });
            agg.AddSnapshot(new[] { "hello wor1d" });   // one-char OCR wobble

            Assert.Single(agg.GetLines());              // merged under the looser threshold
        }

        [Fact]
        public void GetTailText_ReturnsOnlyTheLastLines()
        {
            var agg = WithSnapshots(new[] { "l1" }, new[] { "l2" }, new[] { "l3" }, new[] { "l4" });
            Assert.Equal("l3" + Environment.NewLine + "l4", agg.GetTailText(2));
        }

        [Fact]
        public void GetTimedTail_ReturnsOnlyTheLastLines()
        {
            var agg = WithSnapshots(new[] { "l1" }, new[] { "l2" }, new[] { "l3" });
            var tail = agg.GetTimedTail(2);
            Assert.Equal(new[] { "l2", "l3" }, tail.Select(t => t.Text).ToArray());
        }
    }
}
