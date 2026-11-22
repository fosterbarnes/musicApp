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
    }
}
