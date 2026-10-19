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
$guiUtils = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $ManagedDir 'assembly_guiutils.dll'))
try {
    $character = $game.MainModule.Types | Where-Object Name -eq 'Character'
    $player = $game.MainModule.Types | Where-Object Name -eq 'Player'
    $attack = $game.MainModule.Types | Where-Object Name -eq 'Attack'
    $inventory = $game.MainModule.Types | Where-Object Name -eq 'Inventory'
    $inventoryGui = $game.MainModule.Types | Where-Object Name -eq 'InventoryGui'
    $itemDrop = $game.MainModule.Types | Where-Object Name -eq 'ItemDrop'
    $itemData = $itemDrop.NestedTypes | Where-Object Name -eq 'ItemData'
    $uiDrag = $guiUtils.MainModule.Types | Where-Object Name -eq 'UIDragHandler'
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

    function RequireField($type, $name) {
        if (-not ($type.Fields | Where-Object Name -eq $name)) { throw "Missing game field: $($type.Name).$name" }
        $script:checks++
    }
    function RequireMethod($type, $name, $parameterTypes) {
        $method = $type.Methods | Where-Object {
            $_.Name -eq $name -and $_.Parameters.Count -eq $parameterTypes.Count -and
            (@($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join '|') -eq ($parameterTypes -join '|')
        } | Select-Object -First 1
        if (-not $method) { throw "Missing game method: $($type.Name).$name($($parameterTypes -join ', '))" }
        $script:checks++
    }
    $itemTypeName = $itemData.FullName
    RequireMethod $inventory 'GetAllItems' @()
    RequireMethod $inventory 'GetWidth' @()
    RequireMethod $inventory 'GetHeight' @()
    RequireMethod $inventory 'GetItemAt' @('System.Int32','System.Int32')
    RequireMethod $inventory 'RemoveItem' @($itemTypeName)
    RequireMethod $inventory 'CanAddItem' @($itemTypeName,'System.Int32')
    RequireMethod $inventory 'AddItem' @($itemTypeName,'System.Int32','System.Int32','System.Int32','System.Boolean')
    RequireMethod $itemData 'Save' @('ZPackage')
    RequireMethod $itemData 'Load' @('ZPackage',$itemTypeName,'Version/Item')
    RequireMethod $game.MainModule.GetType('ZPackage') '.ctor' @('System.Byte[]')
    RequireMethod $game.MainModule.GetType('ZPackage') 'GetArray' @()
    RequireMethod $game.MainModule.GetType('ObjectDB') 'GetItemPrefab' @('System.String')
    RequireField $game.MainModule.GetType('Version') 'c_ItemDataVersion'
    RequireMethod $inventoryGui 'OnDropOutside' @()
    RequireMethod $inventoryGui 'SetupDragItem' @($itemTypeName,'Inventory','System.Int32')
    RequireField $inventoryGui 'm_dragItem'
    RequireField $inventoryGui 'm_dragInventory'
    RequireField $inventoryGui 'm_dragAmount'
    RequireField $inventoryGui 'm_dragGo'
    RequireField $itemData 'm_gridPos'
    RequireField $itemData 'm_stack'
    RequireField $itemData 'm_customData'
    RequireMethod $character 'SetMoveDir' @('UnityEngine.Vector3')
    RequireMethod $character 'UpdateWalking' @('System.Single')
    RequireMethod $character 'Jump' @('System.Boolean')
    RequireMethod $player 'Dodge' @('UnityEngine.Vector3')
    RequireMethod $player 'GetMaxCarryWeight' @()
    RequireMethod $inventoryGui 'IsVisible' @()
    RequireMethod $inventoryGui 'Show' @('Container','System.Int32')
    RequireMethod $inventoryGui 'Hide' @()
    RequireField $player 'm_maxCarryWeight'
    $zdoManager = $game.MainModule.GetType('ZDOMan')
    $zdo = $game.MainModule.GetType('ZDO')
    RequireMethod $zdo 'SetOwner' @('System.Int64')
    RequireMethod $zdoManager 'GetSessionID' @()
    RequireMethod $zdoManager 'DestroyZDO' @('ZDO')
    RequireField $zdoManager 'm_objectsByID'
    $netScene = $game.MainModule.GetType('ZNetScene')
    RequireMethod $netScene 'GetPrefabHash' @('UnityEngine.GameObject')
    RequireField $netScene 'm_prefabs'
    RequireMethod $uiDrag 'OnReleasedOn' @()
    RequireField $uiDrag 'm_onReleasedOn'
    $hash = (Get-FileHash -LiteralPath (Join-Path $ManagedDir 'assembly_valheim.dll') -Algorithm SHA256).Hash
    Write-Output "PASS $checks installed-game death/ownership, flight replication, ghost melee and inventory trash API contracts"
    Write-Output "assembly_valheim.dll SHA256 $hash"
}
finally { $game.Dispose(); $guiUtils.Dispose() }
