// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Splat.NET
{
    public enum CompressionMode
    {
        Uncompressed,
        Spark
    }

    /// <summary>
    /// The coordinate frame the source asset was authored in, using the same naming
    /// convention as the Niantic SPZ library: three letters for X (L/R), Y (U/D), Z (F/B).
    /// The importer converts to Unity (RUF) by applying the appropriate sign flips to
    /// positions, rotation quaternions, and spherical-harmonic coefficients at import time.
    /// </summary>
    public enum SourceCoordinates
    {
        /// <summary>
        /// Auto — GLB: LUF; PLY/SOG/SPZ: RUB
        /// </summary>
        Unspecified = 0,

        /// <summary>
        /// LDB — Left-Down-Back
        /// </summary>
        LDB,

        /// <summary>
        /// RDB — Right-Down-Back
        /// </summary>
        RDB,

        /// <summary>
        /// LUB — Left-Up-Back
        /// </summary>
        LUB,

        /// <summary>
        /// RUB — Right-Up-Back  (3DGS, OpenGL, SPZ)
        /// </summary>
        RUB,

        /// <summary>
        /// LDF — Left-Down-Front
        /// </summary>
        LDF,

        /// <summary>
        /// RDF — Right-Down-Front  (OpenCV, COLMAP)
        /// </summary>
        RDF,

        /// <summary>
        /// LUF — Left-Up-Front  (GLB, glTF)
        /// </summary>
        LUF,

        /// <summary>
        /// RUF — Right-Up-Front  (Unity — no conversion)
        /// </summary>
        RUF,
    }

    public class PlyHeaderInfo
    {
        public uint VertexCount = 0;
        public int PropertyCount = 0;
        public int SHPropertyCount = 0;
        public int PositionOffset = -1;
        public int ColorOffset = -1;
        public int SHOffset = -1;
        public int OpacityOffset = -1;
        public int ScaleOffset = -1;
        public int RotationOffset = -1;

        /// <summary>
        /// Read each line, used for header reading.
        /// </summary>
        /// <param name="fs"></param>
        /// <returns></returns>
        static string ReadLine(Stream fs)
        {
            List<byte> byteBuffer = new List<byte>();
            while (true)
            {
                int b = fs.ReadByte();
                if (b == -1 || b == '\n') break;
                byteBuffer.Add((byte)b);
            }

            // If line had CRLF line endings, remove the CR part
            if (byteBuffer.Count > 0 && byteBuffer.Last() == '\r')
            {
                byteBuffer.RemoveAt(byteBuffer.Count - 1);
            }

            return Encoding.UTF8.GetString(byteBuffer.ToArray());
        }

        public PlyHeaderInfo(Stream fs)
        {
            bool inVertexElement = false;
            while (ReadLine(fs) is { } line && line != "end_header")
            {
                var tokens = line.Split(' ');
                if (tokens.Length >= 2 && tokens[0] == "element")
                {
                    inVertexElement = tokens.Length == 3 && tokens[1] == "vertex";
                    if (inVertexElement)
                        VertexCount = uint.Parse(tokens[2]);
                    continue;
                }

                if (!inVertexElement) continue;
                if (tokens.Length != 3 || tokens[0] != "property") continue;
                switch (tokens[2])
                {
                    case "x":
                        PositionOffset = PropertyCount;
                        break;
                    case "f_dc_0":
                        ColorOffset = PropertyCount;
                        break;
                    case "f_rest_0":
                        SHOffset = PropertyCount;
                        break;
                    case "opacity":
                        OpacityOffset = PropertyCount;
                        break;
                    case "scale_0":
                        ScaleOffset = PropertyCount;
                        break;
                    case "rot_0":
                        RotationOffset = PropertyCount;
                        break;
                }

                if (tokens[2].StartsWith("f_rest_"))
                    SHPropertyCount++;
                PropertyCount++;
            }
        }
    }

    public delegate void ProgressCallback(string info, float progress);
}
