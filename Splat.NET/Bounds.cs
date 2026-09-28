using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Splat.NET
{
    public struct Bounds
    {
        public Vector3 center;
        public Vector3 extents;

        public Bounds(Vector3 center, Vector3 extents)
        {
            this.center = center;
            this.extents = extents;
        }

        public void Encapsulate(Vector3 point)
        {
            var min = center - extents;
            var max = center + extents;

            min = new Vector3(MathF.Min(point.X, min.X), MathF.Min(point.Y, min.Y), MathF.Min(point.Z, min.Z));
            max = new Vector3(MathF.Max(point.X, max.X), MathF.Max(point.Y, max.Y), MathF.Max(point.Z, max.Z));

            center = (min + max) * 0.5f;
            extents = (max - min) * 0.5f;
        }
    }
}
