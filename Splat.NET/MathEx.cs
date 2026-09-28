using System;
using System.Collections.Generic;
using System.Text;

namespace Splat.NET
{
    internal static class MathEx
    {
        public static float Clamp01(float value)
        {
            if (value <= 0f)
                return 0f;

            if (value >= 1f)
                return 1f;

            return value;
        }

        public static int FloorToInt(float value) => (int)MathF.Floor(value);

        public static float Lerp(float from, float to, float lerp)
        {
            if (lerp <= 0)
                return from;

            if (lerp >= 1f)
                return to;

            return from + (to - from) * lerp;
        }
    }
}
