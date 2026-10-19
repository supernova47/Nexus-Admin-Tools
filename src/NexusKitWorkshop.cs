using System;
using System.Collections.Generic;

namespace SuperNova.ValheimSpawner
{
    // Portable single-kit format intentionally reuses the validated v1.4.0
    // Format 2 JSON contract. The complete state file is NEVER imported here.
    internal static class NexusKitWorkshop
    {
        internal const int MaxKits = 25;
        internal const int MaxParts = 100;
        internal const int MaxClipboardLength = 128 * 1024;

        internal static NexusKit DeepCopy(NexusKit source)
        {
            if (source == null || source.Items == null) throw new ArgumentException("Kit is unavailable.");
            var copy = new NexusKit { Name = source.Name };
            foreach (NexusKitPart part in source.Items)
            {
                if (part == null) throw new ArgumentException("Kit contains a null entry.");
                copy.Items.Add(new NexusKitPart
                {
                    PrefabId = part.PrefabId,
                    Count = part.Count,
                    Quality = part.Quality,
                    CustomName = part.CustomName,
                    CustomDescription = part.CustomDescription
                });
            }
            return copy;
        }

        internal static bool Validate(NexusKit kit, out string reason)
        {
            reason = string.Empty;
            if (kit == null || kit.Items == null)
            {
                reason = "Kit data is missing.";
                return false;
            }
            if (string.IsNullOrEmpty(kit.Name) || kit.Name != NexusAdvancedData.CleanName(kit.Name))
            {
                reason = "Kit name is invalid (maximum 40 characters).";
                return false;
            }
            if (kit.Items.Count > MaxParts)
            {
                reason = "A kit may contain at most 100 item rows.";
                return false;
            }
            foreach (NexusKitPart part in kit.Items)
            {
                if (part == null || string.IsNullOrWhiteSpace(part.PrefabId) || part.PrefabId.Length > 160 ||
                    part.PrefabId.IndexOfAny(new[] { '\r', '\n', '<', '>' }) >= 0)
                {
                    reason = "Invalid Prefab ID in kit.";
                    return false;
                }
                if (part.Count < 1 || part.Count > 9999 || part.Quality < 1 || part.Quality > 10)
                {
                    reason = "Each row requires count 1–9999 and quality 1–10.";
                    return false;
                }
                if ((part.CustomName ?? string.Empty) != NexusAdvancedData.CleanName(part.CustomName) ||
                    (part.CustomDescription ?? string.Empty) != NexusAdvancedData.CleanDescription(part.CustomDescription))
                {
                    reason = "Item name or description is invalid or too long.";
                    return false;
                }
            }
            return true;
        }

        internal static string UniqueName(string proposal, IList<NexusKit> existing)
        {
            string baseName = NexusAdvancedData.CleanName(proposal);
            if (baseName.Length == 0) baseName = "Kit";
            for (int i = 0; i < 1000; i++)
            {
                string suffix = i == 0 ? string.Empty : " (" + (i + 1) + ")";
                int remaining = Math.Max(1, 40 - suffix.Length);
                string candidate = baseName.Substring(0, Math.Min(baseName.Length, remaining)).TrimEnd() + suffix;
                bool exists = false;
                if (existing != null)
                    foreach (NexusKit old in existing)
                        if (old != null && string.Equals(old.Name, candidate, StringComparison.OrdinalIgnoreCase))
                        { exists = true; break; }
                if (!exists) return candidate;
            }
            throw new InvalidOperationException("Cannot create a unique kit name.");
        }

        internal static string Export(NexusKit kit)
        {
            string reason;
            if (!Validate(kit, out reason)) throw new FormatException(reason);
            var container = new NexusAdvancedData();
            container.Kits.Add(DeepCopy(kit));
            return container.Save();
        }

        internal static bool TryImport(string json, out NexusKit kit, out string reason)
        {
            kit = null;
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxClipboardLength)
            {
                reason = "Clipboard is empty or kit is too large.";
                return false;
            }
            NexusAdvancedData value;
            if (!NexusAdvancedData.TryLoad(json, out value, out reason)) return false;
            if (value.Kits.Count != 1 || value.History.Count != 0 || value.Snapshot.Count != 0 ||
                !string.IsNullOrEmpty(value.SnapshotUtc))
            {
                reason = "Expected one exported kit, without history or Radar data.";
                return false;
            }
            if (!Validate(value.Kits[0], out reason)) return false;
            kit = DeepCopy(value.Kits[0]);
            return true;
        }
    }
}
