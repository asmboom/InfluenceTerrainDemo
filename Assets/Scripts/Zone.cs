﻿using System.Collections.Generic;
using TerrainDemo.Layout;
using TerrainDemo.Voronoi;
using UnityEngine;

namespace TerrainDemo
{
    public class Zone
    {
        public readonly ZoneType Type;
        public readonly Vector2 Center;
        public readonly Bounds2i Bounds;
        public readonly ZoneLayout Layout;
        public IEnumerable<Zone> Neighbours { get; private set; }

        public static readonly IEqualityComparer<Zone> TypeComparer = new ZoneTypeComparer();

        public Zone(ZoneLayout layout)
        {
            Type = layout.Type;
            Center = layout.Center;
            Bounds = layout.Bounds;
            Layout = layout;
            _cell = layout.Cell;
        }

        public void Init(Dictionary<Cell, Zone> allZones)
        {
            var neighbours = new Zone[_cell.Neighbors.Length];
            for (var i = 0; i < neighbours.Length; i++)
                neighbours[i] = allZones[_cell.Neighbors[i]];
            Neighbours = neighbours;
        }

        public override string ToString()
        {
            return string.Format("[{0}, {1}]", Center, Type);
        }

        private readonly Cell _cell;

        private class ZoneTypeComparer : IEqualityComparer<Zone>
        {
            public bool Equals(Zone x, Zone y)
            {
                if (x != null && y != null)
                    return x.Type == y.Type;

                if (x == null && y == null)
                    return true;

                return false;
            }

            public int GetHashCode(Zone obj)
            {
                return (int)obj.Type;
            }
        }
    }
}
