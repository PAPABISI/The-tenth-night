using System;
using System.Collections.Generic;
using System.Linq;

namespace NightTen.Core
{
    public class GameRuleEngine : IGameRuleEngine
    {
        private readonly Random _rng = new();

        public void AssignIdentities(List<Guid> playerIds, GameState state)
        {
            if (playerIds == null || playerIds.Count < 4)
                throw new ArgumentException("[Engine] 玩家人数不足，至少需要4人。");

            var shuffled = new List<Guid>(playerIds);
            int n = shuffled.Count;
            while (n > 1)
            {
                n--;
                int k = _rng.Next(n + 1);
                (shuffled[k], shuffled[n]) = (shuffled[n], shuffled[k]);
            }

            int total = shuffled.Count;
            int loversCount = 2;
            int remaining = total - loversCount;
            int guardianCount = remaining / 2 + (remaining % 2);
            int thiefCount = remaining / 2;

            state.AlivePlayers.Clear();
            state.AllPlayers.Clear();

            int index = 0;

            for (int i = 0; i < loversCount; i++)
                CreateAndRegisterPlayer(shuffled[index++], Faction.Lovers, PlayerRole.Member, state);

            for (int i = 0; i < guardianCount; i++)
                CreateAndRegisterPlayer(shuffled[index++], Faction.Guardian, i == 0 ? PlayerRole.Leader : PlayerRole.Member, state);

            for (int i = 0; i < thiefCount; i++)
                CreateAndRegisterPlayer(shuffled[index++], Faction.Thief, i == 0 ? PlayerRole.Leader : PlayerRole.Member, state);
        }

        private void CreateAndRegisterPlayer(Guid id, Faction faction, PlayerRole role, GameState state)
        {
            int hp = role == PlayerRole.Leader ? 4 : 3;

            var p = new Player
            {
                PlayerId = id,
                DisplayName = $"Player_{id.ToString()[..4]}",
                Faction = faction,
                Role = role,
                MaxHp = hp,
                CurrentHp = hp,
                CurrentAp = 1
            };

            state.AlivePlayers.Add(p);
            state.AllPlayers[p.PlayerId] = p;
        }

        // ===== 新增：开局重置玩家状态 =====
        public void InitializePlayersForNewGame(GameState state)
        {
            foreach (var p in state.AllPlayers.Values)
            {
                p.IsAlive = true;
                p.CurrentAp = 1;
                p.PoisonStacks = 0;
                p.PoisonSourceQueue.Clear();
                p.HasBulletProofBuff = false;
                p.HoldsTreasure = false;
                p.HasInspectableCorpse = false;
                p.HasDrawnCardThisRound = false;
                p.Hand.Clear();

                p.MaxHp = (p.Role == PlayerRole.Leader) ? 4 : 3;
                p.CurrentHp = p.MaxHp;
            }

            state.AlivePlayers.Clear();
            state.AlivePlayers.AddRange(state.AllPlayers.Values);

            state.TreasureState = TreasureState.AtSpawnPoint;
            state.TreasureHolderId = null;
            state.NightThiefIntentions.Clear();
            state.RoundDeathLog.Clear();
            state.GuardianLeaderInspectionUsed = false;
            state.Result = null;
            state.CurrentRound = 1;
        }

        // ===== 新增：开局发初始手牌 =====
        public void InitializeDeckAndDealInitialHands(GameState state, int initialCardsPerPlayer = 4)
        {
            foreach (var p in state.AlivePlayers)
            {
                for (int i = 0; i < initialCardsPerPlayer; i++)
                {
                    if (p.Hand.Count >= p.HandCardLimit) break;
                    var card = new Card { Type = GenerateInitialCardType() };
                    p.Hand.Add(card);
                }
            }
        }

        private CardType GenerateInitialCardType()
        {
            int roll = _rng.Next(1, 101);
            if (roll <= 30) return CardType.Poison;
            if (roll <= 50) return CardType.Bandage;
            if (roll <= 68) return CardType.Antidote;
            if (roll <= 86) return CardType.BulletProof;
            return CardType.GunShot;
        }

        public void InitializeRound(GameState state)
        {
            foreach (var p in state.AlivePlayers)
            {
                p.CurrentAp = 1;
                p.HasDrawnCardThisRound = false;
                p.HasBulletProofBuff = false;
            }

            if (state.TreasureState == TreasureState.HeldByPlayer && state.TreasureHolderId == null)
                state.TreasureState = TreasureState.AtSpawnPoint;
        }

        public Card? DrawCardFromChest(Guid playerId, bool isRedChest, GameState state)
        {
            if (!state.AllPlayers.TryGetValue(playerId, out var player) || !player.IsAlive) return null;
            if (player.HasDrawnCardThisRound) return null;
            if (player.Hand.Count >= player.HandCardLimit) return null;

            var cardType = GenerateCardFromPool(isRedChest);
            var card = new Card { Type = cardType };
            player.Hand.Add(card);
            player.HasDrawnCardThisRound = true;
            return card;
        }

        private CardType GenerateCardFromPool(bool isRedChest)
        {
            int roll = _rng.Next(1, 101);

            if (isRedChest)
            {
                if (roll <= 70) return CardType.Poison;
                if (roll <= 80) return CardType.GunShot;
                if (roll <= 87) return CardType.Antidote;
                if (roll <= 94) return CardType.Bandage;
                return CardType.BulletProof;
            }

            if (roll <= 35) return CardType.Antidote;
            if (roll <= 70) return CardType.Bandage;
            if (roll <= 90) return CardType.BulletProof;
            return CardType.Poison;
        }

        public void DiscardCard(Guid playerId, Guid cardId, GameState state)
        {
            if (!state.AllPlayers.TryGetValue(playerId, out var player) || !player.IsAlive) return;
            player.Hand.RemoveAll(c => c.CardId == cardId);
        }

        public List<GameEvent> ProcessCardInteraction(Guid userId, Guid? targetId, Guid cardId, GameState state)
        {
            var events = new List<GameEvent>();

            if (!state.AllPlayers.TryGetValue(userId, out var user) || !user.IsAlive) return events;

            Player? target = null;
            if (targetId.HasValue)
            {
                if (!state.AllPlayers.TryGetValue(targetId.Value, out target) || !target.IsAlive) return events;
            }

            var card = user.Hand.FirstOrDefault(c => c.CardId == cardId);
            if (card == null) return events;

            var effect = GetEffectStrategy(card.Type);
            if (!effect.CanUse(user, target, state.CurrentPhase)) return events;

            if (state.CurrentPhase == GamePhase.DayExploration)
            {
                if (user.CurrentAp < 1) return events;
                user.CurrentAp--;
            }

            user.Hand.Remove(card);
            events.AddRange(effect.Execute(user, target, state));

            if (target != null && target.CurrentHp <= 0)
                events.AddRange(ProcessPlayerDeath(target.PlayerId, card.Type, user.PlayerId, state));

            return events;
        }

        private ICardEffect GetEffectStrategy(CardType type) => type switch
        {
            CardType.GunShot => new GunShotEffect(),
            CardType.Poison => new PoisonEffect(),
            CardType.Antidote => new AntidoteEffect(),
            CardType.Bandage => new BandageEffect(),
            CardType.BulletProof => new BulletProofEffect(),
            _ => throw new NotImplementedException()
        };

        public List<GameEvent> SettleDinnerPhase(GameState state)
        {
            var events = new List<GameEvent>();
            var snapshot = state.AlivePlayers.ToList();

            foreach (var p in snapshot)
            {
                if (p.PoisonStacks <= 0) continue;

                p.CurrentHp -= p.PoisonStacks;

                if (p.CurrentHp <= 0)
                {
                    Guid? lastPoisonerId = p.PoisonSourceQueue.Count > 0 ? p.PoisonSourceQueue.Last() : null;
                    events.AddRange(ProcessPlayerDeath(p.PlayerId, CardType.Poison, lastPoisonerId, state));
                }
                else
                {
                    p.PoisonStacks = 0;
                    p.PoisonSourceQueue.Clear();
                }
            }

            return events;
        }

        public InspectionResultEvent? ExecuteLeaderInspection(Guid inspectorId, Guid targetId, GameState state)
        {
            if (state.CurrentPhase != GamePhase.DinnerPhase) return null;
            if (!state.AllPlayers.TryGetValue(inspectorId, out var inspector)) return null;
            if (!state.AllPlayers.TryGetValue(targetId, out var target)) return null;

            if (inspector.Faction != Faction.Guardian || inspector.Role != PlayerRole.Leader) return null;
            if (state.GuardianLeaderInspectionUsed) return null;
            if (!target.IsAlive && !target.HasInspectableCorpse) return null;

            state.GuardianLeaderInspectionUsed = true;

            return new InspectionResultEvent
            {
                InspectorId = inspectorId,
                TargetId = targetId,
                TargetFaction = target.Faction,
                VisibleToPlayerIds = new HashSet<Guid> { inspectorId }
            };
        }

        public void RegisterThiefNightIntent(Guid thiefId, bool intendToSteal, GameState state)
        {
            if (state.CurrentPhase != GamePhase.NightPhase) return;
            if (!state.AllPlayers.TryGetValue(thiefId, out var thief)) return;
            if (!thief.IsAlive || thief.Faction != Faction.Thief) return;

            if (intendToSteal) state.NightThiefIntentions.Add(thiefId);
            else state.NightThiefIntentions.Remove(thiefId);
        }

        public List<GameEvent> SettleNightPhase(GameState state)
        {
            var events = new List<GameEvent>();

            var activeThieves = state.NightThiefIntentions
                .Where(id => state.AllPlayers.ContainsKey(id))
                .Select(id => state.AllPlayers[id])
                .Where(p => p.IsAlive && p.Faction == Faction.Thief)
                .ToList();

            if (activeThieves.Count > 0)
            {
                Player winner = activeThieves.FirstOrDefault(p => p.Role == PlayerRole.Leader) ?? RollNightWinner(activeThieves);

                if (state.TreasureHolderId.HasValue && state.AllPlayers.TryGetValue(state.TreasureHolderId.Value, out var oldHolder))
                    oldHolder.HoldsTreasure = false;

                winner.HoldsTreasure = true;
                state.TreasureHolderId = winner.PlayerId;
                state.TreasureState = TreasureState.HeldByPlayer;
            }

            state.NightThiefIntentions.Clear();
            return events;
        }

        private Player RollNightWinner(List<Player> thieves)
        {
            int maxRoll = -1;
            Player winner = thieves[0];

            foreach (var t in thieves)
            {
                int roll = _rng.Next(1, 101);
                if (roll > maxRoll)
                {
                    maxRoll = roll;
                    winner = t;
                }
            }

            return winner;
        }

        public List<GameEvent> ProcessPlayerDeath(Guid victimId, CardType causeOfDeath, Guid? killerId, GameState state)
        {
            var events = new List<GameEvent>();

            if (!state.AllPlayers.TryGetValue(victimId, out var victim) || !victim.IsAlive)
                return events;

            victim.IsAlive = false;
            state.AlivePlayers.Remove(victim);

            if (causeOfDeath == CardType.GunShot)
                victim.HasInspectableCorpse = true;

            events.Add(new PlayerDiedEvent { VictimId = victimId, CauseOfDeath = causeOfDeath });

            Player? killer = null;
            if (killerId.HasValue) state.AllPlayers.TryGetValue(killerId.Value, out killer);

            if (victim.HoldsTreasure)
            {
                victim.HoldsTreasure = false;
                bool resetTreasure =
                    causeOfDeath == CardType.Poison ||
                    killer == null ||
                    killer.Faction == Faction.Guardian;

                if (causeOfDeath == CardType.GunShot && killer != null && killer.IsAlive && killer.Faction != Faction.Guardian)
                {
                    killer.HoldsTreasure = true;
                    state.TreasureHolderId = killer.PlayerId;
                    state.TreasureState = TreasureState.HeldByPlayer;
                }
                else if (resetTreasure)
                {
                    state.TreasureHolderId = null;
                    state.TreasureState = TreasureState.AtSpawnPoint;
                }
            }

            if (victim.Role == PlayerRole.Leader && killer != null && killer.Faction == victim.Faction)
            {
                var inheritor = killer.IsAlive
                    ? killer
                    : state.AlivePlayers.FirstOrDefault(p => p.Faction == victim.Faction);

                if (inheritor != null)
                {
                    inheritor.Role = PlayerRole.Leader;
                    inheritor.MaxHp = 4;
                    inheritor.CurrentHp = 4;

                    events.Add(new LeadershipInheritedEvent
                    {
                        NewLeaderId = inheritor.PlayerId,
                        Faction = inheritor.Faction,
                        VisibleToPlayerIds = new HashSet<Guid> { inheritor.PlayerId }
                    });
                }
            }

            if (victim.Faction == Faction.Lovers)
            {
                var survivingLover = state.AlivePlayers.FirstOrDefault(p => p.Faction == Faction.Lovers);
                if (survivingLover != null)
                    events.AddRange(ProcessPlayerDeath(survivingLover.PlayerId, causeOfDeath, null, state));
            }

            return events;
        }

        public bool CheckVictoryConditions(GameState state)
        {
            var aliveA = state.AlivePlayers.Where(p => p.Faction == Faction.Guardian).ToList();
            var aliveB = state.AlivePlayers.Where(p => p.Faction == Faction.Thief).ToList();
            var aliveC = state.AlivePlayers.Where(p => p.Faction == Faction.Lovers).ToList();

            bool isGameEndRound = state.CurrentRound >= state.MaxRounds;

            Faction? winningFaction = null;
            bool loversIndependent = false;
            string desc = string.Empty;

            if (aliveB.Count == 0 && aliveA.Count > 0)
            {
                winningFaction = Faction.Guardian;
                desc = "守卫肃清了所有盗贼。";
            }
            else if (aliveA.Count == 0 && aliveB.Count > 0)
            {
                winningFaction = Faction.Thief;
                desc = "盗贼肃清了所有守卫。";
            }

            if (winningFaction == null && isGameEndRound)
            {
                Player? holder = null;
                if (state.TreasureHolderId.HasValue)
                    state.AllPlayers.TryGetValue(state.TreasureHolderId.Value, out holder);

                bool loversAlive = aliveC.Count == 2;
                bool loversHoldTreasure = holder != null && holder.Faction == Faction.Lovers;

                if (loversAlive && loversHoldTreasure)
                {
                    winningFaction = Faction.Lovers;
                    loversIndependent = true;
                    desc = "恋人存活到终局且持有宝物，独赢。";
                }
                else
                {
                    if (holder != null && holder.Faction == Faction.Thief)
                    {
                        winningFaction = Faction.Thief;
                        desc = "盗贼在终局持有宝物。";
                    }
                    else
                    {
                        winningFaction = Faction.Guardian;
                        desc = "守卫在终局保护了宝物。";
                    }

                    if (loversAlive && !loversHoldTreasure)
                        desc += " 恋人达成共生主目标，共享胜利。";
                }
            }

            if (winningFaction == null) return false;

            var winners = new List<Guid>();
            if (loversIndependent)
            {
                winners.AddRange(aliveC.Select(p => p.PlayerId));
            }
            else
            {
                winners.AddRange(state.AlivePlayers.Where(p => p.Faction == winningFaction).Select(p => p.PlayerId));
                if (aliveC.Count == 2 && winningFaction != Faction.Lovers)
                    winners.AddRange(aliveC.Select(p => p.PlayerId));
            }

            state.Result = new GameResult
            {
                WinningFaction = winningFaction,
                IsLoversIndependentWin = loversIndependent,
                WinnerPlayerIds = winners,
                WinConditionDescription = desc
            };

            return true;
        }

        public PlayerSelfView GetSelfView(Guid requesterId, GameState state, GamePhase phase)
        {
            if (!state.AllPlayers.TryGetValue(requesterId, out var player))
                throw new KeyNotFoundException($"Player not found: {requesterId}");

            return player.ToSelfView(phase);
        }

        public List<PlayerPublicView> GetPublicPlayerViews(GameState state)
            => state.AllPlayers.Values.Select(p => p.ToPublicView()).ToList();
    }
}
