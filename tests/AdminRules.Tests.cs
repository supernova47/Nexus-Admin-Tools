using System;
using SuperNova.ValheimSpawner;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new Exception(label); }
    private static void Main()
    {
        Check(!AdminRules.Modes(-1, 5, 1), "Negative flags rejected");
        Check(!AdminRules.Modes(1024, 5, 1), "Unknown flags rejected");
        Check(AdminRules.Modes(1023, 25, 1000), "All supported flags and limits accepted");
        foreach (float number in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0, 1001 })
        {
            Check(!AdminRules.Modes(0, number, 1), "Invalid damage multiplier rejected");
            Check(!AdminRules.Modes(0, 5, number), "Invalid XP multiplier rejected");
        }
        Check(!AdminRules.Spawn("", 1, 1, ""), "Empty prefab rejected");
        Check(!AdminRules.Spawn(new string('a', 129), 1, 1, ""), "Oversized prefab rejected");
        Check(!AdminRules.Spawn("Troll", 0, 1, ""), "Zero spawn rejected");
        Check(!AdminRules.Spawn("Troll", 51, 1, ""), "Spawn limit enforced");
        Check(!AdminRules.Spawn("Troll", 1, 4, ""), "Level limit enforced");
        Check(!AdminRules.Spawn("Troll", 1, 1, new string('x', 49)), "Name limit enforced");
        Check(AdminRules.Spawn("Troll", 50, 3, "Страж"), "Valid Unicode spawn accepted");
        Check(!AdminRules.Clock(24, 0) && !AdminRules.Clock(-1, 0) && !AdminRules.Clock(12, 60), "Invalid clock rejected");
        Check(Math.Abs(AdminRules.RawFraction(6, 0) - .15) < 1e-12, "Sunrise matches game clock");
        Check(Math.Abs(AdminRules.RawFraction(18, 0) - .85) < 1e-12, "Sunset matches game clock");
        for (int minute = 0; minute < 1440; minute++)
        {
            var raw = AdminRules.RawFraction(minute / 60, minute % 60);
            Check(Math.Abs(AdminRules.SolarFraction(raw) * 1440 - minute) < 1e-8, "Every minute round trips");
            var next = AdminRules.NextTime(32123.75, 1800, minute / 60, minute % 60);
            Check(next >= 32123.75 && next < 32123.75 + 1800, "World time never rewinds or skips more than a day");
        }
        Check(AdminRules.NextTime(900, 1800, 12, 0) == 900, "Setting current time does not add a day");
        Check(AdminRules.NextTime(901, 1800, 12, 0) == 2700, "Past time moves to next day");
        foreach (float value in new[] { -1, 101, float.NaN, float.PositiveInfinity })
            Check(!AdminRules.Skill(value), "Invalid skill level rejected");
        Check(AdminRules.Skill(0) && AdminRules.Skill(100), "Skill limits accepted");
        Check(AdminRules.WorldPosition(10499, 400, -10499), "Valid generated-world position accepted");
        Check(!AdminRules.WorldPosition(float.NaN, 10, 10), "Non-finite teleport position rejected");
        Check(!AdminRules.WorldPosition(10501, 10, 10), "Out-of-world teleport position rejected");
        Check(AdminRules.CycleIndex(0, 1, 5) == 1, "Weather next advances from current selection");
        Check(AdminRules.CycleIndex(1, 1, 5) == 2 && AdminRules.CycleIndex(2, 1, 5) == 3,
            "Repeated weather next visits successive choices");
        Check(AdminRules.CycleIndex(0, -1, 5) == 4 && AdminRules.CycleIndex(4, 1, 5) == 0,
            "Weather selection wraps in both directions");
        Check(AdminRules.CycleIndex(0, 1, 0) == -1, "Empty weather choices are safe");
        Check(AdminRules.LocationName("", "Bonemass") == "Bonemass" &&
            AdminRules.LocationName("$location_bonemass", "Bonemass") == "$location_bonemass",
            "Missing location display name falls back to its prefab");
        Check(AdminRules.Protocol == 185, "Admin player commands use the current client/server protocol");
        Check(AdminRules.LocationNameInput("  Home  ", false) && AdminRules.LocationNameInput("", true) &&
            !AdminRules.LocationNameInput("", false) && !AdminRules.LocationNameInput(new string('x', 49), false) &&
            !AdminRules.LocationNameInput("Bad\nName", false), "Teleport labels enforce length and safe text rules");
        Check(AdminRules.AdminDetail("set Swords to 1") && !AdminRules.AdminDetail(new string('x', 513)), "Admin log detail bounded");
        var gate = new AdminRequestGate();
        Check(!gate.TryAccept(0, false, 0), "Zero request ID rejected");
        Check(gate.TryAccept(1, true, 0), "First poll accepted");
        Check(!gate.TryAccept(1, true, 10), "Duplicate poll rejected later");
        Check(!gate.TryAccept(2, true, .1), "Poll flood throttled");
        Check(!gate.TryAccept(2, true, 1), "Throttled request cannot be replayed");
        Check(gate.TryAccept(3, false, .1), "Action has independent rate limit");
        Check(!gate.TryAccept(4, false, .2), "Action flood throttled");
        Check(!gate.TryAccept(4, false, 10), "Throttled action never replays");
        Check(!gate.TryAccept(3, false, 20), "Out of order request rejected");
        Check(gate.TryAccept(5, false, .35), "Next action after cooldown accepted");
        Check(gate.TryAccept(6, true, .5), "Next poll after cooldown accepted");
        Check(!gate.TryAccept(7, false, double.NaN), "Nonfinite timestamp rejected");
        Check(new AdminRequestGate().TryAccept(1, false, 0), "New connection has independent replay state");
        System.Console.WriteLine("PASS " + _checks + " protocol, rate limit, replay and world clock checks");
    }
}
