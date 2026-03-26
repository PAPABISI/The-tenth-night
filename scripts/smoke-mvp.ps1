param(
    [string]$BaseUrl = "http://localhost:5000"
)

$ErrorActionPreference = "Stop"

function Assert-HasValue {
    param(
        [string]$Name,
        $Value
    )

    if ($null -eq $Value -or ($Value -is [string] -and [string]::IsNullOrWhiteSpace($Value))) {
        throw "Assertion failed: $Name is empty."
    }
}

function Assert-True {
    param(
        [string]$Name,
        [bool]$Condition
    )

    if (-not $Condition) {
        throw "Assertion failed: $Name"
    }
}

Write-Host "[Smoke] BaseUrl=$BaseUrl"

$create = Invoke-RestMethod -Method Post -Uri "$BaseUrl/room/create"
$roomId = [string]$create.roomId
Assert-HasValue -Name "roomId" -Value $roomId
Write-Host "[Smoke] Room created: $roomId"

$players = @("Alpha", "Bravo", "Charlie", "Delta")
$joined = @()

foreach ($name in $players) {
    $joinReq = @{ displayName = $name } | ConvertTo-Json
    $join = Invoke-RestMethod -Method Post -Uri "$BaseUrl/room/$roomId/join" -ContentType "application/json" -Body $joinReq

    $playerId = [string]$join.playerId
    Assert-HasValue -Name "playerId($name)" -Value $playerId

    $joined += [PSCustomObject]@{
        displayName = $name
        playerId = $playerId
    }

    Write-Host "[Smoke] Joined: $name -> $playerId"
}

$startReq = @{ playerIds = @() } | ConvertTo-Json
$start = Invoke-RestMethod -Method Post -Uri "$BaseUrl/room/$roomId/start" -ContentType "application/json" -Body $startReq
Assert-True -Name "start round is 1" -Condition ($start.round -eq 1)
Assert-True -Name "start alivePlayers >= 4" -Condition ($start.alivePlayers -ge 4)
Write-Host "[Smoke] Started: round=$($start.round), phase=$($start.phase), alive=$($start.alivePlayers)"

$local = $joined[0]
$state = Invoke-RestMethod -Method Get -Uri "$BaseUrl/room/$roomId/state/$($local.playerId)"
Assert-HasValue -Name "state.phase" -Value ([string]$state.phase)
Assert-True -Name "state.self exists" -Condition ($null -ne $state.self)
Assert-True -Name "state.publics exists" -Condition ($null -ne $state.publics)
Write-Host "[Smoke] Initial state phase=$($state.phase), handCount=$($state.self.hand.Count)"

$next1 = Invoke-RestMethod -Method Post -Uri "$BaseUrl/room/$roomId/phase/next" -ContentType "application/json" -Body "{}"
Assert-True -Name "phase advanced to DayExploration" -Condition ($next1.phase -eq "DayExploration")
Write-Host "[Smoke] Phase next => $($next1.phase)"

$moveReq = @{
    userId = $local.playerId
    x = 2.5
    y = -1.25
} | ConvertTo-Json
$moveRes = Invoke-RestMethod -Method Post -Uri "$BaseUrl/room/$roomId/action/move" -ContentType "application/json" -Body $moveReq
Assert-True -Name "move ok" -Condition ($moveRes.ok -eq $true)
Write-Host "[Smoke] Move sync OK"

$state = Invoke-RestMethod -Method Get -Uri "$BaseUrl/room/$roomId/state/$($local.playerId)"
Assert-True -Name "self hasPosition" -Condition ($state.self.hasPosition -eq $true)
Assert-True -Name "self x synced" -Condition ([math]::Abs([double]$state.self.x - 2.5) -lt 0.001)
Assert-True -Name "self y synced" -Condition ([math]::Abs([double]$state.self.y + 1.25) -lt 0.001)
$target = $state.publics | Where-Object { $_.playerId -ne $local.playerId -and $_.isAlive -eq $true } | Select-Object -First 1
$firstCard = $state.self.hand | Select-Object -First 1

if ($null -ne $target -and $null -ne $firstCard) {
    $useReq = @{
        userId = $local.playerId
        targetId = [string]$target.playerId
        cardId = [string]$firstCard.cardId
    } | ConvertTo-Json

    $useRes = Invoke-RestMethod -Method Post -Uri "$BaseUrl/room/$roomId/action/use-card" -ContentType "application/json" -Body $useReq
    Assert-True -Name "use-card has eventCount" -Condition ($null -ne $useRes.eventCount)
    Write-Host "[Smoke] Use-card OK: eventCount=$($useRes.eventCount)"
}
else {
    Write-Host "[Smoke] Skip use-card (missing target or card)."
}

$next2 = Invoke-RestMethod -Method Post -Uri "$BaseUrl/room/$roomId/phase/next" -ContentType "application/json" -Body "{}"
Assert-True -Name "phase advanced to DinnerPhase" -Condition ($next2.phase -eq "DinnerPhase")
Write-Host "[Smoke] Phase next => $($next2.phase)"

$next3 = Invoke-RestMethod -Method Post -Uri "$BaseUrl/room/$roomId/phase/next" -ContentType "application/json" -Body "{}"
Assert-True -Name "phase advanced to NightPhase" -Condition ($next3.phase -eq "NightPhase")
Write-Host "[Smoke] Phase next => $($next3.phase)"

Write-Host "[Smoke] PASS: MVP API chain is healthy." -ForegroundColor Green
