namespace NightTen.Server;

public record JoinRoomRequest(string DisplayName);
public record StartGameRequest(List<Guid>? PlayerIds);
public record UseCardRequest(Guid UserId, Guid? TargetId, Guid CardId);