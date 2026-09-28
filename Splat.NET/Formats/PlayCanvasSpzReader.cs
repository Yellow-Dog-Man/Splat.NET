// SPZ decoding derives from nianticlabs/spz and the PlayCanvas SPZ parser; MIT license.
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Numerics;

namespace Splat.NET.Formats
{
    public static class PlayCanvasSpzReader
    {
        public static GsplatDecodedData Read(byte[] bytes,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUB,
            ProgressCallback progress = null)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using var stream = new MemoryStream(bytes, writable: false);
            return Read(stream, sourceCoordinates, progress);
        }

        public static GsplatDecodedData Read(Stream stream,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUB,
            ProgressCallback progress = null)
        {
            SpzData source = SpzLoader.Load(stream);
            SpzHeader header = source.Header;
            if (header.ShDegree > 4)
                throw new NotSupportedException($"SPZ SH degree {header.ShDegree} exceeds the supported maximum of 4.");

            int count = checked((int)header.NumPoints);
            int shCoefficients = SpzLoader.ShDim(header.ShDegree);
            var result = new GsplatDecodedData(count, header.ShDegree)
            {
                Antialiased = (header.Flags & 1) != 0
            };

            bool float16Positions = header.Version == 1;
            bool smallestThreeRotation = header.Version >= 3;
            var transform = new CoordinateTransform(source, sourceCoordinates);
            Vector3 boundsMin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 boundsMax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            for (int i = 0; i < count; i++)
            {
                Vector3 rawPosition = float16Positions
                    ? SpzLoader.DecodePositionFloat16(source.Positions, i)
                    : SpzLoader.DecodePosition(source.Positions, i, header.FractionalBits);
                Vector3 position = transform.Position(rawPosition);
                result.Positions[i] = position;
                boundsMin = Vector3.Min(boundsMin, position);
                boundsMax = Vector3.Max(boundsMax, position);

                Vector3 logScale = SpzLoader.DecodeScaleLog(source.Scales, i);
                result.Scales[i] = new Vector3(MathF.Exp(logScale.X), MathF.Exp(logScale.Y), MathF.Exp(logScale.Z));

                Quaternion rotation = SpzLoader.DecodeRotation(source.Rotations, i, smallestThreeRotation);
                rotation = transform.Rotation(rotation);
                result.Rotations[i] = Vector4.Normalize(new Vector4(
                    rotation.W, rotation.X, rotation.Y, rotation.Z));

                Vector3 dc = SpzLoader.DecodeColor(source.Colors, i);
                result.Colors[i] = new Vector4(dc.X, dc.Y, dc.Z,
                    SpzLoader.DecodeAlphaLinear(source.Alphas, i));

                for (int coefficient = 0; coefficient < shCoefficients; coefficient++)
                {
                    int offset = (i * shCoefficients + coefficient) * 3;
                    result.SHs[i * shCoefficients + coefficient] = new Vector3(
                        SpzLoader.UnquantizeSH(source.SH, offset),
                        SpzLoader.UnquantizeSH(source.SH, offset + 1),
                        SpzLoader.UnquantizeSH(source.SH, offset + 2));
                }
                transform.SH(result.SHs, i * shCoefficients, header.ShDegree);

                if ((i & 0x3fff) == 0) progress?.Invoke("Decoding SPZ splats", i / (float)Math.Max(1, count));
            }

            result.Bounds = count > 0
                ? new Bounds((boundsMin + boundsMax) * 0.5f, boundsMax - boundsMin)
                : new Bounds(Vector3.Zero, Vector3.Zero);
            progress?.Invoke("Decoding SPZ splats", 1f);
            return result;
        }

        // Converts the storage coordinate system described by SPZ_ADOBE_coordinate_system
        // (or the caller's fallback when the extension is absent) into Unity RUF. The
        // rotated SPZ family needs a quarter-turn about X in addition to sign flips; SH
        // coefficients use the same analytic matrices as the upstream SPZ implementation.
        internal readonly struct CoordinateTransform
        {
            const float Sqrt1_2 = 0.7071067811865476f;
            const float Sqrt2 = 1.4142135623730951f;
            const float Sqrt3 = 1.7320508075688772f;
            const float Sqrt5 = 2.23606797749979f;
            const float Sqrt7 = 2.6457513105737893f;
            const float Sqrt14 = 3.7416573867739413f;
            const float Sqrt15 = 3.872983346207417f;
            const float Sqrt35 = 5.916079783099616f;
            const float Sqrt3Over8 = 0.6123724356957945f;
            const float Sqrt5Over8 = 0.7905694150420949f;

            readonly SourceCoordinates flipSource;
            readonly bool rotatedFamily;
            readonly float px;
            readonly float py;
            readonly float pz;
            readonly float qx;
            readonly float qy;
            readonly float qz;

            public CoordinateTransform(SpzData data, SourceCoordinates fallback)
            {
                if (!Enum.IsDefined(typeof(SourceCoordinates), fallback))
                    throw new ArgumentOutOfRangeException(nameof(fallback), fallback,
                        "Unknown source coordinate system.");

                SpzCoordinateSystem source = data.StorageCoordinates ??
                    (SpzCoordinateSystem)(uint)fallback;
                if (source == SpzCoordinateSystem.Unspecified)
                    source = SpzCoordinateSystem.RUB;
                if (source < SpzCoordinateSystem.LDB || source > SpzCoordinateSystem.RBU)
                    throw new InvalidDataException($"SPZ coordinate system {source} is not supported.");

                int value = (int)source;
                rotatedFamily = value > (int)SpzCoordinateSystem.RUF;
                if (rotatedFamily) value -= 8;
                flipSource = (SourceCoordinates)value;

                (px, py, pz) = GsplatUtils.AxisSigns(flipSource);
                qx = py * pz;
                qy = px * pz;
                qz = px * py;
            }

            public Vector3 Position(Vector3 value)
            {
                float x = px * value.X;
                float y = py * value.Y;
                float z = pz * value.Z;
                // Rotated family -> standard family: flips first, then R_x(-pi/2).
                return rotatedFamily ? new Vector3(x, z, -y) : new Vector3(x, y, z);
            }

            public Quaternion Rotation(Quaternion value)
            {
                float x = qx * value.X;
                float y = qy * value.Y;
                float z = qz * value.Z;
                float w = value.W;
                if (!rotatedFamily) return new Quaternion(x, y, z, w);

                // Left-multiply by the R_x(-pi/2) quaternion after the inner flip.
                return new Quaternion(
                    Sqrt1_2 * (-w + x),
                    Sqrt1_2 * (y + z),
                    Sqrt1_2 * (-y + z),
                    Sqrt1_2 * (w + x));
            }

            public void SH(Vector3[] values, int offset, int degree)
            {
                int coefficient = 0;
                for (int band = 1; band <= degree; band++)
                {
                    SHBand(values, offset + coefficient, band);
                    int bandSize = 2 * band + 1;
                    coefficient += bandSize;
                }
            }

            public void SHBand(Vector3[] values, int offset, int band)
            {
                int bandSize = 2 * band + 1;
                for (int k = 0; k < bandSize; k++)
                    values[offset + k] *= GsplatUtils.ShSign(flipSource, band, k);

                if (rotatedFamily)
                    RotateMinusPiHalfAboutX(values, offset, band);
            }

            static void RotateMinusPiHalfAboutX(Vector3[] p, int o, int band)
            {
                switch (band)
                {
                    case 1:
                    {
                        Vector3 s0 = p[o], s1 = p[o + 1], s2 = p[o + 2];
                        p[o] = -s1;
                        p[o + 1] = s0;
                        p[o + 2] = s2;
                        break;
                    }
                    case 2:
                    {
                        Vector3 s0 = p[o], s1 = p[o + 1], s2 = p[o + 2];
                        Vector3 s3 = p[o + 3], s4 = p[o + 4];
                        p[o] = -s3;
                        p[o + 1] = -s1;
                        p[o + 2] = -0.5f * s2 - (Sqrt3 * 0.5f) * s4;
                        p[o + 3] = s0;
                        p[o + 4] = -(Sqrt3 * 0.5f) * s2 + 0.5f * s4;
                        break;
                    }
                    case 3:
                    {
                        Vector3 s0 = p[o], s1 = p[o + 1], s2 = p[o + 2];
                        Vector3 s3 = p[o + 3], s4 = p[o + 4], s5 = p[o + 5], s6 = p[o + 6];
                        p[o] = Sqrt5Over8 * s3 - Sqrt3Over8 * s5;
                        p[o + 1] = -s1;
                        p[o + 2] = Sqrt3Over8 * s3 + Sqrt5Over8 * s5;
                        p[o + 3] = -Sqrt5Over8 * s0 - Sqrt3Over8 * s2;
                        p[o + 4] = -0.25f * s4 - (Sqrt15 * 0.25f) * s6;
                        p[o + 5] = Sqrt3Over8 * s0 - Sqrt5Over8 * s2;
                        p[o + 6] = -(Sqrt15 * 0.25f) * s4 + 0.25f * s6;
                        break;
                    }
                    case 4:
                    {
                        Vector3 s0 = p[o], s1 = p[o + 1], s2 = p[o + 2];
                        Vector3 s3 = p[o + 3], s4 = p[o + 4], s5 = p[o + 5];
                        Vector3 s6 = p[o + 6], s7 = p[o + 7], s8 = p[o + 8];
                        p[o] = (Sqrt14 * 0.25f) * s5 - (Sqrt2 * 0.25f) * s7;
                        p[o + 1] = -0.75f * s1 + (Sqrt7 * 0.25f) * s3;
                        p[o + 2] = (Sqrt2 * 0.25f) * s5 + (Sqrt14 * 0.25f) * s7;
                        p[o + 3] = (Sqrt7 * 0.25f) * s1 + 0.75f * s3;
                        p[o + 4] = (3f / 8f) * s4 + (Sqrt5 * 0.25f) * s6 + (Sqrt35 / 8f) * s8;
                        p[o + 5] = -(Sqrt14 * 0.25f) * s0 - (Sqrt2 * 0.25f) * s2;
                        p[o + 6] = (Sqrt5 * 0.25f) * s4 + 0.5f * s6 - (Sqrt7 * 0.25f) * s8;
                        p[o + 7] = (Sqrt2 * 0.25f) * s0 - (Sqrt14 * 0.25f) * s2;
                        p[o + 8] = (Sqrt35 / 8f) * s4 - (Sqrt7 * 0.25f) * s6 + 0.125f * s8;
                        break;
                    }
                }
            }
        }
    }
}
