﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEngine;

namespace TerrainDemo
{
    public struct Bounds2i : IEnumerable<Vector2i>, IEquatable<Bounds2i>
    {
        public readonly Vector2i Min;
        public readonly Vector2i Max;

        public Vector2i Size { get { return Max - Min + Vector2i.One; } }
        public Vector2i Corner1 { get { return Min; } }
        public Vector2i Corner2 { get { return new Vector2i(Min.X, Max.Z); } }
        public Vector2i Corner3 { get { return Max; } }
        public Vector2i Corner4 { get { return new Vector2i(Max.X, Min.Z); } }

        public static readonly Bounds2i Empty = new Bounds2i(Vector2i.Zero, Vector2i.Zero);
        public static readonly Bounds2i Infinite = new Bounds2i(new Vector2i(-10000, -10000), new Vector2i(10000, 10000));
        private const float RoundBiasValue = 0.5f;
        //private static readonly Vector3 RoundBias = new Vector3(RoundBiasValue, RoundBiasValue);

        public Bounds2i(Vector2i min, Vector2i max)
        {
            if(min.X > max.X || min.Z > max.Z) throw new ArgumentException(String.Format("Min point {0} greater then Max {1} ", min, max));

            Min = min;
            Max = max;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="center"></param>
        /// <param name="extends"></param>
        public Bounds2i(Vector2i center, int extends) : this(center - Vector2i.One * extends, center + Vector2i.One * extends)
        {
        }

        public Bounds2i(Vector2i min, int xSize, int zSize) : this(min, new Vector2i(min.X + xSize - 1, min.Z + zSize - 1))
        {
        }
    
        [Pure]
        public bool Contains(Vector2i pos)
        {
            return pos.X >= Min.X && pos.X <= Max.X &&
                   pos.Z >= Min.Z && pos.Z <= Max.Z;
        }

        public IEnumerable<Vector2i> Substract(Bounds2i b)
        {
            return this.Where(v => !b.Contains(v));
        }

        public Bounds2i Intersect(Bounds2i b)
        {
            int x = Math.Max(Min.X, b.Min.X);
            int num2 = Math.Min(Min.X + Size.X, b.Min.X + b.Size.X);
            int z = Math.Max(Min.Z, b.Min.Z);
            int num4 = Math.Min(Min.Z + Size.Z, b.Min.Z + b.Size.Z);

            if ((num2 > x) && (num4 > z))
                return new Bounds2i(new Vector2i(x, z), num2 - x, num4 - z);

            return Empty;
        }

        /// <summary>
        /// Slide bounds by given offset
        /// </summary>
        /// <param name="offset"></param>
        /// <returns></returns>
        public Bounds2i Translate(Vector2i offset)
        {
            return new Bounds2i(Min + offset, Max + offset);
        }

        /// <summary>
        /// Inclusive enumeration of bound
        /// </summary>
        /// <returns></returns>
        public IEnumerator<Vector2i> GetEnumerator()
        {
            for (var x = Min.X; x <= Max.X; x++)
                for (var z = Min.Z; z <= Max.Z; z++)
                    yield return new Vector2i(x, z);
        }

        public override string ToString()
        {
            return string.Format("Bounds2i(min = {0}, max = {1})", Min, Max);
        }

        public override int GetHashCode()
        {
            return Min.GetHashCode() ^ (Max.GetHashCode() << 16);
        }

        public bool Equals(Bounds2i other)
        {
            return Min == other.Min && Max == other.Max;
        }

        public override bool Equals(object other)
        {
            if (!(other is Bounds2i)) return false;
            var b = (Bounds2i)other;
            return Min == b.Min && Max == b.Max;
        }

        public static bool operator ==(Bounds2i a, Bounds2i b)
        {
            return a.Min == b.Min && a.Max == b.Max;
        }

        public static bool operator !=(Bounds2i a, Bounds2i b)
        {
            return a.Min != b.Min || a.Max != b.Max;
        }

        public static Bounds2i operator *(Bounds2i a, int b)
        {
            return new Bounds2i(a.Min * b, a.Max*b);
        }

        public static explicit operator Bounds(Bounds2i input)
        {
            var size = new Vector3(input.Max.X - input.Min.X + 1, 0, input.Max.Z - input.Min.Z + 1);
            var center = new Vector3(input.Min.X + size.x / 2, 0, input.Min.Z + size.z / 2);
            return new Bounds(center, size);
        }

        public static explicit operator Bounds2i(Bounds input)
        {
            var xMin = input.min.x % 1 == 0 ? input.min.x + RoundBiasValue : input.min.x;
            var yMin = input.min.z % 1 == 0 ? input.min.z + RoundBiasValue : input.min.z;
            var xMax = input.max.x % 1 == 0 ? input.max.x - RoundBiasValue : input.max.x;
            var yMax = input.max.z % 1 == 0 ? input.max.z - RoundBiasValue : input.max.z;

            return new Bounds2i((Vector2i)(new Vector2(xMin, yMin)), (Vector2i)(new Vector2(xMax, yMax)));         //To bias float bounds values to integer
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        //public static void Tests()
        //{
        //    var testIntersect = new Bounds2i(new Vector2i(0, 0), new Vector2i(3, 2)).Intersect(new Bounds2i(new Vector2i(4, 0), 1));
        //    if (testIntersect != new Bounds2i(new Vector2i(3, 0), new Vector2i(3, 1))) throw new AssertFailedException("Bounds2i test");

        //    testIntersect = new Bounds2i(new Vector2i(0, 0), new Vector2i(3, 2)).Intersect(new Bounds2i(new Vector2i(4, 2), 1));
        //    if (testIntersect != new Bounds2i(new Vector2i(3, 1), new Vector2i(3, 2))) throw new AssertFailedException("Bounds2i test");

        //    testIntersect = new Bounds2i(new Vector2i(0, 0), new Vector2i(3, 2)).Intersect(new Bounds2i(new Vector2i(1, 1), new Vector2i(2, 1)));
        //    if (testIntersect != new Bounds2i(new Vector2i(1, 1), new Vector2i(2, 1))) throw new AssertFailedException("Bounds2i test");

        //    testIntersect = new Bounds2i(new Vector2i(0, 0), new Vector2i(3, 2)).Intersect(new Bounds2i(new Vector2i(4, 0), new Vector2i(4, 1)));
        //    if (testIntersect != Empty) throw new AssertFailedException("Bounds2i test");

        //}

    }
}
