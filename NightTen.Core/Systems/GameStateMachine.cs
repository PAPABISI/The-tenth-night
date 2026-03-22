using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NightTen.Core
{
    public class GameStateMachine : IGameStateMachine
    {
        public GamePhase CurrentPhase { get; private set; } = GamePhase.WaitingForPlayers;

        private readonly Dictionary<GamePhase, List<Func<GameState, Task>>> _enterHandlers = new();
        private readonly Dictionary<GamePhase, List<Func<GameState, Task>>> _exitHandlers = new();

        private bool _isTransitioning = false;

        public GameStateMachine()
        {
            foreach (GamePhase phase in Enum.GetValues(typeof(GamePhase)))
            {
                _enterHandlers[phase] = new List<Func<GameState, Task>>();
                _exitHandlers[phase] = new List<Func<GameState, Task>>();
            }
        }

        public void OnPhaseEnter(GamePhase phase, Func<GameState, Task> handler)
        {
            if (handler != null) _enterHandlers[phase].Add(handler);
        }

        public void OnPhaseExit(GamePhase phase, Func<GameState, Task> handler)
        {
            if (handler != null) _exitHandlers[phase].Add(handler);
        }

        public async Task TransitionToAsync(GamePhase targetPhase, GameState state)
        {
            if (_isTransitioning)
                throw new InvalidOperationException($"[FSM] 正在切换中，拒绝并发请求: {CurrentPhase} -> {targetPhase}");

            if (CurrentPhase == targetPhase)
            {
                Console.WriteLine($"[FSM] 忽略相同状态切换: {targetPhase}");
                return;
            }

            _isTransitioning = true;
            var previous = CurrentPhase;

            try
            {
                Console.WriteLine($"[FSM] {previous} -> {targetPhase}");

                foreach (var handler in _exitHandlers[previous])
                    await handler(state);

                CurrentPhase = targetPhase;
                state.CurrentPhase = targetPhase;

                foreach (var handler in _enterHandlers[targetPhase])
                    await handler(state);

                Console.WriteLine($"[FSM] 已进入: {targetPhase}");
            }
            finally
            {
                _isTransitioning = false;
            }
        }
    }
}