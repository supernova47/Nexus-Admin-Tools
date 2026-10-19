param(
    [string]$ManagedDir = '',
    [string]$CecilDll = (Join-Path $env:APPDATA 'r2modmanPlus-local\Valheim\profiles\Default\BepInEx\core\Mono.Cecil.dll')
)
$ErrorActionPreference = 'Stop'
if (-not $ManagedDir) {
    $managedCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Valheim\valheim_Data\Managed'),
        (Join-Path $env:ProgramFiles 'Steam\steamapps\common\Valheim\valheim_Data\Managed')
    ) | Where-Object { Test-Path (Join-Path $_ 'assembly_valheim.dll') }
    $ManagedDir = $managedCandidates | Select-Object -First 1
}
if (-not $ManagedDir -or -not (Test-Path (Join-Path $ManagedDir 'assembly_valheim.dll'))) {
    throw 'Pass -ManagedDir with the Valheim valheim_Data\Managed folder.'
}
Add-Type -LiteralPath $CecilDll
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $ManagedDir 'assembly_valheim.dll'))
try {
    $character = $game.MainModule.Types | Where-Object Name -eq 'Character'
    $player = $game.MainModule.Types | Where-Object Name -eq 'Player'
    $attack = $game.MainModule.Types | Where-Object Name -eq 'Attack'
    function Body($type, $method) { [string]::Join(' ', ($type.Methods | Where-Object Name -eq $method).Body.Instructions) }
    $checks = 0
    foreach($pattern in @('ZNetView::IsOwner', 'ZDO::Set', 'ZDOVars::s_health')) {
        if((Body $character 'SetHealth') -notmatch $pattern) { throw "SetHealth contract changed: $pattern" }; $checks++
    }
    foreach($pattern in @('Character::GetHealth', 'Character::OnDeath')) {
        if((Body $character 'CheckDeath') -notmatch $pattern) { throw "CheckDeath contract changed: $pattern" }; $checks++
    }
    if(-not ($character.Methods | Where-Object { $_.HasBody -and [string]::Join(' ', $_.Body.Instructions) -match 'call System.Void Character::CheckDeath' })) {
        throw 'No normal Character update calls CheckDeath'
    }; $checks++
    foreach($pattern in @('m_debugFly', 'ZDOVars::s_debugFly', 'ZDO::Set')) {
        if((Body $player 'ToggleDebugFly') -notmatch $pattern) { throw "Flight synchronization contract changed: $pattern" }; $checks++
    }
    # The attacking player is the first IsEnemy argument, so a mob->ghost-only
    # suppression cannot suppress this player's outgoing melee hit.
    if((Body $attack 'DoMeleeAttack') -notmatch 'Attack::m_character.*call System.Boolean BaseAI::IsEnemy') {
        throw 'Melee hostility argument order changed'
    }; $checks++
    $hash = (Get-FileHash -LiteralPath (Join-Path $ManagedDir 'assembly_valheim.dll') -Algorithm SHA256).Hash
    Write-Output "PASS $checks installed-game death/ownership, flight replication and ghost melee API contracts"
    Write-Output "assembly_valheim.dll SHA256 $hash"
}
finally { $game.Dispose() }
