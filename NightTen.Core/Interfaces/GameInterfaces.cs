using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NightTen.Core
{
    public interface ICardEffect
    {
        CardType CardType { get; }
        bool CanUse(Player user, Player? target, GamePhase currentPhase);
        List<GameEvent> Execute(Player user, Player? target, GameState state);
    }

    public abstract record GameEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public HashSet<Guid>? VisibleToPlayerIds { get; init; } = null;
    }

    public record CardUsedEvent : GameEvent
    {
        public Guid UserId { get; init; }
        public Guid? TargetId { get; init; }
        public CardType? RevealedCardType { get; init; }
    }

    public record PlayerDiedEvent : GameEvent
    {
        public Guid VictimId { get; init; }
        public CardType CauseOfDeath { get; init; }
        public Vector2? DeathPosition { get; init; }
    }

    public record TreasureResetEvent : GameEvent { }

    public record LeadershipInheritedEvent : GameEvent
    {
        public Guid NewLeaderId { get; init; }
        public Faction Faction { get; init; }
    }

    public record InspectionResultEvent : GameEvent
    {
        public Guid InspectorId { get; init; }
        public Guid TargetId { get; init; }
        public Faction TargetFaction { get; init; }
    }

    public record PoisonRevealEvent : GameEvent
    {
        public Guid PlayerId { get; init; }
        public int PoisonStacks { get; init; }
    }

    public record Vector2(float X, float Y);

    public interface IGameRuleEngine
    {
        void AssignIdentities(List<Guid> playerIds, GameState state);

        // 开局初始化
        void InitializePlayersForNewGame(GameState state);
        void InitializeDeckAndDealInitialHands(GameState state, int initialCardsPerPlayer = 4);

        void InitializeRound(GameState state);

        Card? DrawCardFromChest(Guid playerId, bool isRedChest, GameState state);
        void DiscardCard(Guid playerId, Guid cardId, GameState state);

        List<GameEvent> ProcessCardInteraction(Guid userId, Guid? targetId, Guid cardId, GameState state);
        List<GameEvent> SettleDinnerPhase(GameState state);

        InspectionResultEvent? ExecuteLeaderInspection(Guid inspectorId, Guid targetId, GameState state);

        void RegisterThiefNightIntent(Guid thiefId, bool intendToSteal, GameState state);
        List<GameEvent> SettleNightPhase(GameState state);

        List<GameEvent> ProcessPlayerDeath(Guid victimId, CardType causeOfDeath, Guid? killerId, GameState state);

        bool CheckVictoryConditions(GameState state);

        PlayerSelfView GetSelfView(Guid requesterId, GameState state, GamePhase phase);
        List<PlayerPublicView> GetPublicPlayerViews(GameState state);
    }

    public interface IGameStateMachine
    {
        GamePhase CurrentPhase { get; }

        Task TransitionToAsync(GamePhase targetPhase, GameState state);
        void OnPhaseEnter(GamePhase phase, Func<GameState, Task> handler);
        void OnPhaseExit(GamePhase phase, Func<GameState, Task> handler);
    }
}