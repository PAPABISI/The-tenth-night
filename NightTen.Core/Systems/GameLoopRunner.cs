using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NightTen.Core
{
    /// <summary>
    /// 服务端主循环驱动器（Demo自动推演）
    /// </summary>
    public class GameLoopRunner
    {
        private readonly IGameStateMachine _fsm;
        private readonly GameState _gameState;
        private readonly IGameRuleEngine _ruleEngine;

        private readonly List<Guid> _mockPlayerIds = new();
        private readonly Random _rng = new();

        public GameLoopRunner(IGameStateMachine fsm, GameState gameState, IGameRuleEngine ruleEngine)
        {
            _fsm = fsm;
            _gameState = gameState;
            _ruleEngine = ruleEngine;

            for (int i = 0; i < 6; i++)
                _mockPlayerIds.Add(Guid.NewGuid());

            RegisterPhaseHooks();
        }

        private void RegisterPhaseHooks()
        {
            _fsm.OnPhaseEnter(GamePhase.Initialization, async state =>
            {
                Console.WriteLine("\n[System] 初始化：分配身份...");
                _ruleEngine.AssignIdentities(_mockPlayerIds, state);
                await Task.CompletedTask;
            });

            _fsm.OnPhaseEnter(GamePhase.DayExploration, async state =>
            {
                Console.WriteLine("\n[Day] 白天开始：初始化回合 + 摸牌");
                _ruleEngine.InitializeRound(state);

                foreach (var p in state.AlivePlayers.ToList())
                {
                    _ruleEngine.DrawCardFromChest(p.PlayerId, _rng.NextDouble() > 0.5, state);
                }

                // Demo剧本注入（方便观察关键规则）
                if (state.CurrentRound == 2)
                {
                    var guardianLeader = state.AlivePlayers
                        .FirstOrDefault(p => p.Faction == Faction.Guardian && p.Role == PlayerRole.Leader);

                    var thief = state.AlivePlayers.FirstOrDefault(p => p.Faction == Faction.Thief);

                    if (guardianLeader != null && thief != null)
                    {
                        guardianLeader.PoisonStacks += 1;
                        guardianLeader.PoisonSourceQueue.Enqueue(thief.PlayerId);
                        Console.WriteLine($"[Inject] 第2回合：守卫老大 {guardianLeader.DisplayName} 被下毒。");
                    }
                }
                else if (state.CurrentRound == 3)
                {
                    var lover = state.AlivePlayers.FirstOrDefault(p => p.Faction == Faction.Lovers);
                    if (lover != null)
                    {
                        lover.PoisonStacks += 3;
                        Console.WriteLine($"[Inject] 第3回合：恋人 {lover.DisplayName} 被下3层毒。");
                    }
                }

                await Task.CompletedTask;
            });

            _fsm.OnPhaseEnter(GamePhase.DinnerPhase, async state =>
            {
                Console.WriteLine("\n[Dinner] 晚餐结算：毒药生效");
                var events = _ruleEngine.SettleDinnerPhase(state);
                PrintEvents(events);
                await Task.CompletedTask;
            });

            _fsm.OnPhaseEnter(GamePhase.NightPhase, async state =>
            {
                Console.WriteLine("\n[Night] 夜晚阶段：盗贼提交意向并结算");

                foreach (var thief in state.AlivePlayers.Where(p => p.Faction == Faction.Thief))
                    _ruleEngine.RegisterThiefNightIntent(thief.PlayerId, intendToSteal: true, state);

                var events = _ruleEngine.SettleNightPhase(state);
                PrintEvents(events);

                await Task.CompletedTask;
            });

            _fsm.OnPhaseEnter(GamePhase.RoundSettlement, async state =>
            {
                Console.WriteLine("\n[Settlement] 回合末：检查胜利条件");
                if (_ruleEngine.CheckVictoryConditions(state))
                    Console.WriteLine("[System] 已触发胜利条件，即将结束游戏。");

                await Task.CompletedTask;
            });

            _fsm.OnPhaseEnter(GamePhase.GameOver, async state =>
            {
                Console.WriteLine("\n[GameOver] 游戏结束阶段");
                await Task.CompletedTask;
            });
        }

        public async Task StartGameAsync()
        {
            Console.WriteLine("=== 《第十夜》服务端自动推演启动 ===");

            await _fsm.TransitionToAsync(GamePhase.Initialization, _gameState);

            while (_gameState.CurrentRound < _gameState.MaxRounds && _gameState.Result == null)
            {
                _gameState.CurrentRound++;

                Console.WriteLine("\n================================================");
                Console.WriteLine($">>> 第 {_gameState.CurrentRound} / {_gameState.MaxRounds} 回合");
                Console.WriteLine("================================================");

                await _fsm.TransitionToAsync(GamePhase.DayExploration, _gameState);
                await _fsm.TransitionToAsync(GamePhase.DinnerPhase, _gameState);
                await _fsm.TransitionToAsync(GamePhase.NightPhase, _gameState);
                await _fsm.TransitionToAsync(GamePhase.RoundSettlement, _gameState);
            }

            await _fsm.TransitionToAsync(GamePhase.GameOver, _gameState);

            if (_gameState.Result != null)
            {
                Console.WriteLine($"\n=== 胜利阵营: {_gameState.Result.WinningFaction} ===");
                Console.WriteLine($"=== 结局描述: {_gameState.Result.WinConditionDescription} ===");
                Console.WriteLine($"=== 胜者人数: {_gameState.Result.WinnerPlayerIds.Count} ===");
            }
            else
            {
                Console.WriteLine("\n=== 到达最大回合，未触发胜利条件 ===");
            }
        }

        private static void PrintEvents(List<GameEvent> events)
        {
            if (events.Count == 0) return;
            Console.WriteLine($"[Event] 本阶段事件数: {events.Count}");
            foreach (var e in events)
                Console.WriteLine($"  - {e.GetType().Name} ({e.EventId})");
        }
    }
}