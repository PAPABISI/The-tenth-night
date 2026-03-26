using System;
using System.Collections.Generic;
using System.Linq;
using NightTen.Core;

namespace NightTen.Core.Tests;

public class GameRuleEngineTests
{
    [Fact]
    public void AssignIdentities_ShouldKeepJoinedDisplayNames()
    {
        var engine = new GameRuleEngine();
        var state = new GameState { RoomId = Guid.NewGuid() };
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();

        foreach (var id in ids)
        {
            state.AllPlayers[id] = new Player
            {
                PlayerId = id,
                DisplayName = $"U_{id.ToString()[..4]}"
            };
        }

        var expected = state.AllPlayers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DisplayName);

        engine.AssignIdentities(ids, state);

        foreach (var id in ids)
        {
            Assert.True(state.AllPlayers.ContainsKey(id));
            Assert.Equal(expected[id], state.AllPlayers[id].DisplayName);
        }
    }

    [Fact]
    public void ExecuteLeaderInspection_ShouldAllowDeadInspectableCorpse()
    {
        var engine = new GameRuleEngine();
        var state = new GameState
        {
            RoomId = Guid.NewGuid(),
            CurrentPhase = GamePhase.DinnerPhase
        };

        var inspectorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        state.AllPlayers[inspectorId] = new Player
        {
            PlayerId = inspectorId,
            DisplayName = "Leader",
            Faction = Faction.Guardian,
            Role = PlayerRole.Leader,
            IsAlive = true
        };

        state.AllPlayers[targetId] = new Player
        {
            PlayerId = targetId,
            DisplayName = "DeadTarget",
            Faction = Faction.Thief,
            Role = PlayerRole.Member,
            IsAlive = false,
            HasInspectableCorpse = true
        };

        var result = engine.ExecuteLeaderInspection(inspectorId, targetId, state);

        Assert.NotNull(result);
        Assert.Equal(inspectorId, result!.InspectorId);
        Assert.Equal(targetId, result.TargetId);
        Assert.Equal(Faction.Thief, result.TargetFaction);
    }

    [Fact]
    public void ProcessPlayerDeath_ShouldCascadeForLoversOnce()
    {
        var engine = new GameRuleEngine();
        var state = new GameState { RoomId = Guid.NewGuid() };

        var loverA = new Player
        {
            PlayerId = Guid.NewGuid(),
            DisplayName = "LoverA",
            Faction = Faction.Lovers,
            Role = PlayerRole.Member,
            IsAlive = true,
            CurrentHp = 1,
            MaxHp = 3
        };

        var loverB = new Player
        {
            PlayerId = Guid.NewGuid(),
            DisplayName = "LoverB",
            Faction = Faction.Lovers,
            Role = PlayerRole.Member,
            IsAlive = true,
            CurrentHp = 1,
            MaxHp = 3
        };

        state.AllPlayers[loverA.PlayerId] = loverA;
        state.AllPlayers[loverB.PlayerId] = loverB;
        state.AlivePlayers.AddRange(new[] { loverA, loverB });

        var events = engine.ProcessPlayerDeath(loverA.PlayerId, CardType.Poison, null, state);
        var deaths = events.OfType<PlayerDiedEvent>().ToList();

        Assert.False(loverA.IsAlive);
        Assert.False(loverB.IsAlive);
        Assert.Empty(state.AlivePlayers);
        Assert.Equal(2, deaths.Count);
    }
}
