# The Tenth Night — Multiplayer Game Server

**The Tenth Night** (《第十夜》) is a server-authoritative backend prototype for a 6-player asymmetric
hidden-role strategy game. Players belong to one of three factions (Guardian, Thief, Lovers) and
interact through a card system across up to 10 rounds. This repository provides the full game-rule
engine, a phase state machine, and a minimal REST API ready for integration with a Unity client.

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Technology Stack](#2-technology-stack)
3. [Directory Structure](#3-directory-structure)
4. [Architecture & Design Patterns](#4-architecture--design-patterns)
5. [Game Rules](#5-game-rules)
6. [API Reference](#6-api-reference)
7. [Quick Start](#7-quick-start)
8. [Usage Examples](#8-usage-examples)
9. [Related Repository](#9-related-repository)

---

## 1. Project Overview

The server follows a **server-authoritative** model:

- The server owns the single source of truth (`GameState`).
- Clients receive only the information they are permitted to see (their own hand, HP, faction; never
  another player's hidden data).
- Every critical rule — card effects, death processing, treasure transfers, leadership inheritance,
  and win-condition evaluation — runs exclusively on the server.

The project is intentionally focused on the rule system and server flow; it does not include a
front-end presentation layer.

---

## 2. Technology Stack

| Concern | Choice |
|---|---|
| Language | C# |
| Runtime | .NET 10 |
| Backend framework | ASP.NET Core Minimal APIs |
| HTTP package | `Microsoft.AspNetCore.OpenApi` |
| Build tool | `dotnet` CLI |
| Package manager | NuGet |
| Persistence | In-memory only (`ConcurrentDictionary`) |

---

## 3. Directory Structure

```text
The-tenth-night/
├── Tenth.slnx                        # Solution file (references both projects)
├── README.md
│
├── NightTen.Core/                    # Class library — all game logic (no web dependency)
│   ├── NightTen.Core.csproj
│   ├── Models/
│   │   └── CoreDataStructures.cs     # Enums, Player, Card, GameState, view projections
│   ├── Interfaces/
│   │   └── GameInterfaces.cs         # ICardEffect, IGameRuleEngine, IGameStateMachine, GameEvent hierarchy
│   └── Systems/
│       ├── GameStateMachine.cs       # Finite state machine managing phase transitions
│       ├── GameRuleEngine.cs         # Core rule engine (~477 lines)
│       ├── CardEffects.cs            # One strategy class per card type
│       └── GameLoopRunner.cs         # Optional demo auto-play runner (local testing)
│
└── NightTen.Server/                  # ASP.NET Core web application
    ├── NightTen.Server.csproj
    ├── Program.cs                    # All Minimal API endpoint definitions
    ├── RoomStore.cs                  # Thread-safe in-memory room registry
    ├── Contracts.cs                  # Request record types (DTOs)
    ├── NightTen.Server.http          # HTTP test file (VS Code REST Client / Rider)
    ├── appsettings.json
    ├── appsettings.Development.json
    └── Properties/
        └── launchSettings.json       # Launch profiles (HTTP :5234 / HTTPS :7161)
```

### Key files at a glance

| File | Responsibility |
|---|---|
| `CoreDataStructures.cs` | Defines every enum (`GamePhase`, `Faction`, `PlayerRole`, `CardType`, `TreasureState`) and every data class (`Player`, `Card`, `GameState`, `GameResult`, `DeathRecord`). Also defines the two view projections used for information hiding (`PlayerSelfView`, `PlayerPublicView`). |
| `GameInterfaces.cs` | Contracts that decouple the rule engine from card implementations (`ICardEffect`) and decouple the API from the engine (`IGameRuleEngine`, `IGameStateMachine`). Also contains the `GameEvent` discriminated union. |
| `GameStateMachine.cs` | Manages the legal sequence of `GamePhase` values, fires `OnEnter`/`OnExit` lifecycle hooks asynchronously, and prevents concurrent transitions. |
| `GameRuleEngine.cs` | The heart of the game: identity assignment, deck management, card interaction dispatch, dinner/night settlement, the death pipeline (treasure transfer → leadership inheritance → lover link), and victory evaluation. |
| `CardEffects.cs` | Five concrete `ICardEffect` implementations: `GunShotEffect`, `PoisonEffect`, `AntidoteEffect`, `BandageEffect`, `BulletProofEffect`. |
| `GameLoopRunner.cs` | A self-contained demo that wires up phase hooks and drives an automated game loop — useful for rule validation without a client. |
| `Program.cs` | Eight Minimal API endpoints. Each endpoint resolves the room from `RoomStore`, delegates to `IGameRuleEngine` or `IGameStateMachine`, and returns JSON. |
| `RoomStore.cs` | Wraps a `ConcurrentDictionary<Guid, RoomRuntime>` to provide thread-safe room creation and lookup. `RoomRuntime` bundles `GameState`, `GameStateMachine`, and `GameRuleEngine` together. |

---

## 4. Architecture & Design Patterns

### Layer diagram

```
┌──────────────────────────────────┐
│  ASP.NET Core Minimal API        │  NightTen.Server / Program.cs
│  (HTTP routing, JSON serialize)  │
└────────────────┬─────────────────┘
                 │ calls
┌────────────────▼─────────────────┐
│  Room Management                 │  RoomStore / RoomRuntime
└────────────────┬─────────────────┘
                 │ owns
┌────────────────▼─────────────────┐
│  Game Rule Engine                │  GameRuleEngine  ◄──── IGameRuleEngine
│  (all game logic lives here)     │
└──────┬──────────────┬────────────┘
       │ reads/writes │ dispatches card effects via
┌──────▼──────┐  ┌────▼──────────────────────────┐
│  GameState  │  │  ICardEffect strategy objects   │
│  (the       │  │  GunShotEffect, PoisonEffect,   │
│  truth)     │  │  AntidoteEffect, BandageEffect, │
└──────┬──────┘  │  BulletProofEffect              │
       │         └────────────────────────────────┘
┌──────▼──────────────────────────┐
│  GameStateMachine               │  manages phase transitions + lifecycle hooks
└─────────────────────────────────┘
```

### Design patterns used

| Pattern | Where | Why |
|---|---|---|
| **Strategy** | `ICardEffect` / `CardEffects.cs` | Each card type is an independent class; adding a new card requires no change to the rule engine. |
| **State Machine** | `GameStateMachine` | Encodes the legal phase sequence and prevents invalid transitions. Async hooks decouple phase entry/exit logic from the engine. |
| **Information Hiding (Projection)** | `Player.ToSelfView()` / `Player.ToPublicView()` | Ensures the API never leaks a player's faction, hand, or poison status to other clients. |
| **Repository** | `RoomStore` | Centralises room lifecycle management; the API layer never touches raw state. |
| **Dependency Injection** | `builder.Services.AddSingleton<RoomStore>()` | `RoomStore` is injected into every endpoint via the ASP.NET Core DI container. |

---

## 5. Game Rules

### Factions (6 players total)

| Faction | Count | Role split |
|---|---|---|
| Guardian | 2–3 | 1 Leader + rest Members |
| Thief | 2 | 1 Leader + 1 Member |
| Lovers | 2 | (no role distinction) |

### Phase flow (per round)

```
Initialization ──► DayExploration ──► DinnerPhase ──► NightPhase ──► RoundSettlement
                                                                             │
                                                                    (repeat up to 10 rounds
                                                                     or until win condition)
                                                                             │
                                                                         GameOver
```

| Phase | What happens |
|---|---|
| **Initialization** | Identities assigned; initial hands dealt (4 cards per player). |
| **DayExploration** | Players draw one card from a Red or Blue chest; they may play cards (GunShot, Poison, Bandage, BulletProof) on themselves or others. |
| **DinnerPhase** | Accumulated poison stacks resolve into HP damage. The Guardian Leader may use the once-per-game faction-inspection ability. |
| **NightPhase** | Each Thief submits a steal intent; the engine resolves treasure transfer to the spawn point or a thief. |
| **RoundSettlement** | Win conditions are evaluated. If no winner, the round counter increments and play continues. |
| **GameOver** | A `GameResult` is attached to `GameState`; further phase advances are rejected. |

### Cards

| Card | Usable phase | Effect |
|---|---|---|
| **GunShot** | DayExploration | Instant lethal damage. Cancelled if target has BulletProof buff. |
| **Poison** | DayExploration | Adds a hidden poison stack; resolved at DinnerPhase. |
| **Antidote** | DinnerPhase | Removes one poison stack and restores HP. |
| **Bandage** | DayExploration or DinnerPhase | Restores 1 HP. |
| **BulletProof** | DayExploration | Grants a one-shot GunShot immunity buff. |

Cards are drawn from two weighted chests (Red / Blue) via `DrawCardFromChest`.

### Death pipeline

When a player dies the rule engine runs these steps in order:

1. Remove from `AlivePlayers`; mark corpse as inspectable.
2. Transfer treasure — to the killer if appropriate, otherwise back to spawn point.
3. Leadership inheritance — if the victim was a Leader, the next Member in the same faction is
   promoted.
4. Lover link — if either Lover dies, the other Lover also dies (processed recursively through the
   same pipeline).
5. Append a `DeathRecord` to `RoundDeathLog`.

### Victory conditions (evaluated in order)

1. **Immediate faction elimination** — if all Guardians or all Thieves are dead, the opposing
   faction wins immediately.
2. **Lovers independent win** — if both Lovers are alive and hold the treasure at round end, they
   win regardless of other factions.
3. **Treasure control at max rounds** — after round 10, whichever faction holds (or last held) the
   treasure wins.

---

## 6. API Reference

The server binds to all interfaces on port 5000 (`http://0.0.0.0:5000`); access it via `http://localhost:5000` from the same machine.

| Method | Path | Description |
|---|---|---|
| `POST` | `/room/create` | Create a new game room. Returns `{ roomId }`. |
| `POST` | `/room/{roomId}/join` | Join a room (simplified lobby). Returns `{ roomId, playerId, displayName }`. |
| `POST` | `/room/{roomId}/start` | Start the game with a provided list of player IDs. Runs Initialization phase. |
| `POST` | `/room/{roomId}/phase/next` | Advance to the next phase; triggers phase settlement (poison, night theft, victory check). |
| `POST` | `/room/{roomId}/action/draw` | Draw one card from Red (`isRedChest: true`) or Blue chest. DayExploration only. |
| `POST` | `/room/{roomId}/action/use-card` | Play a card (`cardId`) from `userId` onto `targetId`. |
| `POST` | `/room/{roomId}/action/night-intent` | Register whether a Thief intends to steal (`intendToSteal: bool`). |
| `GET` | `/room/{roomId}/state/{playerId}` | Return the permission-filtered view for `playerId` (own hand + public info for all others). |

### Request bodies

```jsonc
// POST /room/{id}/start
{ "playerIds": ["<guid>", "<guid>", ...] }   // exactly 6 player GUIDs

// POST /room/{id}/action/use-card
{ "userId": "<guid>", "targetId": "<guid>", "cardId": "<guid>" }

// POST /room/{id}/action/draw
{ "userId": "<guid>", "isRedChest": true }

// POST /room/{id}/action/night-intent
{ "userId": "<guid>", "intendToSteal": true }
```

---

## 7. Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Build

```bash
dotnet build Tenth.slnx
```

### Run the server

```bash
cd NightTen.Server
dotnet run
# Server starts at http://localhost:5000
```

---

## 8. Usage Examples

### PowerShell

```powershell
# 1. Create a room
$create = Invoke-RestMethod -Method Post -Uri "http://localhost:5000/room/create"
$roomId = $create.roomId

# 2. Start the game with six players
$body = @{
    playerIds = @(
        "11111111-1111-1111-1111-111111111111",
        "22222222-2222-2222-2222-222222222222",
        "33333333-3333-3333-3333-333333333333",
        "44444444-4444-4444-4444-444444444444",
        "55555555-5555-5555-5555-555555555555",
        "66666666-6666-6666-6666-666666666666"
    )
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
    -Uri "http://localhost:5000/room/$roomId/start" `
    -ContentType "application/json" `
    -Body $body

# 3. Advance phase (e.g. Initialization -> DayExploration)
Invoke-RestMethod -Method Post -Uri "http://localhost:5000/room/$roomId/phase/next"

# 4. Draw a card (Red chest)
$draw = @{ userId = "11111111-1111-1111-1111-111111111111"; isRedChest = $true } | ConvertTo-Json
Invoke-RestMethod -Method Post `
    -Uri "http://localhost:5000/room/$roomId/action/draw" `
    -ContentType "application/json" `
    -Body $draw

# 5. Query a player's view
Invoke-RestMethod -Method Get `
    -Uri "http://localhost:5000/room/$roomId/state/11111111-1111-1111-1111-111111111111"
```

### curl

```bash
# Create room
curl -s -X POST http://localhost:5000/room/create

# Start game
curl -s -X POST http://localhost:5000/room/<roomId>/start \
     -H "Content-Type: application/json" \
     -d '{"playerIds":["11111111-1111-1111-1111-111111111111","22222222-2222-2222-2222-222222222222","33333333-3333-3333-3333-333333333333","44444444-4444-4444-4444-444444444444","55555555-5555-5555-5555-555555555555","66666666-6666-6666-6666-666666666666"]}'

# Get player state
curl -s http://localhost:5000/room/<roomId>/state/11111111-1111-1111-1111-111111111111
```

---

## 9. Related Repository

Unity client: <https://github.com/PAPABISI/the-tenth-night-unity.git>
