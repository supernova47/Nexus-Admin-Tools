param(
    [string]$DllPath = (Join-Path $PSScriptRoot '..\src\bin\Release\ValheimSpawner.dll'),
    [string]$CecilDll = (Join-Path $env:APPDATA 'r2modmanPlus-local\Valheim\profiles\Default\BepInEx\core\Mono.Cecil.dll')
)
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath $CecilDll
$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory((Join-Path $env:APPDATA 'r2modmanPlus-local\Valheim\profiles\Default\BepInEx\plugins\ValheimModding-Jotunn'))
$resolver.AddSearchDirectory([IO.Path]::GetDirectoryName($CecilDll))
$reader = New-Object Mono.Cecil.ReaderParameters
$reader.AssemblyResolver = $resolver
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly([IO.Path]::GetFullPath($DllPath), $reader)
try {
    $plugin = $assembly.MainModule.Types | Where-Object Name -eq 'ValheimSpawnerPlugin'
    $worldSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\NexusWorldAdmin.cs') -Raw
    $checks = 0
    function Code($name) {
        $method = $plugin.Methods | Where-Object Name -eq $name
        if (-not $method) { throw "Missing compiled method: $name" }
        [string]::Join(' ', $method.Body.Instructions)
    }
    function Require($name, $pattern) {
        if ((Code $name) -notmatch $pattern) { throw "Missing compiled guard: $name / $pattern" }
        $script:checks++
    }
    function RequireNested($methodPattern, $pattern) {
        $matchesPattern = $false
        # Captured lambdas can be emitted as private methods on the plugin class,
        # while non-capturing lambdas are emitted on <>c or a display-class.
        foreach ($type in (@($plugin) + @($plugin.NestedTypes))) {
            foreach ($method in ($type.Methods | Where-Object Name -Match $methodPattern)) {
                if ($method.HasBody -and [string]::Join(' ', $method.Body.Instructions) -match $pattern) {
                    $matchesPattern = $true
                    break
                }
            }
            if ($matchesPattern) { break }
        }
        if (-not $matchesPattern) {
            throw "Missing compiled nested guard: $methodPattern / $pattern"
        }
        $script:checks++
    }
    function RequireSource($label, $pattern) {
        if ($worldSource -notmatch $pattern) { throw "Missing world-admin source guard: $label / $pattern" }
        $script:checks++
    }
    $compatibility = $plugin.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'NetworkCompatibilityAttribute' }
    if (-not $compatibility -or [int]$compatibility.ConstructorArguments[0].Value -ne 2 -or [int]$compatibility.ConstructorArguments[1].Value -ne 3) {
        throw 'Matching client/server patch versions must be required'
    }
    $checks++
    Require 'AuthenticatedAdmin' 'ZNet::IsServer'
    Require 'AuthenticatedAdmin' 'ZNet::GetPeers'
    Require 'AuthenticatedAdmin' 'ZNetPeer::IsReady'
    Require 'AuthenticatedAdmin' 'ISocket::IsConnected'
    Require 'AuthenticatedAdmin' 'ISocket::GetHostName'
    Require 'AuthenticatedAdmin' 'ZNet::IsAdmin'
    Require 'ReceiveRequest' 'ZPackage::Size'
    Require 'ReceiveRequest' 'AdminRequestGate::TryAccept'
    Require 'ReceiveRequest' 'ValheimSpawnerPlugin::AuthenticatedAdmin'
    Require 'ReceiveRequest' 'ValheimSpawnerPlugin::ExecuteServerAction'
    Require 'ReceiveReply' 'ZNet::GetServerPeer'
    Require 'ReceiveReply' 'Dictionary.*TryGetValue'
    Require 'ReceiveReply' 'Dictionary.*Remove'
    Require 'ReceiveReply' 'PendingAction::Until'
    Require 'ReceiveReply' 'PendingAction::Kind'
    Require 'ReceiveReply' 'PendingAction::Rpc'
    Require 'AuthorizeLocal' 'ValheimSpawnerPlugin::get_HasAdminAccess'
    foreach ($method in @('AdminDamagePrefix','AdminStaminaPrefix','AdminEitrPrefix','AdminSkillXpPrefix',
        'AdminAdrenalinePrefix','AdminOutgoingDamagePrefix','ApplyAdminVitals','UpdateAdminAdrenaline','IsNexusGhost')) {
        Require $method 'ValheimSpawnerPlugin::get_HasAdminAccess'
    }
    foreach ($method in @('RepairInventory','MaximizeInventoryStacks','RaiseSkills','HealSelf','ApplySelectedKit','AddSelectedItemToInventory')) {
        Require $method 'ValheimSpawnerPlugin::AuthorizeLocal'
    }
    Require 'AddItemBatches' 'ValheimSpawnerPlugin::get_HasAdminAccess'
    Require 'AddItemBatches' '_executingApprovedLocal'
    Require 'TickAdminPreferences' 'ValheimSpawnerPlugin::get_HasAdminAccess'
    Require 'TickAdminPreferences' 'ConfigFile::Bind'
    Require 'AdminScope' 'ZNet::GetWorldUID'
    Require 'AdminScope' 'Player::GetPlayerID'
    Require 'SuspendAdminEffects' 'Player::SetGhostMode'
    Require 'TickNativeHud' 'GUIManager::IsHeadless'
    Require 'GhostEnemyPostfix' 'ValheimSpawnerPlugin::IsNexusGhost'
    Require 'BuildNativeHud' 'GUIManager::CreateWoodpanel'
    Require 'ExecuteServerAction' 'AdminRules::NextTime'
    Require 'ExecuteServerAction' 'ZNet::SetNetTime'
    Require 'ExecuteServerAction' 'AdminRules::Spawn'
    Require 'ExecuteServerAction' 'ValheimSpawnerPlugin::ExecuteCleanup'
    foreach ($handler in @('ExecuteWeatherAction','ExecuteRaidAction','WriteLocationPage',
        'ExecuteLocationTeleport','ExecuteReturnTeleport','WriteAuditPage','ExecuteAddTeleportPoint',
        'ExecuteRenameTeleportPoint','ExecuteDeleteTeleportPoint')) {
        Require 'ExecuteServerAction' "ValheimSpawnerPlugin::$handler"
    }
    Require 'ExecuteWeatherAction' 'm_environments'
    Require 'ApplyServerWeather' 'EnvMan::SetForceEnvironment'
    Require 'ChangeWeather' 'AdminRules::CycleIndex'
    Require 'ExecuteRaidAction' 'ApplyRaidSuppression'
    Require 'ApplyRaidSuppression' 'RandEventSystem::ResetRandomEvent'
    Require 'GetUniqueWorldLocations' 'ZoneSystem::GetLocationList'
    Require 'GetUniqueWorldLocations' 'ValheimSpawnerPlugin/<>c'
    RequireNested '<GetUniqueWorldLocations>b__\d+_3' 'AdminRules::LocationName'
    RequireNested '<RequestLocationPage>b__\d+' 'AdminRules::LocationName'
    RequireNested '<RequestLocationPage>b__\d+' 'ManualLogSource::LogInfo'
    Require 'WriteLocationPage' 'ManualLogSource::LogInfo'
    RequireSource 'map-wide location generation completion' 'LocationsGenerated'
    RequireSource 'world-wide unique locations without loaded-zone filtering' 'return zones\.GetLocationList\(\)\.Where\(location => location\.m_location != null &&[\s\S]*?location\.m_location\.m_unique'
    if ($worldSource -match 'return zones\.GetLocationList\(\)\.Where\([\s\S]*?location\.m_placed') { throw 'Unique world locations must include positions in zones not loaded by a player' }
    $checks++
    Require 'ExecuteLocationTeleport' 'GetAllNexusLocations'
    Require 'ExecuteLocationTeleport' 'AdminRules::WorldPosition'
    Require 'ExecuteLocationTeleport' 'Dictionary.*set_Item'
    RequireNested '<ExecuteLocationTeleport>b__0' 'NexusWorldLocation::Key'
    Require 'ExecuteAddTeleportPoint' 'AdminRules::LocationNameInput'
    Require 'ExecuteAddTeleportPoint' 'AdminRules::WorldPosition'
    Require 'ExecuteAddTeleportPoint' 'Guid::NewGuid'
    Require 'ExecuteRenameTeleportPoint' 'SaveTeleportLocations'
    Require 'ExecuteRenameTeleportPoint' 'GetUniqueWorldLocations'
    Require 'ExecuteDeleteTeleportPoint' 'SaveTeleportLocations'
    Require 'ExecuteDeleteTeleportPoint' 'List.*Remove'
    Require 'SaveTeleportLocations' 'ValheimSpawnerPlugin::TrySavePreferences'
    Require 'ExecuteReturnTeleport' 'Dictionary.*TryGetValue'
    Require 'ExecuteReturnTeleport' 'Dictionary.*Remove'
    RequireSource 'page-bounded location response' 'response\.Write\(page\)[\s\S]*?response\.Write\(entries\.Length\)'
    RequireSource 'world-scoped shared teleport data' 'Config\.Bind\(scope, "TeleportLocations"'
    RequireSource 'add-point coordinates come from authenticated server origin' 'ExecuteAddTeleportPoint\(Vector3 origin, ZPackage request[\s\S]*?WorldPosition\(origin\.x, origin\.y, origin\.z\)'
    Require 'WriteAuditPage' 'Enumerable::Reverse'
    Require 'AppendAdminAudit' 'File::AppendAllText'
    Require 'ReplyToRequest' '_serverForcedWeather'
    Require 'ReplyToRequest' '_serverRaidsDisabled'
    Require 'ExecuteCleanup' 'ZNet::IsServer'
    Require 'ExecuteCleanup' 'ZDO::GetOwner'
    Require 'ExecuteCleanup' 'ZRpc::Invoke'
    Require 'ReceiveOwnerCleanup' 'ZNet::GetServerPeer'
    Require 'ReceiveOwnerCleanup' 'ZNetPeer::m_rpc.*ldarg\.1.*bne\.un'
    Require 'ReceiveOwnerCleanup' 'ZPackage::Size'
    Require 'CanCleanup' 'ZDO::GetPrefab'
    Require 'CanCleanup' 'ZDO::GetPosition'
    Require 'CanCleanup' 'NexusInteractionRules::CleanupTarget'
    Require 'ApplyOwnerCleanup' 'ZNetView::IsOwner'
    Require 'ApplyOwnerCleanup' 'Character::IsPlayer'
    Require 'ApplyOwnerCleanup' 'Character::IsTamed'
    Require 'ApplyOwnerCleanup' 'Character::IsBoss'
    Require 'ApplyOwnerCleanup' 'Character::SetHealth'
    Require 'ApplyFlightState' 'Player::ToggleDebugFly'
    $serverCode = Code 'ExecuteServerAction'
    if ($serverCode -match 'SpawnObject|InvokeRoutedRPC') { throw 'World creation must not broadcast another instantiate' }
    $checks++
    Require 'RaiseSkills' 'Skills::GetSkillLevel'
    Require 'RaiseSkills' 'AdminRules::Skill'
    Require 'AdminOutgoingDamagePrefix' 'DamageTypes::Modify'
    Require 'RestoreOutgoingDamage' 'HitData::m_damage'
    Require 'InstallAdvancedAdminPatches' 'RestoreOutgoingDamage'
    Write-Output "PASS $checks compiled authorization, transport, ownership and native HUD checks"
}
finally { $assembly.Dispose(); $resolver.Dispose() }
