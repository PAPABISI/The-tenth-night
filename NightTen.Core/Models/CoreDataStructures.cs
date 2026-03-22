using System;
using System.Collections.Generic;

namespace NightTen.Core
{
    public enum GamePhase
    {
        WaitingForPlayers,
        Initialization,
        DayExploration,
        DinnerPhase,
        NightPhase,
        RoundSettlement,
        GameOver
    }

    public enum Faction
    {
        Guardian,
        Thief,
        Lovers
    }

    public enum PlayerRole
    {
        Leader,
        Member
    }

    public enum CardType
    {
        GunShot,
        Poison,
        Antidote,
        BulletProof,
        Bandage
    }

    public enum TreasureState
    {
        AtSpawnPoint,
        HeldByPlayer
    }

    public class Card
    {
        public Guid CardId { get; } = Guid.NewGuid();
        public CardType Type { get; init; }

        public CardClientView ToClientView() => new()
        {
            CardId = CardId,
            Type = Type
        };
    }

    public record CardClientView
    {
        public Guid CardId { get; init; }
        public CardType Type { get; init; }
    }

    public class Player
    {
        public Guid PlayerId { get; init; }
        public string DisplayName { get; init; } = string.Empty;

        public Faction Faction { get; set; }
        public PlayerRole Role { get; set; }

        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }

        public int CurrentAp { get; set; }

        public int PoisonStacks { get; set; } = 0;
        public Queue<Guid> PoisonSourceQueue { get; } = new();

        public bool HasBulletProofBuff { get; set; } = false;

        public List<Card> Hand { get; } = new(capacity: 4);
        public int HandCardLimit => 4;

        public bool IsAlive { get; set; } = true;
        public bool HoldsTreasure { get; set; } = false;
        public bool HasInspectableCorpse { get; set; } = false;
        public bool HasDrawnCardThisRound { get; set; } = false;

        public PlayerSelfView ToSelfView(GamePhase currentPhase) => new()
        {
            PlayerId = PlayerId,
            DisplayName = DisplayName,
            CurrentHp = CurrentHp,
            MaxHp = MaxHp,
            CurrentAp = CurrentAp,
            PoisonStacks = currentPhase == GamePhase.DinnerPhase ? PoisonStacks : (int?)null,
            HasBulletProofBuff = HasBulletProofBuff,
            Hand = Hand.ConvertAll(c => c.ToClientView()),
            IsAlive = IsAlive,
            HoldsTreasure = HoldsTreasure
        };

        public PlayerPublicView ToPublicView() => new()
        {
            PlayerId = PlayerId,
            DisplayName = DisplayName,
            IsAlive = IsAlive,
            HasInspectableCorpse = HasInspectableCorpse
        };
    }

    public record PlayerSelfView
    {
        public Guid PlayerId { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public int CurrentHp { get; init; }
        public int MaxHp { get; init; }
        public int CurrentAp { get; init; }
        public int? PoisonStacks { get; init; }
        public bool HasBulletProofBuff { get; init; }
        public List<CardClientView> Hand { get; init; } = new();
        public bool IsAlive { get; init; }
        public bool HoldsTreasure { get; init; }
    }

    public record PlayerPublicView
    {
        public Guid PlayerId { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public bool IsAlive { get; init; }
        public bool HasInspectableCorpse { get; init; }
    }

    public class GameState
    {
        public Guid RoomId { get; init; }

        public int CurrentRound { get; set; } = 0;
        public int MaxRounds { get; init; } = 10;
        public GamePhase CurrentPhase { get; set; } = GamePhase.WaitingForPlayers;

        public List<Player> AlivePlayers { get; } = new();
        public Dictionary<Guid, Player> AllPlayers { get; } = new();

        public TreasureState TreasureState { get; set; } = TreasureState.AtSpawnPoint;
        public Guid? TreasureHolderId { get; set; } = null;

        public HashSet<Guid> NightThiefIntentions { get; } = new();
        public List<DeathRecord> RoundDeathLog { get; } = new();

        public bool GuardianLeaderInspectionUsed { get; set; } = false;
        public GameResult? Result { get; set; } = null;
    }

    public record DeathRecord
    {
        public Guid VictimId { get; init; }
        public CardType CauseOfDeath { get; init; }
        public Guid? KillerId { get; init; }
        public Guid? InheritedLeadershipTo { get; init; }
    }

    public record GameResult
    {
        public Faction? WinningFaction { get; init; }
        public bool IsLoversIndependentWin { get; init; }
        public List<Guid> WinnerPlayerIds { get; init; } = new();
        public string WinConditionDescription { get; init; } = string.Empty;
    }
}