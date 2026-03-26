using System;

namespace musicApp.Helpers
{
    public static class CrossfadeParameters
    {
        public static readonly double[] DefaultRampSecondsByOverlap =
        {
            0,
            1.75, 2, 2.25, 2.5, 2.75, 3, 3.25, 3.5, 3.75, 4,
            4.25, 4.5, 4.75, 5, 5.25,
        };

        public static double GetDefaultRampSecondsForOverlap(int overlapSeconds)
        {
            if (overlapSeconds < 1)
                return 0d;
            var i = Math.Clamp(overlapSeconds, 1, DefaultRampSecondsByOverlap.Length - 1);
            return DefaultRampSecondsByOverlap[i];
        }

        public static float LinearOutgoingVolume(float rampT01) => Math.Clamp(1f - rampT01, 0f, 1f);

        public static float LinearIncomingVolume(float rampT01) => Math.Clamp(rampT01, 0f, 1f);

        public static double ClampRampToOverlap(double requestedRampSeconds, double secondsRemainingInOutgoing)
        {
            if (requestedRampSeconds <= 0)
                return 0;
            var rem = Math.Max(1e-6, secondsRemainingInOutgoing);
            return Math.Min(requestedRampSeconds, rem);
        }
    }
}
