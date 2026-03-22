using System;
using System.Collections.Generic;

namespace NightTen.Core
{
    public class GunShotEffect : ICardEffect
    {
        public CardType CardType => CardType.GunShot;

        public bool CanUse(Player user, Player? target, GamePhase currentPhase)
            => currentPhase == GamePhase.DayExploration && target != null && target.IsAlive;

        public List<GameEvent> Execute(Player user, Player? target, GameState state)
        {
            var events = new List<GameEvent>();
            var t = target!;

            if (t.HasBulletProofBuff)
            {
                t.HasBulletProofBuff = false;
                Console.WriteLine($"[Card] {user.DisplayName} 枪击 {t.DisplayName}，被防弹衣抵消。");
                events.Add(new CardUsedEvent { UserId = user.PlayerId, TargetId = t.PlayerId, RevealedCardType = CardType.GunShot });
                return events;
            }

            t.CurrentHp = 0;
            Console.WriteLine($"[Card] {user.DisplayName} 使用【枪杀】命中 {t.DisplayName}。");
            events.Add(new CardUsedEvent { UserId = user.PlayerId, TargetId = t.PlayerId, RevealedCardType = CardType.GunShot });
            return events;
        }
    }

    public class PoisonEffect : ICardEffect
    {
        public CardType CardType => CardType.Poison;

        public bool CanUse(Player user, Player? target, GamePhase currentPhase)
            => currentPhase == GamePhase.DayExploration && target != null && target.IsAlive;

        public List<GameEvent> Execute(Player user, Player? target, GameState state)
        {
            var t = target!;
            t.PoisonStacks++;
            t.PoisonSourceQueue.Enqueue(user.PlayerId);

            Console.WriteLine($"[Card] {user.DisplayName} 对 {t.DisplayName} 使用【毒杀】（暗牌）。");
            return new List<GameEvent>
            {
                new CardUsedEvent { UserId = user.PlayerId, TargetId = t.PlayerId, RevealedCardType = null }
            };
        }
    }

    public class AntidoteEffect : ICardEffect
    {
        public CardType CardType => CardType.Antidote;

        public bool CanUse(Player user, Player? target, GamePhase currentPhase)
            => currentPhase == GamePhase.DinnerPhase;

        public List<GameEvent> Execute(Player user, Player? target, GameState state)
        {
            if (user.PoisonStacks > 0) user.PoisonStacks--;
            if (user.CurrentHp < user.MaxHp) user.CurrentHp++;

            Console.WriteLine($"[Card] {user.DisplayName} 使用【解药】。");
            return new List<GameEvent>
            {
                new CardUsedEvent { UserId = user.PlayerId, TargetId = user.PlayerId, RevealedCardType = CardType.Antidote }
            };
        }
    }

    public class BandageEffect : ICardEffect
    {
        public CardType CardType => CardType.Bandage;

        public bool CanUse(Player user, Player? target, GamePhase currentPhase)
            => currentPhase is GamePhase.DayExploration or GamePhase.DinnerPhase;

        public List<GameEvent> Execute(Player user, Player? target, GameState state)
        {
            if (user.CurrentHp < user.MaxHp) user.CurrentHp++;
            Console.WriteLine($"[Card] {user.DisplayName} 使用【绷带】。");
            return new List<GameEvent>
            {
                new CardUsedEvent { UserId = user.PlayerId, TargetId = user.PlayerId, RevealedCardType = CardType.Bandage }
            };
        }
    }

    public class BulletProofEffect : ICardEffect
    {
        public CardType CardType => CardType.BulletProof;

        public bool CanUse(Player user, Player? target, GamePhase currentPhase)
            => currentPhase == GamePhase.DayExploration;

        public List<GameEvent> Execute(Player user, Player? target, GameState state)
        {
            user.HasBulletProofBuff = true;
            Console.WriteLine($"[Card] {user.DisplayName} 使用【防弹衣】。");
            return new List<GameEvent>
            {
                new CardUsedEvent { UserId = user.PlayerId, TargetId = user.PlayerId, RevealedCardType = CardType.BulletProof }
            };
        }
    }
}