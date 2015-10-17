﻿using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Assertions;

namespace Assets.Code
{
    public class Chunk
    {
        public const int Size = 16;
        private const int Shift = 4;
        private const int Mask = 0x0F;

        /// <summary>
        /// Blocks size (meters)
        /// </summary>
        public readonly int BlockSize;
        /// <summary>
        /// Side blocks count
        /// </summary>
        public readonly int BlocksCount;
        /// <summary>
        /// Chunk position (chunk units)
        /// </summary>
        public readonly Vector2i Position;
        /// <summary>
        /// Heightmap points side count
        /// </summary>
        public readonly int GridSize;

        public readonly float[,] HeightMap;
        public readonly ZoneRatio[,] Influence;
        public readonly BlockType[,] BlockType;
        public Vector3[] Flora;
        public Vector3[] Stones;

        public Chunk(int blocksCount, int blockSize, Vector2i position)
        {
            BlockSize = blockSize;
            BlocksCount = blocksCount;
            Position = position;
            GridSize = BlocksCount;
            HeightMap = new float[GridSize, GridSize];
            Influence = new ZoneRatio[GridSize, GridSize];
            BlockType = new BlockType[BlocksCount, BlocksCount];

            //Debug
            Test();
        }

        /// <summary>
        /// Calculate world position of center of chunk
        /// </summary>
        /// <param name="chunkPosition">Chunk position</param>
        /// <returns>World position</returns>
        public static Vector2 GetCenter(Vector2i chunkPosition)
        {
            return new Vector2((chunkPosition.X << Shift) + Size / 2, (chunkPosition.Z << Shift) + Size / 2);
        }

        public static Vector2i GetPosition(Vector2 worldPosition)
        {
            return GetPosition((Vector2i)worldPosition);
        }

        public static Vector2i GetPosition(Vector2i worldPosition)
        {
            return new Vector2i(worldPosition.X >> Shift, worldPosition.Z >> Shift);
        }

        /// <summary>
        /// Get 2D world bounds of chunk
        /// </summary>
        /// <param name="position">Chunk position</param>
        /// <returns>World bounds</returns>
        public static Bounds GetBounds(Vector2i position)
        {
            var center = GetCenter(position);
            return new Bounds(new Vector3(center.x, 0, center.y), new Vector3(Size, 0, Size));
        }

        public static Vector2i GetLocalPosition(Vector2 worldPosition)
        {
            return GetLocalPosition((Vector2i) worldPosition);
        }

        public static Vector2i GetLocalPosition(Vector2i worldPosition)
        {
            return new Vector2i(worldPosition.X & Mask, worldPosition.Z & Mask);
        }

        private static void Test()
        {
            //var testCenter = new Vector2(-110.1f, -55.7f);
            ////var testCenter = new Vector2(-1f, -1f);

            //var chunkPos = GetChunkPosition(testCenter);
            //var chunkCenter = GetChunkCenter(chunkPos);

            //var assertDistance = Vector2.Distance(testCenter, chunkCenter);

            //Assert.IsTrue(assertDistance <= Mathf.Sqrt(chunkSize * chunkSize * 2));
        }
    }
}
