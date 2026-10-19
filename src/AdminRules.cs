using System;
using System.Linq;

namespace SuperNova.ValheimSpawner
{
    // Pure protocol rules shared with the regression tests. Never trust client UI bounds.
    internal static class AdminRules
    {
        internal const int Protocol = 185;
        internal const int MaxPacketBytes = 16384;
        internal const int AllModeBits = 1023;
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal static bool Modes(int bits, float damage, float xp) => bits >= 0 && (bits & ~AllModeBits) == 0 &&
            Finite(damage) && damage >= 1 && damage <= 25 && Finite(xp) && xp >= 1 && xp <= 1000;
        internal static bool Spawn(string prefab, int count, int level, string name) =>
            !string.IsNullOrWhiteSpace(prefab) && prefab.Length <= 128 && count >= 1 && count <= 50 &&
            level >= 1 && level <= 3 && name != null && name.Length <= 48;
        internal static bool Clock(int hour, int minute) => hour >= 0 && hour < 24 && minute >= 0 && minute < 60;
        internal static bool Skill(float level) => Finite(level) && level >= 0 && level <= 100;
        internal static int CycleIndex(int currentIndex, int delta, int count)
        {
            if (count <= 0) return -1;
            int index = currentIndex < 0 || currentIndex >= count ? 0 : currentIndex;
            return (int)(((long)index + delta % count + count) % count);
        }
        internal static string LocationName(string name, string prefab) =>
            string.IsNullOrWhiteSpace(name) ? (prefab ?? string.Empty) : name;
        internal static bool LocationNameInput(string value, bool allowEmpty)
        {
            if (value == null || value.Length > 48) return false;
            string trimmed = value.Trim();
            if (trimmed.Length == 0) return allowEmpty;
            return trimmed.Length <= 48 && !trimmed.Any(char.IsControl);
        }
        internal static bool WorldPosition(float x, float y, float z, float maxCoordinate = 10500f) =>
            Finite(x) && Finite(y) && Finite(z) && Math.Abs(x) <= maxCoordinate && Math.Abs(z) <= maxCoordinate &&
            y >= -1000f && y <= 10000f;
        internal static bool AdminDetail(string value) => value != null && value.Length <= 512;
        // Valheim devotes 70% of elapsed time to daylight (06:00–18:00).
        internal static double SolarFraction(double raw) => raw < .15 ? raw / .15 * .25 :
            raw <= .85 ? .25 + (raw - .15) / .7 * .5 : .75 + (raw - .85) / .15 * .25;
        internal static double RawFraction(int hour, int minute)
        {
            if (!Clock(hour, minute)) throw new ArgumentOutOfRangeException(nameof(hour));
            double solar = (hour * 60 + minute) / 1440.0;
            return solar < .25 ? solar / .25 * .15 : solar <= .75 ? .15 + (solar - .25) / .5 * .7 :
                .85 + (solar - .75) / .25 * .15;
        }
        internal static double NextTime(double now, long dayLength, int hour, int minute)
        {
            if (!Finite(now) || now < 0 || dayLength < 1) throw new ArgumentOutOfRangeException(nameof(now));
            double target = Math.Floor(now / dayLength) * dayLength + RawFraction(hour, minute) * dayLength;
            // Move forward, protecting world timers from a rewind.
            return target < now ? target + dayLength : target;
        }
    }

    internal sealed class AdminRequestGate
    {
        private int _lastId;
        private double _nextPoll, _nextAction;
        internal bool TryAccept(int id, bool poll, double now)
        {
            if (!AdminRules.Finite(now) || now < 0 || id <= _lastId) return false;
            _lastId = id; // Even a throttled packet cannot be replayed later.
            if (poll)
            {
                if (now < _nextPoll) return false;
                _nextPoll = now + .5;
            }
            else
            {
                if (now < _nextAction) return false;
                _nextAction = now + .25;
            }
            return true;
        }
    }
}
