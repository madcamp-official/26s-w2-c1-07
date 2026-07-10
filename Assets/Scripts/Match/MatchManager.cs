using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components; // NetworkTransform (built-in test-teleport fallback)
using UnityEngine;

namespace RouletteParty.Match
{
    /// <summary>
    /// Day4 host-authoritative match FSM. Attach to a scene-placed GameObject with a
    /// NetworkObject (server authority). A scene NetworkObject auto-spawns on server start,
    /// so it does NOT need to be in any NetworkPrefabs list.
    ///
    ///   LOBBY -> PREP -> [ROULETTE -> PLAY -> HIGHLIGHT] x3 -> RESULT -> (loop back to LOBBY)
    ///
    /// TIMER MODEL:
    ///   The phase timer runs on the SERVER only (Update, guarded by IsServer + IsSpawned).
    ///   The server writes the phase's absolute end time (server clock) into a
    ///   NetworkVariable&lt;double&gt; ONCE per phase. Every client computes remaining time as
    ///       remaining = _phaseEndTime.Value - NetworkManager.ServerTime.Time
    ///   ServerTime is synchronized on all peers, so timers match and join-in-progress works
    ///   automatically (no per-frame countdown replication).
    ///
    /// OWNER-AUTHORITATIVE MOVEMENT CONTRACT (ClientNetworkTransform, OnIsServerAuthoritative=>false):
    ///   The host may READ every player's transform (owners replicate position to the server),
    ///   so death / progress / peak-height checks all run on the server by reading positions.
    ///   The host may NOT WRITE remote player transforms. To move / respawn a player, the server
    ///   sends a targeted RPC to that player's OWNING client, and the owner teleports ITSELF.
    ///
    /// INTEGRATION ASSUMPTION (parallel Day3 work):
    ///   PlayerController (RouletteParty.Net) is assumed to expose, once Day3 merges:
    ///       public void TeleportTo(Vector3 pos)   // owner-only self-teleport
    ///   To guarantee THIS file compiles on its own (TeleportTo may not be merged yet), the RPC
    ///   receiver calls it by name via SendMessage (zero compile-time dependency). A built-in
    ///   self-teleport fallback (see builtInTeleportFallback) lets you TEST teleport/respawn in
    ///   Day4 before TeleportTo exists. After Day3 merges, turn the fallback OFF and (optionally)
    ///   replace the SendMessage line with the strongly-typed call marked below.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class MatchManager : NetworkBehaviour
    {
        [Header("Test tuning")]
        [Tooltip("When on, every phase duration is multiplied by fastMultiplier for quick testing.")]
        [SerializeField] private bool fastMode = false;
        [SerializeField, Range(0.05f, 1f)] private float fastMultiplier = 0.25f;

        [Tooltip("Horizontal spacing between players on the start line.")]
        [SerializeField] private float spawnSpacingX = 2f;

        [Tooltip("Draw the top-right debug panel (phase/round/topic/time/alive/winners).")]
        [SerializeField] private bool showDebugGui = true;

        [Tooltip("Day4 STANDALONE-TEST ONLY. Owner teleports itself directly from this RPC " +
                 "(no PlayerController needed) so you can verify start-line move / respawn before " +
                 "Day3's TeleportTo is merged. Turn OFF once PlayerController.TeleportTo is integrated " +
                 "to avoid a double teleport.")]
        [SerializeField] private bool builtInTeleportFallback = false;

        // ---- Replicated state (server-write by default permission, everyone-read) ----
        private NetworkVariable<MatchPhase> _phase        = new NetworkVariable<MatchPhase>(MatchPhase.Lobby);
        private NetworkVariable<int>        _round        = new NetworkVariable<int>(0);
        private NetworkVariable<TopicMode>  _topic        = new NetworkVariable<TopicMode>(TopicMode.None);
        private NetworkVariable<double>     _phaseEndTime = new NetworkVariable<double>(0d);
        private NetworkVariable<int>        _aliveCount   = new NetworkVariable<int>(0);
        private NetworkVariable<ulong>      _roundWinner  = new NetworkVariable<ulong>(ulong.MaxValue);
        private NetworkVariable<ulong>      _matchWinner  = new NetworkVariable<ulong>(ulong.MaxValue);

        // Full per-round rankings, replicated to all clients (Day5/Day6 result UI reads this).
        // Must exist BEFORE spawn -> field initializer (do NOT create it in OnNetworkSpawn).
        private NetworkList<RoundResult> _results = new NetworkList<RoundResult>();

        // ---- Server-only state (never networked) ----
        private readonly Dictionary<ulong, PlayerRuntime> _players = new Dictionary<ulong, PlayerRuntime>();
        private readonly Dictionary<ulong, int> _wins = new Dictionary<ulong, int>();
        private readonly System.Random _rng = new System.Random();
        private IGameMode _mode;
        private TopicMode _lastTopic = TopicMode.None;

        // ============================ Lifecycle ============================
        public override void OnNetworkSpawn()
        {
            // Client-side reaction hooks (SFX / UI) can subscribe here. Day4's debug GUI reads
            // the values directly every frame, so no subscription is required for it.
            // NOTE: OnValueChanged may NOT fire on the initial spawn sync, so late joiners must
            // read current values directly (the GUI already does).
            //   _phase.OnValueChanged += (prev, next) => { /* Day5: phase transition SFX/UI */ };

            if (!IsServer) return;

            NetworkManager.OnClientConnectedCallback    += HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback   += HandleClientDisconnected;

            // Seed already-connected clients (the host's own player, plus anyone who connected
            // before this scene object spawned). The host's own player can be missed by the
            // connect callback, so seeding from ConnectedClientsList here is the safe pattern.
            foreach (var c in NetworkManager.ConnectedClientsList)
                EnsurePlayer(c.ClientId);

            EnterPhase(MatchPhase.Lobby);
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer) return;
            NetworkManager.OnClientConnectedCallback  -= HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        private void HandleClientConnected(ulong clientId) => EnsurePlayer(clientId);

        private void HandleClientDisconnected(ulong clientId)
        {
            _players.Remove(clientId);
            _wins.Remove(clientId);
        }

        private void EnsurePlayer(ulong clientId)
        {
            if (!_players.ContainsKey(clientId))
                _players[clientId] = new PlayerRuntime { ClientId = clientId };
        }

        // ============================ Server tick ============================
        private void Update()
        {
            if (!IsServer || !IsSpawned) return;

            double now = NetworkManager.ServerTime.Time;

            if (_phase.Value == MatchPhase.Play)
                SamplePlayers(now);
            else
                RescueFallenPlayers();

            if (now >= _phaseEndTime.Value) { AdvancePhase(); return; }

            // Optional: end PLAY early once the round is decided (multiplayer only).
            if (_phase.Value == MatchPhase.Play && CheckEarlyEnd())
                AdvancePhase();
        }

        private float BaseDuration(MatchPhase p)
        {
            switch (p)
            {
                case MatchPhase.Lobby:     return 3f;
                case MatchPhase.Prep:      return 30f;
                case MatchPhase.Roulette:  return 5f;
                case MatchPhase.Play:      return 45f;
                case MatchPhase.Highlight: return 8f;
                case MatchPhase.Result:    return 14f;
                default:                   return 3f;
            }
        }

        private float EffectiveDuration(MatchPhase p) =>
            BaseDuration(p) * (fastMode ? fastMultiplier : 1f);

        // ============================ FSM ============================
        private void EnterPhase(MatchPhase p)
        {
            _phase.Value = p;
            _phaseEndTime.Value = NetworkManager.ServerTime.Time + EffectiveDuration(p);

            switch (p)
            {
                case MatchPhase.Lobby:
                    ResetMatch();
                    break;

                case MatchPhase.Prep:
                    // Day4: PREP is a countdown only.
                    // Day5: obstacle placement + topic voting happen here. Collect votes into a
                    //       server-only structure, then bias PickTopic() by the tally.
                    break;

                case MatchPhase.Roulette:
                    PickTopic();
                    break;

                case MatchPhase.Play:
                    BeginPlay();
                    break;

                case MatchPhase.Highlight:
                    EndPlayEvaluate();
                    break;

                case MatchPhase.Result:
                    ComputeMatchWinner();
                    break;
            }
        }

        private void AdvancePhase()
        {
            switch (_phase.Value)
            {
                case MatchPhase.Lobby:
                    EnterPhase(MatchPhase.Prep);
                    break;
                case MatchPhase.Prep:
                    _round.Value = 1;
                    EnterPhase(MatchPhase.Roulette);
                    break;
                case MatchPhase.Roulette:
                    EnterPhase(MatchPhase.Play);
                    break;
                case MatchPhase.Play:
                    EnterPhase(MatchPhase.Highlight);
                    break;
                case MatchPhase.Highlight:
                    if (_round.Value < Course.ROUNDS)
                    {
                        _round.Value++;
                        EnterPhase(MatchPhase.Roulette);
                    }
                    else
                    {
                        EnterPhase(MatchPhase.Result);
                    }
                    break;
                case MatchPhase.Result:
                    EnterPhase(MatchPhase.Lobby);
                    break;
            }
        }

        private void ResetMatch()
        {
            _wins.Clear();
            _results.Clear();
            _lastTopic = TopicMode.None;
            _round.Value = 0;
            _topic.Value = TopicMode.None;
            _roundWinner.Value = ulong.MaxValue;
            _matchWinner.Value = ulong.MaxValue;
        }

        private void PickTopic()
        {
            // Day4: uniform random, excluding the previous round's topic.
            // Day5: weight this selection by the votes collected during PREP.
            var choices = new List<TopicMode> { TopicMode.Race, TopicMode.Height, TopicMode.Survive };
            if (_lastTopic != TopicMode.None) choices.Remove(_lastTopic);

            TopicMode t = choices[_rng.Next(choices.Count)];
            _topic.Value = t;
            _lastTopic = t;
            _mode = CreateMode(t);
        }

        private IGameMode CreateMode(TopicMode t)
        {
            switch (t)
            {
                case TopicMode.Race:    return new RaceMode();
                case TopicMode.Height:  return new HeightMode();
                case TopicMode.Survive: return new SurviveMode();
                default:                return new RaceMode();
            }
        }

        // Move everyone to the start line and reset their judging data.
        private void BeginPlay()
        {
            int i = 0;
            foreach (var c in NetworkManager.ConnectedClientsList)
            {
                EnsurePlayer(c.ClientId);
                _players[c.ClientId].ResetForRound(Course.SPAWN_Y, i);
                TeleportOwner(c.ClientId, SpawnSlot(i)); // owner-authoritative: RPC to the owner
                i++;
            }
            _aliveCount.Value = _players.Count;
        }

        private Vector3 SpawnSlot(int index)
        {
            // Staggered start line centered on x=0. y = 吏???믪씠 + ?ъ쑀 濡?吏硫??꾩뿉 ?ㅽ룿.
            float x = (index - 3) * spawnSpacingX;
            float y = TerrainGenerator.SampleHeight(x, Course.START_Z) + 1.5f;
            return new Vector3(x, y, Course.START_Z);
        }

        // 鍮?PLAY ?섏씠利? 媛뺣Ъ(y < DEATH_Y)??鍮좎쭊(珥덇린 ?ㅽ룿 ?ы븿) ?뚮젅?댁뼱瑜?異쒕컻???щ’?쇰줈 援ъ“.
        private void RescueFallenPlayers()
        {
            int i = 0;
            foreach (var c in NetworkManager.ConnectedClientsList)
            {
                var po = c.PlayerObject;
                if (po != null && po.transform.position.y < Course.DEATH_Y)
                    TeleportOwner(c.ClientId, SpawnSlot(i));
                i++;
            }
        }

        // Per-frame server sampling: read replicated positions (owner -> server), update judging
        // data, and drive death / respawn. Positions are READ only here (never written).
        private void SamplePlayers(double now)
        {
            if (_mode == null) return;

            bool allowRespawn = _mode.AllowRespawn;
            float dur = EffectiveDuration(MatchPhase.Play);
            float elapsed = dur - (float)(_phaseEndTime.Value - now); // PLAY-elapsed seconds
            int alive = 0;

            foreach (var c in NetworkManager.ConnectedClientsList)
            {
                if (!_players.TryGetValue(c.ClientId, out var pr)) continue;
                var po = c.PlayerObject;
                if (po == null) continue; // player object not spawned yet

                if (!pr.Alive)
                {
                    // race/height: respawn after the delay by asking the owner to teleport itself.
                    if (allowRespawn && pr.PendingRespawn && now >= pr.RespawnAtServerTime)
                    {
                        pr.Alive = true;
                        pr.PendingRespawn = false;
                        TeleportOwner(c.ClientId, SpawnSlot(pr.SpawnIndex));
                    }
                    if (pr.Alive) alive++;
                    continue; // survive: eliminated players stay out (spectate)
                }

                Vector3 pos = po.transform.position; // READ ok (owner replicates position to server)

                if (pos.y > pr.PeakY) pr.PeakY = pos.y;

                float prog = Mathf.Clamp01((Course.START_Z - pos.z) / (Course.START_Z - Course.GOAL_Z));
                if (prog > pr.Progress) pr.Progress = prog;

                if (!pr.Finished && pos.z <= Course.GOAL_Z)
                {
                    pr.Finished = true;
                    pr.FinishTime = elapsed;
                }

                // Death check (only during PLAY). Detected on the SERVER by reading the
                // owner-replicated Y. The reaction (respawn) is delegated to the owner via RPC.
                if (pos.y < Course.DEATH_Y)
                {
                    pr.Alive = false;
                    pr.DeathTime = elapsed;
                    if (allowRespawn)
                    {
                        pr.PendingRespawn = true;
                        pr.RespawnAtServerTime = now + Course.RESPAWN_DELAY;
                    }
                    // survive: no respawn scheduled -> eliminated for the rest of the round.
                    continue; // dead this frame -> not counted as alive
                }

                alive++;
            }

            _aliveCount.Value = alive;
        }

        // Optional early round end (skips the remaining PLAY timer once the outcome is settled).
        // Guarded to >= 2 players so solo testing always runs full timers.
        private bool CheckEarlyEnd()
        {
            if (_mode == null || _players.Count < 2) return false;
            if (_topic.Value == TopicMode.Race)    return _players.Values.All(p => p.Finished);
            if (_topic.Value == TopicMode.Survive) return _aliveCount.Value <= 1;
            return false; // height always uses the full timer
        }

        // Rank the round, publish the winner, and append replicated result rows.
        private void EndPlayEvaluate()
        {
            if (_mode == null) return;

            var ctx = new MatchContext { Players = _players, PlayDuration = EffectiveDuration(MatchPhase.Play) };
            List<ModeRank> ranking = _mode.Evaluate(ctx);

            for (int i = 0; i < ranking.Count; i++)
            {
                _results.Add(new RoundResult
                {
                    Round    = _round.Value,
                    ClientId = ranking[i].ClientId,
                    Rank     = i + 1,
                    Score    = ranking[i].Score,
                    Topic    = (byte)_topic.Value
                });
            }

            if (ranking.Count > 0)
            {
                ulong w = ranking[0].ClientId;
                _roundWinner.Value = w;
                _wins.TryGetValue(w, out int n);
                _wins[w] = n + 1;
            }
            Debug.Log($"[Match] R{_round.Value} {_topic.Value} end: players={_players.Count} ranking={ranking.Count} roundWin={_roundWinner.Value} totalWins={_wins.Count}");
            // Day5: accumulate detailed 0..1000 scores here (clear 70 : obstacle 30) instead of
            // only counting round wins, then rank the match by total score.
        }

        private void ComputeMatchWinner()
        {
            ulong best = ulong.MaxValue;
            int bestWins = -1;
            foreach (var kv in _wins)
            {
                if (kv.Value > bestWins) { bestWins = kv.Value; best = kv.Key; }
            }
            _matchWinner.Value = best;
        }

        // ============ Owner-authoritative teleport bridge (server -> owning client) ============
        // Server-side helper: ask ONE owning client to teleport its own player.
        private void TeleportOwner(ulong clientId, Vector3 pos)
        {
            // RpcTarget.Single(...) implicitly converts to RpcParams (last param). Temp = one-shot,
            // no lasting allocation; fine for infrequent teleport/respawn events.
            TeleportPlayerRpc(pos, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        // Runs ONLY on the targeted client (the host also runs it locally when it targets itself).
        // The client teleports ITS OWN player, which is the ClientNetworkTransform authority.
        [Rpc(SendTo.SpecifiedInParams)]
        private void TeleportPlayerRpc(Vector3 pos, RpcParams rpcParams = default)
        {
            var po = NetworkManager.LocalClient != null ? NetworkManager.LocalClient.PlayerObject : null;
            if (po == null) return;

            // ---- INTENDED Day3 integration path (owner authority) ----
            // Compile-safe: calls PlayerController.TeleportTo(Vector3) by name if present.
            // After Day3 merges, prefer the strongly-typed call:
            //     if (po.TryGetComponent(out RouletteParty.Net.PlayerController pc)) pc.TeleportTo(pos);
            po.gameObject.SendMessage("TeleportTo", pos, SendMessageOptions.DontRequireReceiver);

            // ---- Day4 standalone-test fallback (see field tooltip) ----
            // While TeleportTo does not exist, the SendMessage above is a no-op, so this actually
            // performs the teleport. Turn it OFF after Day3 integration to avoid a double teleport.
            if (builtInTeleportFallback)
                BuiltInTeleport(po, pos);
        }

        // Self-contained teleport used only by the Day4 test fallback. Runs on the OWNER, which is
        // the ClientNetworkTransform authority, so writing its own transform is valid and replicates.
        private static void BuiltInTeleport(NetworkObject po, Vector3 pos)
        {
            var cc = po.GetComponent<CharacterController>();
            bool wasEnabled = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false; // stop CC from fighting the position write

            po.transform.position = pos;

            // Skip interpolation so remote instances snap instead of sliding across the level.
            var nt = po.GetComponent<NetworkTransform>();
            if (nt != null) nt.Teleport(pos, po.transform.rotation, po.transform.localScale);

            if (cc != null) cc.enabled = wasEnabled;
        }

        // ============================ Debug GUI (no scene UI needed) ============================
        private void OnGUI()
        {
            if (!showDebugGui) return;
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;

            double now = IsSpawned ? nm.ServerTime.Time : 0d;
            float rem = Mathf.Max(0f, (float)(_phaseEndTime.Value - now));
            string role = IsServer ? (IsHost ? "Host" : "Server") : "Client";

            // Placed to the RIGHT of NetworkBootstrap's panel (which occupies x:10..270, y:10..280).
            GUILayout.BeginArea(new Rect(280, 10, 380, 400), GUI.skin.box);
            GUILayout.Label($"[MatchManager] role={role}");
            GUILayout.Label($"Phase   : {_phase.Value}");
            GUILayout.Label($"Round   : {_round.Value}/{Course.ROUNDS}");
            GUILayout.Label($"Topic   : {_topic.Value}");
            GUILayout.Label($"Time    : {rem:0.0}s");
            GUILayout.Label($"Alive   : {_aliveCount.Value}");
            if (IsServer) GUILayout.Label($"Players : {_players.Count} (server)");
            GUILayout.Label($"RoundWin: {WinnerLabel(_roundWinner.Value)}");
            GUILayout.Label($"MatchWin: {WinnerLabel(_matchWinner.Value)}");
            GUILayout.Label($"Results : {_results.Count} rows");
            if (fastMode) GUILayout.Label($"FAST x{fastMultiplier}");
            GUILayout.EndArea();
        }

        private static string WinnerLabel(ulong id) => id == ulong.MaxValue ? "-" : id.ToString();
    }
}
