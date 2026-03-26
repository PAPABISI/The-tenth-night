using NightTen.Core;
using System.Collections.Concurrent;

namespace NightTen.Server;

public class RoomRuntime
{
    public Guid RoomId { get; init; }
    public GameState State { get; init; } = new();
    public IGameStateMachine Fsm { get; init; } = new GameStateMachine();
    public IGameRuleEngine RuleEngine { get; init; } = new GameRuleEngine();
    public object SyncRoot { get; } = new();
    public HashSet<Guid> JoinedPlayerIds { get; } = new();
}

public class RoomStore
{
    private readonly ConcurrentDictionary<Guid, RoomRuntime> _rooms = new();

    public RoomRuntime CreateRoom()
    {
        var roomId = Guid.NewGuid();
        var room = new RoomRuntime
        {
            RoomId = roomId,
            State = new GameState { RoomId = roomId },
            Fsm = new GameStateMachine(),
            RuleEngine = new GameRuleEngine()
        };

        _rooms[roomId] = room;
        return room;
    }

    public bool TryGetRoom(Guid roomId, out RoomRuntime room) => _rooms.TryGetValue(roomId, out room!);
}