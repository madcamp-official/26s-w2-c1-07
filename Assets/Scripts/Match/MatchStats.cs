using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RouletteParty.Match
{
    /// <summary>
    /// 복제되는 라운드 통계(하이라이트 카드 표시용). 서버가 라운드 종료(HIGHLIGHT 진입) 시 1회 채운다.
    /// 플레이어 식별자가 없는 항목은 None(ulong.MaxValue) — UI 는 None 이면 해당 줄을 숨긴다.
    /// </summary>
    public struct RoundStats : INetworkSerializable, System.IEquatable<RoundStats>
    {
        public const ulong None = ulong.MaxValue;

        public int   Round;              // 이 통계가 속한 라운드(1..). 0 = 아직 없음
        public ulong BiggestFallVictim;  // 최대 낙하 피해자
        public float BiggestFallHeight;  // 그 낙하 높이(m)
        public ulong BestBaiter;         // 낚시왕: 자기 투명 구조물로 타인의 낙하 피해를 가장 많이 유발한 설치자
        public int   BestBaiterCount;
        public ulong MostBaited;         // 낚임왕: 투명 구조물에 가장 많이 당한 피해자
        public int   MostBaitedCount;
        public int   DeathCount;         // 라운드 총 탈락 수

        public static RoundStats Empty => new RoundStats
        {
            Round = 0,
            BiggestFallVictim = None,
            BestBaiter = None,
            MostBaited = None,
        };

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Round);
            s.SerializeValue(ref BiggestFallVictim);
            s.SerializeValue(ref BiggestFallHeight);
            s.SerializeValue(ref BestBaiter);
            s.SerializeValue(ref BestBaiterCount);
            s.SerializeValue(ref MostBaited);
            s.SerializeValue(ref MostBaitedCount);
            s.SerializeValue(ref DeathCount);
        }

        public bool Equals(RoundStats o) =>
            Round == o.Round &&
            BiggestFallVictim == o.BiggestFallVictim && BiggestFallHeight.Equals(o.BiggestFallHeight) &&
            BestBaiter == o.BestBaiter && BestBaiterCount == o.BestBaiterCount &&
            MostBaited == o.MostBaited && MostBaitedCount == o.MostBaitedCount &&
            DeathCount == o.DeathCount;

        public override bool Equals(object obj) => obj is RoundStats o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + Round;
                h = h * 31 + BiggestFallVictim.GetHashCode();
                h = h * 31 + BiggestFallHeight.GetHashCode();
                h = h * 31 + BestBaiter.GetHashCode();
                h = h * 31 + BestBaiterCount;
                h = h * 31 + MostBaited.GetHashCode();
                h = h * 31 + MostBaitedCount;
                h = h * 31 + DeathCount;
                return h;
            }
        }
    }

    /// <summary>
    /// 서버 전용 라운드 이벤트 로거/통계 산정기. NOT networked.
    /// MatchManager 가 소유하며 라운드 중 훅(RecordReveal/RecordFall)을 호출하고,
    /// 라운드 종료 시 Snapshot() 결과를 NetworkVariable 로 복제한다.
    ///
    /// "낚시" 판정 휴리스틱: 플레이어가 투명 구조물에 접촉(Reveal 보고)한 뒤 BAIT_WINDOW 초 안에
    /// 낙하 피해를 입으면, 그 낙하를 해당 구조물 설치자의 낚시 1회로 귀속한다(접촉 1회당 최대 1회).
    /// 자기 구조물에 자기가 닿은 경우는 집계하지 않는다.
    /// </summary>
    public class MatchStatsTracker
    {
        private const double BAIT_WINDOW = 6.0; // 접촉 -> 낙하 귀속 유효 시간(초)

        private struct TouchMark { public ulong Owner; public double Time; }

        // victim -> 마지막 투명 구조물 접촉 기록
        private readonly Dictionary<ulong, TouchMark> _lastTouch = new Dictionary<ulong, TouchMark>();
        private readonly Dictionary<ulong, int> _baitsByOwner   = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, int> _baitedByVictim = new Dictionary<ulong, int>();

        private ulong _fallVictim = RoundStats.None;
        private float _fallHeight;
        private int   _deaths;

        /// <summary>PLAY 시작 시 호출. 라운드 통계 초기화.</summary>
        public void BeginRound()
        {
            _lastTouch.Clear();
            _baitsByOwner.Clear();
            _baitedByVictim.Clear();
            _fallVictim = RoundStats.None;
            _fallHeight = 0f;
            _deaths = 0;
        }

        /// <summary>투명 구조물 접촉 보고(RevealStructureServerRpc) 시 호출.</summary>
        public void RecordReveal(ulong victim, ulong structureOwner, double serverTime)
        {
            if (victim == structureOwner) return; // 자기 함정 접촉은 낚시가 아님
            _lastTouch[victim] = new TouchMark { Owner = structureOwner, Time = serverTime };
        }

        /// <summary>낙하 데미지 확정(ApplyFallDamage) 시 호출.</summary>
        public void RecordFall(ulong victim, float fallHeight, bool died, double serverTime)
        {
            if (died) _deaths++;

            if (fallHeight > _fallHeight)
            {
                _fallHeight = fallHeight;
                _fallVictim = victim;
            }

            // 직전 투명 구조물 접촉이 유효 시간 안이면 설치자에게 낚시 1회 귀속.
            if (_lastTouch.TryGetValue(victim, out var mark) && serverTime - mark.Time <= BAIT_WINDOW)
            {
                _baitsByOwner.TryGetValue(mark.Owner, out int b);
                _baitsByOwner[mark.Owner] = b + 1;

                _baitedByVictim.TryGetValue(victim, out int v);
                _baitedByVictim[victim] = v + 1;

                _lastTouch.Remove(victim); // 접촉 1회당 1회만 귀속
            }
        }

        /// <summary>라운드 종료 시 호출. 복제용 통계 구조체 산출.</summary>
        public RoundStats Snapshot(int round)
        {
            var s = RoundStats.Empty;
            s.Round = round;
            s.DeathCount = _deaths;
            s.BiggestFallVictim = _fallVictim;
            s.BiggestFallHeight = _fallHeight;
            (s.BestBaiter, s.BestBaiterCount)  = MaxEntry(_baitsByOwner);
            (s.MostBaited, s.MostBaitedCount)  = MaxEntry(_baitedByVictim);
            return s;
        }

        // 최댓값 항목(동률이면 낮은 clientId). 비어 있으면 (None, 0).
        private static (ulong id, int count) MaxEntry(Dictionary<ulong, int> map)
        {
            ulong best = RoundStats.None;
            int bestCount = 0;
            foreach (var kv in map)
            {
                if (kv.Value > bestCount || (kv.Value == bestCount && kv.Value > 0 && kv.Key < best))
                {
                    best = kv.Key;
                    bestCount = kv.Value;
                }
            }
            return (best, bestCount);
        }
    }
}
