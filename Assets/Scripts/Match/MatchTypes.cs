using System.Collections.Generic;
using Unity.Netcode;

namespace RouletteParty.Match
{
    // =====================================================================================
    // Day4 match domain types.
    //
    // Design (validated by research, NGO 2.13 / Unity 6):
    //  - Replicated *display* state (phase / round / topic / phase-end-time / alive / winner)
    //    lives in NetworkVariables on MatchManager (NetworkBehaviour).
    //  - Per-round *judging* data (peak height, finish time, death time, ...) is SERVER-ONLY
    //    plain C# (PlayerRuntime), never networked.
    //  - Judging is a strategy pattern: each mode is a plain server-side C# class (IGameMode).
    //    MatchManager does the common per-frame sampling; a mode only ranks at round end.
    //    This keeps modes thin and makes Day5 (vote weighting, detailed 0..1000 scoring) an
    //    additive change instead of a rewrite.
    //  - Full per-round rankings are replicated to every client via NetworkList<RoundResult>
    //    (unmanaged struct) so Day5/Day6 can build the result/leaderboard UI without new RPCs.
    // =====================================================================================

    /// <summary>Match phase FSM (LOBBY -> PREP -> [ROULETTE -> PLAY -> HIGHLIGHT] x3 -> RESULT -> loop).</summary>
    public enum MatchPhase : byte { Lobby, Prep, Roulette, Play, Highlight, Result }

    /// <summary>Round topic / game mode. None is used only for LOBBY/PREP display.</summary>
    public enum TopicMode : byte { None, Race, Height, Survive }

    /// <summary>Course constants shared 1:1 with the validated web prototype.</summary>
    public static class Course
    {
        public const float START_Z       = 68f;    // start line / spawn Z
        public const float GOAL_Z        = -70f;   // finish line Z (race)
        public const float DEATH_Y       = -3.7f;  // below this Y = fell into the river (death)
        public const float SPAWN_Y       = 1.0f;   // safe height on the start line
        public const float RESPAWN_DELAY = 2f;     // seconds before respawn (race/height)
        public const int   ROUNDS        = 3;      // rounds per match
    }

    /// <summary>
    /// Server-only per-player judging data for the current round. NOT networked
    /// (only the final ranking is replicated, via RoundResult rows). Reused across rounds
    /// through <see cref="ResetForRound"/>.
    /// </summary>
    public class PlayerRuntime
    {
        public ulong ClientId;
        public int   SpawnIndex;             // start-line slot; reused on respawn so players keep lanes
        public float PeakY;                  // height mode: highest Y reached during PLAY
        public bool  Finished;               // race mode: crossed the goal
        public float FinishTime;             // race: PLAY-elapsed seconds at finish (smaller = better)
        public float Progress;               // race: best 0..1 course progress (for non-finishers)
        public bool  Alive = true;           // survive mode: still in; race/height: currently not respawning
        public float DeathTime;              // PLAY-elapsed seconds at (last) death (survive: bigger = better)
        public bool  PendingRespawn;         // race/height: waiting to respawn
        public double RespawnAtServerTime;   // server time at which the pending respawn fires

        public void ResetForRound(float spawnY, int spawnIndex)
        {
            SpawnIndex = spawnIndex;
            PeakY = spawnY;
            Finished = false; FinishTime = 0f; Progress = 0f;
            Alive = true; DeathTime = 0f;
            PendingRespawn = false; RespawnAtServerTime = 0d;
        }
    }

    /// <summary>Read-only view handed to a mode at round evaluation (HIGHLIGHT entry).</summary>
    public class MatchContext
    {
        public IReadOnlyDictionary<ulong, PlayerRuntime> Players;
        public float PlayDuration; // effective PLAY seconds (fastMode-scaled) for score normalization
    }

    /// <summary>One ranked entry produced by a mode: a client and its (higher = better) score.</summary>
    public struct ModeRank
    {
        public ulong ClientId;
        public float Score;
        public ModeRank(ulong clientId, float score) { ClientId = clientId; Score = score; }
    }

    /// <summary>
    /// Strategy interface for a round mode. Concrete modes are plain server-side C# (no
    /// NetworkBehaviour). <see cref="Evaluate"/> returns entries ordered best-first (index 0 =
    /// round winner) with a per-player score (higher = better).
    /// Day5: add a per-round detailed scorer (clear 70 : obstacle 30, 0..1000) that consumes
    /// the same MatchContext and accumulates across rounds.
    /// </summary>
    public interface IGameMode
    {
        TopicMode Topic { get; }
        bool AllowRespawn { get; }   // false => elimination on death (survive)
        List<ModeRank> Evaluate(MatchContext ctx);
    }

    /// <summary>Race: reach the goal. Finishers ranked by earliest finish; non-finishers by progress.</summary>
    public sealed class RaceMode : IGameMode
    {
        public TopicMode Topic => TopicMode.Race;
        public bool AllowRespawn => true;

        public List<ModeRank> Evaluate(MatchContext ctx)
        {
            var ranks = new List<ModeRank>(ctx.Players.Count);
            foreach (var pr in ctx.Players.Values)
            {
                // Finishers always outrank non-finishers (base 1000). Earlier finish => higher score.
                // Non-finishers score by 0..1 progress, which is always below any finisher.
                float score = pr.Finished
                    ? 1000f + (ctx.PlayDuration - pr.FinishTime)
                    : pr.Progress;
                ranks.Add(new ModeRank(pr.ClientId, score));
            }
            ranks.Sort((a, b) => b.Score.CompareTo(a.Score));
            return ranks;
        }
    }

    /// <summary>Height: highest peak Y wins.</summary>
    public sealed class HeightMode : IGameMode
    {
        public TopicMode Topic => TopicMode.Height;
        public bool AllowRespawn => true;

        public List<ModeRank> Evaluate(MatchContext ctx)
        {
            var ranks = new List<ModeRank>(ctx.Players.Count);
            foreach (var pr in ctx.Players.Values)
                ranks.Add(new ModeRank(pr.ClientId, pr.PeakY));
            ranks.Sort((a, b) => b.Score.CompareTo(a.Score));
            return ranks;
        }
    }

    /// <summary>Survive: no respawn. Survivors beat everyone; among the eliminated, later death is better.</summary>
    public sealed class SurviveMode : IGameMode
    {
        public TopicMode Topic => TopicMode.Survive;
        public bool AllowRespawn => false;

        public List<ModeRank> Evaluate(MatchContext ctx)
        {
            var ranks = new List<ModeRank>(ctx.Players.Count);
            foreach (var pr in ctx.Players.Values)
            {
                // Survivors get a score above the maximum possible death time; eliminated players
                // score by death time (later = higher).
                float score = pr.Alive ? (ctx.PlayDuration + 1000f) : pr.DeathTime;
                ranks.Add(new ModeRank(pr.ClientId, score));
            }
            ranks.Sort((a, b) => b.Score.CompareTo(a.Score));
            return ranks;
        }
    }

    /// <summary>
    /// One replicated per-round result row. Lives in NetworkList&lt;RoundResult&gt; so every client
    /// can render round rankings without extra RPCs.
    /// NetworkList requires: T : unmanaged, IEquatable&lt;T&gt;. All fields are value types (no
    /// reference fields) so the struct stays unmanaged. INetworkSerializable pins the wire format.
    /// </summary>
    public struct RoundResult : INetworkSerializable, System.IEquatable<RoundResult>
    {
        public int   Round;      // 1..ROUNDS
        public ulong ClientId;
        public int   Rank;       // 1 = round winner
        public float Score;      // mode metric (see each mode's Evaluate)
        public byte  Topic;      // (TopicMode) as byte

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Round);
            s.SerializeValue(ref ClientId);
            s.SerializeValue(ref Rank);
            s.SerializeValue(ref Score);
            s.SerializeValue(ref Topic);
        }

        public bool Equals(RoundResult o) =>
            Round == o.Round && ClientId == o.ClientId &&
            Rank == o.Rank && Score.Equals(o.Score) && Topic == o.Topic;

        public override bool Equals(object obj) => obj is RoundResult o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + Round;
                h = h * 31 + ClientId.GetHashCode();
                h = h * 31 + Rank;
                h = h * 31 + Score.GetHashCode();
                h = h * 31 + Topic;
                return h;
            }
        }
    }
}
