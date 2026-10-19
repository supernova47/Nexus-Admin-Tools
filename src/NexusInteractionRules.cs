using System;

namespace SuperNova.ValheimSpawner
{
    internal static class NexusInteractionRules
    {
        internal static float WheelDelta(float delta) => !AdminRules.Finite(delta) || delta == 0 ? 0 :
            Math.Sign(delta) * Math.Max(1f, Math.Abs(delta));

        internal static bool CleanupTarget(bool player, bool boss, bool tamed, bool includeBosses,
            bool includeTamed, double squaredDistance, float radius) => !player &&
            (!boss || includeBosses) && (!tamed || includeTamed) && AdminRules.Finite(squaredDistance) &&
            squaredDistance >= 0 && AdminRules.Finite(radius) && radius >= 5 && radius <= 100 &&
            squaredDistance <= radius * radius;
    }
}
