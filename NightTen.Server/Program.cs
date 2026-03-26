using NightTen.Core;
using NightTen.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<RoomStore>();

var app = builder.Build();

app.MapPost("/room/create", (RoomStore store) =>
{
    var room = store.CreateRoom();
    return Results.Ok(new { roomId = room.RoomId });
});

app.MapPost("/room/{roomId:guid}/join", (Guid roomId, JoinRoomRequest req, RoomStore store) =>
{
    if (!store.TryGetRoom(roomId, out var room))
        return Results.NotFound("Room not found.");

    if (string.IsNullOrWhiteSpace(req.DisplayName))
        return Results.BadRequest("DisplayName is required.");

    lock (room.SyncRoot)
    {
        if (room.State.CurrentPhase != GamePhase.WaitingForPlayers)
            return Results.BadRequest("Game already started.");

        var playerId = Guid.NewGuid();
        room.JoinedPlayerIds.Add(playerId);

        var player = new Player
        {
            PlayerId = playerId,
            DisplayName = req.DisplayName.Trim(),
            IsAlive = true,
            CurrentAp = 1,
            CurrentHp = 3,
            MaxHp = 3,
            Role = PlayerRole.Member,
            Faction = Faction.Guardian
        };

        room.State.AllPlayers[playerId] = player;
        return Results.Ok(new { roomId, playerId, displayName = player.DisplayName });
    }
});

app.MapPost("/room/{roomId:guid}/start", async (Guid roomId, StartGameRequest req, RoomStore store) =>
{
    if (!store.TryGetRoom(roomId, out var room))
        return Results.NotFound("Room not found.");

    List<Guid> playerIds;
    lock (room.SyncRoot)
    {
        if (room.State.CurrentPhase != GamePhase.WaitingForPlayers)
            return Results.BadRequest("Game already started.");

        playerIds = room.JoinedPlayerIds.ToList();
        if (playerIds.Count == 0 && req.PlayerIds is { Count: > 0 })
            playerIds = req.PlayerIds.Distinct().ToList();

        if (playerIds.Count < 4)
            return Results.BadRequest("At least 4 players are required to start.");
    }

    await room.Fsm.TransitionToAsync(GamePhase.Initialization, room.State);

    lock (room.SyncRoot)
    {
        room.RuleEngine.AssignIdentities(playerIds, room.State);
        room.RuleEngine.InitializePlayersForNewGame(room.State);
        room.RuleEngine.InitializeDeckAndDealInitialHands(room.State, 4);

        return Results.Ok(new
        {
            roomId,
            round = room.State.CurrentRound,
            phase = room.State.CurrentPhase.ToString(),
            alivePlayers = room.State.AlivePlayers.Count
        });
    }
});

app.MapPost("/room/{roomId:guid}/action/use-card", (Guid roomId, UseCardRequest req, RoomStore store) =>
{
    if (!store.TryGetRoom(roomId, out var room))
        return Results.NotFound("Room not found.");

    List<GameEvent> events;
    lock (room.SyncRoot)
    {
        events = room.RuleEngine.ProcessCardInteraction(req.UserId, req.TargetId, req.CardId, room.State);
    }

    return Results.Ok(new
    {
        eventCount = events.Count,
        events
    });
});

app.MapPost("/room/{roomId:guid}/action/draw", (Guid roomId, DrawCardRequest req, RoomStore store) =>
{
    if (!store.TryGetRoom(roomId, out var room))
        return Results.NotFound("Room not found.");

    Card? card;
    lock (room.SyncRoot)
    {
        if (room.State.CurrentPhase != GamePhase.DayExploration)
            return Results.BadRequest("Draw is only allowed in DayExploration.");

        card = room.RuleEngine.DrawCardFromChest(req.UserId, req.IsRedChest, room.State);
        if (card == null)
            return Results.BadRequest("Draw failed. Maybe already drawn this round or hand is full.");
    }

    return Results.Ok(new
    {
        card = card.ToClientView()
    });
});

app.MapPost("/room/{roomId:guid}/action/move", (Guid roomId, MoveRequest req, RoomStore store) =>
{
    if (!store.TryGetRoom(roomId, out var room))
        return Results.NotFound("Room not found.");

    lock (room.SyncRoot)
    {
        if (room.State.CurrentPhase == GamePhase.GameOver)
            return Results.BadRequest("Game is already over.");

        if (!room.State.AllPlayers.ContainsKey(req.UserId))
            return Results.NotFound("Player not found.");

        room.RuleEngine.UpdatePlayerPosition(req.UserId, req.X, req.Y, room.State);
    }

    return Results.Ok(new { ok = true });
});

app.MapPost("/room/{roomId:guid}/action/night-intent", (Guid roomId, NightIntentRequest req, RoomStore store) =>
{
    if (!store.TryGetRoom(roomId, out var room))
        return Results.NotFound("Room not found.");

    lock (room.SyncRoot)
    {
        room.RuleEngine.RegisterThiefNightIntent(req.UserId, req.IntendToSteal, room.State);
    }

    return Results.Ok(new
    {
        ok = true,
        phase = room.State.CurrentPhase.ToString()
    });
});

app.MapPost("/room/{roomId:guid}/phase/next", (Guid roomId, RoomStore store) =>
{
    if (!store.TryGetRoom(roomId, out var room))
        return Results.NotFound("Room not found.");

    lock (room.SyncRoot)
    {
        var state = room.State;
        var events = new List<GameEvent>();

        switch (state.CurrentPhase)
        {
            case GamePhase.Initialization:
                state.CurrentPhase = GamePhase.DayExploration;
                room.RuleEngine.InitializeRound(state);
                break;

            case GamePhase.DayExploration:
                state.CurrentPhase = GamePhase.DinnerPhase;
                break;

            case GamePhase.DinnerPhase:
                events.AddRange(room.RuleEngine.SettleDinnerPhase(state));
                state.CurrentPhase = GamePhase.NightPhase;
                break;

            case GamePhase.NightPhase:
                events.AddRange(room.RuleEngine.SettleNightPhase(state));
                state.CurrentPhase = GamePhase.RoundSettlement;
                break;

            case GamePhase.RoundSettlement:
                if (room.RuleEngine.CheckVictoryConditions(state))
                {
                    state.CurrentPhase = GamePhase.GameOver;
                    return Results.Ok(new
                    {
                        round = state.CurrentRound,
                        phase = state.CurrentPhase.ToString(),
                        isGameOver = true,
                        result = state.Result,
                        events
                    });
                }

                state.CurrentRound++;
                if (state.CurrentRound > state.MaxRounds)
                {
                    room.RuleEngine.CheckVictoryConditions(state);
                    state.CurrentPhase = GamePhase.GameOver;

                    return Results.Ok(new
                    {
                        round = state.CurrentRound,
                        phase = state.CurrentPhase.ToString(),
                        isGameOver = true,
                        result = state.Result,
                        events
                    });
                }

                room.RuleEngine.InitializeRound(state);
                state.CurrentPhase = GamePhase.DayExploration;
                break;

            case GamePhase.GameOver:
                return Results.BadRequest("Game is already over.");

            default:
                return Results.BadRequest("Unknown phase.");
        }

        return Results.Ok(new
        {
            round = state.CurrentRound,
            phase = state.CurrentPhase.ToString(),
            isGameOver = state.CurrentPhase == GamePhase.GameOver,
            events
        });
    }
});

app.MapGet("/room/{roomId:guid}/state/{playerId:guid}", (Guid roomId, Guid playerId, RoomStore store) =>
{
    if (!store.TryGetRoom(roomId, out var room))
        return Results.NotFound("Room not found.");

    lock (room.SyncRoot)
    {
        if (!room.State.AllPlayers.ContainsKey(playerId))
            return Results.NotFound("Player not found.");

        var self = room.RuleEngine.GetSelfView(playerId, room.State, room.State.CurrentPhase);
        var publics = room.RuleEngine.GetPublicPlayerViews(room.State);

        return Results.Ok(new
        {
            roomId,
            round = room.State.CurrentRound,
            phase = room.State.CurrentPhase.ToString(),
            self,
            publics,
            treasureState = room.State.TreasureState.ToString(),
            treasureHolderId = room.State.TreasureHolderId,
            gameResult = room.State.Result
        });
    }
});

app.Run("http://0.0.0.0:5000");

public record DrawCardRequest(Guid UserId, bool IsRedChest);
public record MoveRequest(Guid UserId, float X, float Y);
public record NightIntentRequest(Guid UserId, bool IntendToSteal);
