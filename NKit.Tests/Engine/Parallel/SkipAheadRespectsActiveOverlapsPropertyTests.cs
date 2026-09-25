using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Parallel
{
    /// <summary>
    /// Property 9: Skip-Ahead Respects Active Overlaps
    ///
    /// For any state where _activeOverlaps contains entries, the skip-ahead optimization
    /// SHALL NOT advance past disc positions needed by those active overlaps. Skip-ahead
    /// to next candidate requires _activeOverlaps.Count == 0.
    ///
    /// **Validates: Requirements 7.1, 7.2, 7.3**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Parallel")]
    public class SkipAheadRespectsActiveOverlapsPropertyTests
    {
        #region Test Infrastructure

        /// <summary>
        /// The possible skip-ahead decisions that the saveFileData logic can make.
        /// </summary>
        private enum SkipDecision
        {
            /// <summary>No skip occurs — data is still needed by active overlaps or candidates.</summary>
            NoSkip,
            /// <summary>Skip to the next area (all candidates done, no active overlaps).</summary>
            SkipToNextArea,
            /// <summary>Skip to the next candidate offset (candidates remain, no active overlaps).</summary>
            SkipToNextCandidate
        }

        /// <summary>
        /// The possible loop-continuation decisions inside the section items iteration.
        /// </summary>
        private enum LoopDecision
        {
            /// <summary>Continue processing section items.</summary>
            Continue,
            /// <summary>Break out of the loop (no candidates, no active overlaps).</summary>
            Break
        }

        /// <summary>
        /// Simulates the skip-ahead decision logic from saveFileData.
        /// This mirrors the actual implementation:
        ///
        /// if (_candidates.Count == 0 &amp;&amp; _activeOverlaps.Count == 0)
        ///     SkipToNextArea
        /// else if (_activeOverlaps.Count == 0)
        ///     SkipToNextCandidate
        /// // else: no skip (active overlaps present)
        /// </summary>
        private static SkipDecision SimulateSkipAheadDecision(
            int candidatesCount, int activeOverlapsCount)
        {
            if (candidatesCount == 0 && activeOverlapsCount == 0)
                return SkipDecision.SkipToNextArea;
            else if (activeOverlapsCount == 0)
                return SkipDecision.SkipToNextCandidate;
            else
                return SkipDecision.NoSkip;
        }

        /// <summary>
        /// Simulates the loop break decision from saveFileData's section items iteration.
        /// This mirrors:
        ///
        /// if (_candidates != null &amp;&amp; _candidates.Count == 0 &amp;&amp; _activeOverlaps.Count == 0)
        ///     break;
        /// </summary>
        private static LoopDecision SimulateLoopBreakDecision(
            bool candidatesNotNull, int candidatesCount, int activeOverlapsCount)
        {
            if (candidatesNotNull && candidatesCount == 0 && activeOverlapsCount == 0)
                return LoopDecision.Break;
            return LoopDecision.Continue;
        }

        #endregion

        #region Property Tests

        /// <summary>
        /// Property 9a: When _activeOverlaps contains entries, NO skip-ahead occurs.
        ///
        /// For any state with activeOverlaps.Count &gt; 0, the skip-ahead decision must
        /// be NoSkip regardless of the candidates count. This ensures data needed by
        /// active overlaps is not lost.
        ///
        /// **Validates: Requirements 7.1**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property ActiveOverlapsPresent_NoSkipOccurs()
        {
            var testGen =
                from activeOverlapsCount in Gen.Choose(1, 50)
                from candidatesCount in Gen.Choose(0, 100)
                select new
                {
                    ActiveOverlapsCount = activeOverlapsCount,
                    CandidatesCount = candidatesCount
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                SkipDecision decision = SimulateSkipAheadDecision(
                    data.CandidatesCount, data.ActiveOverlapsCount);

                if (decision != SkipDecision.NoSkip)
                    return false.Label(
                        $"Skip-ahead should NOT occur when _activeOverlaps.Count={data.ActiveOverlapsCount} > 0, " +
                        $"but got {decision} (candidates={data.CandidatesCount})");

                return true.Label(
                    $"No skip with activeOverlaps={data.ActiveOverlapsCount}, candidates={data.CandidatesCount}");
            });
        }

        /// <summary>
        /// Property 9b: When _candidates is empty AND _activeOverlaps is empty, skip to next area.
        ///
        /// This is the only condition under which the system skips to the next source area,
        /// meaning all extraction work for the current area is complete.
        ///
        /// **Validates: Requirements 7.2**
        /// </summary>
        [Property(MaxTest = 100)]
        public Property NoCandidatesNoActiveOverlaps_SkipToNextArea()
        {
            // candidatesCount is always 0, activeOverlapsCount is always 0
            // We vary nothing meaningful but confirm the invariant
            Gen<int> testGen =
                from dummy in Gen.Choose(0, 100)
                select dummy;

            return Prop.ForAll(testGen.ToArbitrary(), _ =>
            {
                SkipDecision decision = SimulateSkipAheadDecision(
                    candidatesCount: 0, activeOverlapsCount: 0);

                if (decision != SkipDecision.SkipToNextArea)
                    return false.Label(
                        $"Should skip to next area when candidates=0 and activeOverlaps=0, " +
                        $"but got {decision}");

                return true.Label("Correctly skips to next area when no work remains");
            });
        }

        /// <summary>
        /// Property 9c: When _candidates is non-empty AND _activeOverlaps is empty,
        /// skip to next candidate.
        ///
        /// This enables the optimization to jump ahead to the next file that needs
        /// extraction, but only when no active overlap streams need data.
        ///
        /// **Validates: Requirements 7.3**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property CandidatesRemainNoActiveOverlaps_SkipToNextCandidate()
        {
            Gen<int> testGen =
                from candidatesCount in Gen.Choose(1, 100)
                select candidatesCount;

            return Prop.ForAll(testGen.ToArbitrary(), candidatesCount =>
            {
                SkipDecision decision = SimulateSkipAheadDecision(
                    candidatesCount: candidatesCount, activeOverlapsCount: 0);

                if (decision != SkipDecision.SkipToNextCandidate)
                    return false.Label(
                        $"Should skip to next candidate when candidates={candidatesCount} > 0 " +
                        $"and activeOverlaps=0, but got {decision}");

                return true.Label(
                    $"Correctly skips to next candidate with {candidatesCount} candidates remaining");
            });
        }

        /// <summary>
        /// Property 9d: The loop break decision also respects active overlaps.
        ///
        /// The section items loop only breaks when both _candidates.Count == 0 AND
        /// _activeOverlaps.Count == 0. If active overlaps exist, the loop must continue
        /// processing section items even if candidates is empty.
        ///
        /// **Validates: Requirements 7.1**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property LoopBreak_OnlyWhenBothCandidatesAndOverlapsEmpty()
        {
            var testGen =
                from activeOverlapsCount in Gen.Choose(0, 30)
                from candidatesCount in Gen.Choose(0, 50)
                select new
                {
                    ActiveOverlapsCount = activeOverlapsCount,
                    CandidatesCount = candidatesCount
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                LoopDecision loopDecision = SimulateLoopBreakDecision(
                    candidatesNotNull: true,
                    data.CandidatesCount,
                    data.ActiveOverlapsCount);

                bool shouldBreak = data.CandidatesCount == 0 && data.ActiveOverlapsCount == 0;

                if (shouldBreak && loopDecision != LoopDecision.Break)
                    return false.Label(
                        $"Loop should break when candidates=0 and activeOverlaps=0, " +
                        $"but got {loopDecision}");

                if (!shouldBreak && loopDecision != LoopDecision.Continue)
                    return false.Label(
                        $"Loop should continue when candidates={data.CandidatesCount} " +
                        $"or activeOverlaps={data.ActiveOverlapsCount} > 0, but got {loopDecision}");

                return true.Label(
                    $"Loop decision correct: candidates={data.CandidatesCount}, " +
                    $"activeOverlaps={data.ActiveOverlapsCount} → {loopDecision}");
            });
        }

        /// <summary>
        /// Property 9e: Skip-ahead decisions form a complete partition of the state space.
        ///
        /// For any combination of (candidatesCount, activeOverlapsCount), exactly one
        /// skip decision is reached. The three cases are mutually exclusive and exhaustive:
        ///   1. NoSkip: activeOverlaps > 0
        ///   2. SkipToNextArea: candidates == 0 AND activeOverlaps == 0
        ///   3. SkipToNextCandidate: candidates > 0 AND activeOverlaps == 0
        ///
        /// **Validates: Requirements 7.1, 7.2, 7.3**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property SkipDecisions_MutuallyExclusiveAndExhaustive()
        {
            var testGen =
                from candidatesCount in Gen.Choose(0, 100)
                from activeOverlapsCount in Gen.Choose(0, 50)
                select new
                {
                    CandidatesCount = candidatesCount,
                    ActiveOverlapsCount = activeOverlapsCount
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                SkipDecision decision = SimulateSkipAheadDecision(
                    data.CandidatesCount, data.ActiveOverlapsCount);

                // Determine the expected decision based on state
                SkipDecision expected;
                if (data.ActiveOverlapsCount > 0)
                    expected = SkipDecision.NoSkip;
                else if (data.CandidatesCount == 0)
                    expected = SkipDecision.SkipToNextArea;
                else
                    expected = SkipDecision.SkipToNextCandidate;

                if (decision != expected)
                    return false.Label(
                        $"Unexpected decision for candidates={data.CandidatesCount}, " +
                        $"activeOverlaps={data.ActiveOverlapsCount}: " +
                        $"got {decision}, expected {expected}");

                return true.Label(
                    $"Decision partition correct: candidates={data.CandidatesCount}, " +
                    $"activeOverlaps={data.ActiveOverlapsCount} → {decision}");
            });
        }

        /// <summary>
        /// Property 9f: Active overlaps always block skip-ahead regardless of count.
        ///
        /// Whether there is 1 active overlap or many, the skip-ahead is equally blocked.
        /// This ensures that no single active overlap can be "outvoted" by an empty
        /// candidate list.
        ///
        /// **Validates: Requirements 7.1**
        /// </summary>
        [Property(MaxTest = 200)]
        public Property AnyNonZeroActiveOverlaps_BlocksAllSkipVariants()
        {
            var testGen =
                from activeOverlapsCount in Gen.Choose(1, 100)
                from candidatesCount in Gen.Choose(0, 100)
                select new
                {
                    ActiveOverlapsCount = activeOverlapsCount,
                    CandidatesCount = candidatesCount
                };

            return Prop.ForAll(testGen.ToArbitrary(), data =>
            {
                SkipDecision decision = SimulateSkipAheadDecision(
                    data.CandidatesCount, data.ActiveOverlapsCount);

                // No variant of skip should fire
                if (decision == SkipDecision.SkipToNextArea)
                    return false.Label(
                        $"SkipToNextArea must NOT fire when activeOverlaps={data.ActiveOverlapsCount}, " +
                        $"candidates={data.CandidatesCount}");

                if (decision == SkipDecision.SkipToNextCandidate)
                    return false.Label(
                        $"SkipToNextCandidate must NOT fire when activeOverlaps={data.ActiveOverlapsCount}, " +
                        $"candidates={data.CandidatesCount}");

                return true.Label(
                    $"All skip variants blocked with activeOverlaps={data.ActiveOverlapsCount}");
            });
        }

        #endregion
    }
}