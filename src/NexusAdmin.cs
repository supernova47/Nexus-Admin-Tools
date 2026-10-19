using System;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SuperNova.ValheimSpawner
{
    public sealed partial class ValheimSpawnerPlugin
    {
        private bool _adminGhost;
        private bool _infiniteGuardianPower;
        private Player _preferencesPlayer, _effectsPlayer;
        private string _preferencesScope;
        private ConfigEntry<int> _savedModes;
        private ConfigEntry<float> _savedDamage, _savedXp;
        private ConfigEntry<string> _savedSkill;
        private ConfigEntry<int> _savedSkillLevel, _savedHour, _savedMinute;
        private ConfigEntry<bool> _savedCleanupTamed, _savedCleanupBosses;
        private ConfigEntry<int> _savedAdminSection;
        private int _timeHour = 12, _timeMinute, _skillTarget = 100;
        private Skills.SkillType _skillChoice = Skills.SkillType.Swords;
        private float _nextEffectsApply;
        private bool _effectsActive, _priorFly, _priorCreative, _priorGhost;
        private int _lastLocalAuditedModes = -1;
        private float _lastLocalAuditedDamage = float.NaN, _lastLocalAuditedXp = float.NaN;
        private bool AdminContextMatches => Player.m_localPlayer != null && ZNet.instance != null &&
            ReferenceEquals(Player.m_localPlayer, _preferencesPlayer) && _preferencesScope == AdminScope();
        private string AdminScope() => "Admin." + ZNet.instance.GetWorldUID().ToString("X16") + "." + Player.m_localPlayer.GetPlayerID().ToString("X16");

        private void ResetAdminContext()
        {
            _preferencesPlayer = null; _preferencesScope = null;
            _savedModes = null;
            _infiniteHealth = _infiniteStamina = _infiniteEitr = _infiniteAdrenaline = _superDamage = false;
            _adminFly = _adminCreative = _adminGhost = _adminHotkeys = false;
            _infiniteGuardianPower = false;
            _superDamageMultiplier = 5; _skillXpMultiplier = 1;
            _lastLocalAuditedModes = -1;
        }

        private int DesiredModeBits => (_infiniteHealth ? 1 : 0) | (_infiniteStamina ? 2 : 0) |
            (_infiniteEitr ? 4 : 0) | (_infiniteAdrenaline ? 8 : 0) | (_superDamage ? 16 : 0) |
            (_adminFly ? 32 : 0) | (_adminCreative ? 64 : 0) | (_adminGhost ? 128 : 0) | (_adminHotkeys ? 256 : 0) |
            (_infiniteGuardianPower ? 512 : 0);

        private void TickAdminPreferences()
        {
            var player = Player.m_localPlayer;
            if (!HasAdminAccess || player == null || player.IsDead()) { SuspendAdminEffects(); return; }
            string scope = AdminScope();
            if (scope != _preferencesScope || !ReferenceEquals(player, _preferencesPlayer))
            {
                SuspendAdminEffects();
                _preferencesPlayer = player;
                _preferencesScope = scope;
                _savedModes = Config.Bind(scope, "Modes", 0, "Remembered admin choices for this world and character. Permissions are rechecked on every connection.");
                _savedDamage = Config.Bind(scope, "DamageMultiplier", 5f);
                _savedXp = Config.Bind(scope, "SkillXpMultiplier", 1f);
                _savedSkill = Config.Bind(scope, "SelectedSkill", "Swords");
                _savedSkillLevel = Config.Bind(scope, "SkillTarget", 100);
                _savedHour = Config.Bind(scope, "TimeHour", 12);
                _savedMinute = Config.Bind(scope, "TimeMinute", 0);
                _savedCleanupTamed = Config.Bind(scope, "CleanupTamed", false);
                _savedCleanupBosses = Config.Bind(scope, "CleanupBosses", false);
                _savedAdminSection = Config.Bind(scope, "Section", 0);
                _killIncludeTamed = _savedCleanupTamed.Value; _killIncludeBosses = _savedCleanupBosses.Value;
                _adminSection = Mathf.Clamp(_savedAdminSection.Value, 0, 5);
                int bits = _savedModes.Value & AdminRules.AllModeBits;
                _infiniteHealth = (bits & 1) != 0; _infiniteStamina = (bits & 2) != 0; _infiniteEitr = (bits & 4) != 0;
                _infiniteAdrenaline = (bits & 8) != 0; _superDamage = (bits & 16) != 0;
                _adminFly = (bits & 32) != 0; _adminCreative = (bits & 64) != 0;
                _adminGhost = (bits & 128) != 0; _adminHotkeys = (bits & 256) != 0;
                _infiniteGuardianPower = (bits & 512) != 0;
                _superDamageMultiplier = AdminRules.Finite(_savedDamage.Value) ? Mathf.Clamp(_savedDamage.Value, 1, 25) : 5;
                _skillXpMultiplier = AdminRules.Finite(_savedXp.Value) ? Mathf.Clamp(_savedXp.Value, 1, 1000) : 1;
                _skillXpInput = _skillXpMultiplier.ToString(CultureInfo.InvariantCulture);
                _skillTarget = Mathf.Clamp(_savedSkillLevel.Value, 0, 100);
                _timeHour = Mathf.Clamp(_savedHour.Value, 0, 23); _timeMinute = Mathf.Clamp(_savedMinute.Value, 0, 59);
                if (Enum.TryParse(_savedSkill.Value, out Skills.SkillType skill) && SkillChoices().Contains(skill)) _skillChoice = skill;
                _lastLocalAuditedModes = DesiredModeBits;
                _lastLocalAuditedDamage = _superDamageMultiplier; _lastLocalAuditedXp = _skillXpMultiplier;
                _hudDirty = true;
            }
            if (!_effectsActive || !ReferenceEquals(_effectsPlayer, player))
            {
                _effectsPlayer = player;
                _priorFly = ReadMember(player, "m_debugFly") is bool fly && fly;
                _priorCreative = ReadMember(player, "m_noPlacementCost") is bool creative && creative;
                _priorGhost = ReadMember(player, "m_ghostMode") is bool ghost && ghost;
                _effectsActive = true;
                _nextEffectsApply = 0;
            }
            if (Time.unscaledTime >= _nextEffectsApply)
            {
                _nextEffectsApply = Time.unscaledTime + .25f;
                // Restore vanilla state when a Nexus option is switched off.
                ApplyFlightState(player, _adminFly || _priorFly);
                TrySetBoolMember(player, "m_noPlacementCost", _adminCreative || _priorCreative);
                player.SetGhostMode(_adminGhost || _priorGhost);
                // Cooldown is reset continuously while enabled, so a player can use
                // a Forsaken power, switch to another, and use that one immediately.
                if (_infiniteGuardianPower) TrySetFloatMember(player, "m_guardianPowerCooldown", 0f);
            }
        }

        private void SuspendAdminEffects()
        {
            if (_effectsActive && _effectsPlayer != null)
            {
                ApplyFlightState(_effectsPlayer, _priorFly);
                TrySetBoolMember(_effectsPlayer, "m_noPlacementCost", _priorCreative);
                _effectsPlayer.SetGhostMode(_priorGhost);
            }
            _effectsActive = false;
            _effectsPlayer = null;
        }

        private void SaveAdminPreferences()
        {
            if (!HasAdminAccess || _savedModes == null || !ReferenceEquals(Player.m_localPlayer, _preferencesPlayer)) return;
            _savedModes.Value = DesiredModeBits;
            _savedDamage.Value = _superDamageMultiplier;
            _savedXp.Value = _skillXpMultiplier;
            _savedSkill.Value = _skillChoice.ToString(); _savedSkillLevel.Value = _skillTarget;
            _savedHour.Value = _timeHour; _savedMinute.Value = _timeMinute;
            _savedCleanupTamed.Value = _killIncludeTamed; _savedCleanupBosses.Value = _killIncludeBosses;
            _savedAdminSection.Value = _adminSection;
            if (ZNet.instance.IsServer()) AuditLocalModeChanges();
            _nextEffectsApply = 0;
            TrySavePreferences();
        }

        private void AuditLocalModeChanges()
        {
            int current = DesiredModeBits;
            if (_lastLocalAuditedModes >= 0)
            {
                for (int bit = 0; bit < 10; bit++)
                {
                    int mask = 1 << bit;
                    if (((current ^ _lastLocalAuditedModes) & mask) != 0)
                        AppendAdminAudit(Player.m_localPlayer.GetPlayerName(), Player.m_localPlayer.GetPlayerID(),
                            AdminModeName(bit) + ((current & mask) != 0 ? " enabled" : " disabled"), "OK");
                }
                if (Math.Abs(_superDamageMultiplier - _lastLocalAuditedDamage) > .001f)
                    AppendAdminAudit(Player.m_localPlayer.GetPlayerName(), Player.m_localPlayer.GetPlayerID(),
                        "damage multiplier ×" + _superDamageMultiplier.ToString("0.##", CultureInfo.InvariantCulture), "OK");
                if (Math.Abs(_skillXpMultiplier - _lastLocalAuditedXp) > .001f)
                    AppendAdminAudit(Player.m_localPlayer.GetPlayerName(), Player.m_localPlayer.GetPlayerID(),
                        "skill XP multiplier ×" + _skillXpMultiplier.ToString("0.##", CultureInfo.InvariantCulture), "OK");
            }
            _lastLocalAuditedModes = current;
            _lastLocalAuditedDamage = _superDamageMultiplier; _lastLocalAuditedXp = _skillXpMultiplier;
        }

        private static void ApplyFlightState(Player player, bool enabled)
        {
            var view = player == null ? null : player.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner()) return;
            if (player.InDebugFlyMode() != enabled) player.ToggleDebugFly();
        }

        private string WorldClock()
        {
            if (ZNet.instance == null || EnvMan.instance == null || EnvMan.instance.m_dayLengthSec < 1) return "—";
            double seconds = ZNet.instance.GetTimeSeconds(), length = EnvMan.instance.m_dayLengthSec;
            double raw = (seconds % length) / length;
            int minutes = Math.Min(1439, (int)(AdminRules.SolarFraction(raw) * 1440));
            return T("Day ", "День ") + EnvMan.instance.GetDay() + "  ·  " + (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
        }

        private Skills.SkillType[] SkillChoices() => Player.m_localPlayer?.GetSkills()?.m_skills?
            .Select(s => s.m_skill).Where(s => s != Skills.SkillType.None && s != Skills.SkillType.All).Distinct().ToArray()
            ?? Array.Empty<Skills.SkillType>();

        private string SkillName(Skills.SkillType skill)
        {
            // Use the same localized skill tokens as the vanilla skills window.
            string token = "$skill_" + skill.ToString().ToLowerInvariant();
            string translated = Localization.instance?.Localize(token);
            return string.IsNullOrEmpty(translated) || translated == token ? skill.ToString() : translated;
        }

        private void RaiseSkills(bool all, Skills.SkillType choice, int target)
        {
            if (!AdminRules.Skill(target) || !AuthorizeLocal(() => RaiseSkills(all, choice, target),
                (all ? "set all skills to " : "set " + choice + " to ") + target.ToString(CultureInfo.InvariantCulture))) return;
            var skills = Player.m_localPlayer?.GetSkills();
            if (skills == null) return;
            var choices = SkillChoices();
            if (!all && !choices.Contains(choice)) { _status = T("Skill unavailable.", "Навык недоступен."); return; }
            var get = AccessTools.Method(typeof(Skills), "GetSkill", new[] { typeof(Skills.SkillType) });
            int changed = 0;
            foreach (var type in all ? choices : new[] { choice })
            {
                if (Mathf.Abs(skills.GetSkillLevel(type) - target) < .01f) continue;
                var skill = get.Invoke(skills, new object[] { type });
                if (TrySetFloatMember(skill, "m_level", target))
                {
                    TrySetFloatMember(skill, "m_accumulator", 0); changed++;
                }
            }
            ShowAdminToast(T("Skill levels set: ", "Установлено навыков: ") + changed + " → " + target);
        }

        private void HealSelf()
        {
            if (!AuthorizeLocal(HealSelf, "restore player health")) return;
            var player = Player.m_localPlayer;
            if (player == null) return;
            player.Heal(player.GetMaxHealth(), true);
            _status = T("Health restored.", "Здоровье восстановлено.");
        }
    }
}
