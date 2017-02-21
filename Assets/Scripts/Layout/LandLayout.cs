﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using JetBrains.Annotations;
using TerrainDemo.Settings;
using TerrainDemo.Threads;
using TerrainDemo.Tools;
using TerrainDemo.Voronoi;
using UnityEngine;
using UnityEngine.Assertions;
using Debug = UnityEngine.Debug;

namespace TerrainDemo.Layout
{
    /// <summary>
    /// Geometrical representaton of land zones
    /// </summary>
    public class LandLayout
    {
        public Bounds2i Bounds { get; private set; }

        public CellMesh CellMesh { get; private set; }

        /// <summary>
        /// Sorted by Id (same as a Cells in CellMesh)
        /// </summary>
        public IEnumerable<ZoneLayout> Zones { get; private set; }

        public IEnumerable<ClusterLayout> Clusters { get; private set; }

        public IEnumerable<Vector3> Heights { get; private set; }

        public LandLayout(LandSettings settings, CellMesh cellMesh, ClusterInfo[] clusters)
        {
            _settings = settings;
            _globalHeight = new FastNoise(settings.Seed);
            _globalHeight.SetFrequency(settings.GlobalHeightFreq);
            Update(cellMesh, clusters);
        }

        public void Update(CellMesh cellMesh, ClusterInfo[] clusters)
        {
            _globalHeight.SetSeed(_settings.Seed);
            _globalHeight.SetFrequency(_settings.GlobalHeightFreq);

            Bounds = _settings.LandBounds;
            CellMesh = cellMesh;
            _zoneSettings = _settings.Zones.ToArray();
            _zoneMaxType = (int)_settings.Zones.Max(z => z.Type);

            //Build zone influence KD-Tree
            var tags = cellMesh.Cells.Select(c => c.Id).ToArray();
            var positions = new double[cellMesh.Cells.Length, 2];
            for (int i = 0; i < cellMesh.Cells.Length; i++)
            {
                positions[i, 0] = cellMesh[i].Center.x;
                positions[i, 1] = cellMesh[i].Center.y;
            }
            alglib.kdtreebuildtagged(positions, tags, 2, 0, 2, out _influence);

            //Remove close height points
            var removedPointsCount = 0;
            var baseHeights = clusters.SelectMany(c => c.ZoneHeights).ToList();
            for (int i = 0; i < baseHeights.Count; i++)
            {
                for (int j = 0; j < baseHeights.Count; j++)
                {
                    if (baseHeights[i] != baseHeights[j])
                    {
                        if (Vector2.Distance(baseHeights[i].ConvertTo2D(), baseHeights[j].ConvertTo2D()) < 20)
                        {
                            baseHeights[i] = new Vector3(baseHeights[i].x, (baseHeights[i].y + baseHeights[j].y)/2, baseHeights[i].z);
                            baseHeights.RemoveAt(j);
                            removedPointsCount++;
                        }
                    }
                }
            }
            Debug.LogFormat("Land layout removed {0} close height points", removedPointsCount);

            _globalHeights = MakeClusterHeightInterpolator(clusters);
            
            //Build base height interpolator
            var points = new double[baseHeights.Count, 3];
            for (int i = 0; i < baseHeights.Count; i++)
            {
                //Modify zone height with cluster height
                var clusterHeight = alglib.idwcalc(_globalHeights, new double[] { baseHeights[i].x, baseHeights[i].z });
                baseHeights[i] = new Vector3(baseHeights[i].x, (float)(baseHeights[i].y + clusterHeight), baseHeights[i].z);

                points[i, 0] = baseHeights[i].x;
                points[i, 1] = baseHeights[i].z;
                points[i, 2] = baseHeights[i].y;
            }
            
            _baseHeight = new alglib.idwinterpolant();
            alglib.idwbuildmodifiedshepard(points, points.GetLength(0), 2, 2, 10, 15, out _baseHeight);

            Heights = baseHeights;

            //Set Clusters and Zones collections
            var clusterLayouts = new ClusterLayout[clusters.Length];
            var zones = new ZoneLayout[cellMesh.Cells.Length];

            for (var i = 0; i < clusters.Length; i++)
            {
                var clusterInfo = clusters[i];
                var zoneLayouts = new List<ZoneLayout>();

                foreach (var zoneInfo in clusterInfo.Zones)
                {
                    var zoneLayout = new ZoneLayout(zoneInfo, cellMesh.Cells[zoneInfo.Id],
                        _zoneSettings.First(z => z.Type == zoneInfo.Type));
                    zones[zoneInfo.Id] = zoneLayout;
                    zoneLayouts.Add(zoneLayout);
                }

                clusterLayouts[i] = new ClusterLayout(clusterInfo, zoneLayouts, cellMesh);
            }

            Clusters = clusterLayouts;
            Zones = zones;

            foreach (var cluster in clusterLayouts)
                cluster.Init(clusterLayouts);

            for (int i = 0; i < zones.Length; i++)
            {
                var zone = zones[i];
                zone.Init(this);
                zones[i] = zone;
            }
        }

        /// <summary>
        /// Get all chunks of zone
        /// </summary>
        /// <returns></returns>
        public IEnumerable<Vector2i> GetChunks(ZoneLayout zone)
        {
            var centerChunk = Chunk.GetPositionFromWorld(zone.Center);
            var result = new List<Vector2i>();
            var processed = new List<Vector2i>();

            GetChunksFloodFill(zone, centerChunk, processed, result);
            return result;
        }

        public ClusterLayout GetClusterFor([NotNull] ZoneLayout zone)
        {
            if (zone == null) throw new ArgumentNullException("zone");

            return Clusters.First(c => c.Zones.Contains(zone));
        }

        public IEnumerable<ZoneLayout> GetNeighbors(ZoneLayout zone)
        {
            return CellMesh.GetNeighbors(zone.Cell).Select(c => Zones.ElementAt(c.Id));
        }

        public ZoneRatio GetInfluence(Vector2 worldPosition)
        {
            var result = GetInfluenceLocalIDW2(worldPosition);
            return result;
        }

        /// <summary>
        /// Get zones influence for given layout point (function from Shepard IDW)
        /// </summary>
        /// <param name="worldPosition"></param>
        /// <returns></returns>
        [Obsolete("Use more optimized GetInfluenceLocalIDW2()")]
        public ZoneRatio GetInfluenceLocalIDW(Vector2 worldPosition)
        {
            //Spatial optimization
            var center = CellMesh.GetCellFor(worldPosition);
            var nearestCells = new List<Cell>(center.Neighbors.Length + center.Neighbors2.Length + 1);
            nearestCells.Add(center);
            nearestCells.AddRange(center.Neighbors);
            nearestCells.AddRange(center.Neighbors2);
            nearestCells.Sort(new Cell.DistanceComparer(worldPosition));

            Assert.IsTrue(nearestCells.Count >= _settings.IDWNearestPoints);

            var searchRadius = Vector2.Distance(nearestCells[_settings.IDWNearestPoints - 1].Center, worldPosition);
            var influenceLookup = new double[_zoneMaxType + 1];

            //Sum up zones influence
            for (int i = 0; i < _settings.IDWNearestPoints; i++)
            {
                var cell = nearestCells[i];
                var zone = Zones.ElementAt(cell.Id);
                if (zone.Type != ZoneType.Empty)
                {
                    //var zoneWeight = IDWShepardWeighting(zone.Center, worldPosition, searchRadius);
                    var zoneWeight = IDWLocalShepard(zone.Center, worldPosition, searchRadius);
                    //var zoneWeight = IDWLocalLinear(zone.Center, worldPosition, searchRadius);
                    influenceLookup[(int)zone.Type] += zoneWeight;
                }
            }

            var values =
                influenceLookup.Select((v, i) => new ZoneValue((ZoneType)i, v)).Where(v => v.Value > 0).ToArray();
            var result = new ZoneRatio(values, values.Length);

            return result;
        }

        public ZoneRatio GetInfluenceLocalIDW2(Vector2 worldPosition)
        {
            var nearestCellsCount = alglib.kdtreequeryknn(_influence, new double[] {worldPosition.x, worldPosition.y},
                _settings.IDWNearestPoints, true);

            var cellsId = new int[nearestCellsCount];
            alglib.kdtreequeryresultstags(_influence, ref cellsId);

            //Calc search radius
            var searchRadius = Vector2.Distance(CellMesh[cellsId[cellsId.Length - 1]].Center, worldPosition);
            var influenceLookup = new double[_zoneMaxType + 1];

            //Sum up zones influence
            for (int i = 0; i < cellsId.Length; i++)
            {
                var cell = CellMesh[cellsId[i]];
                var zone = Zones.ElementAt(cell.Id);
                if (zone.Type != ZoneType.Empty)
                {
                    //var zoneWeight = IDWShepardWeighting(zone.Center, worldPosition, searchRadius);
                    var zoneWeight = IDWLocalShepard(zone.Center, worldPosition, searchRadius);
                    //var zoneWeight = IDWLocalLinear(zone.Center, worldPosition, searchRadius);
                    influenceLookup[(int)zone.Type] += zoneWeight;
                }
            }

            var values =
                influenceLookup.Select((v, i) => new ZoneValue((ZoneType)i, v)).Where(v => v.Value > 0).ToArray();
            var result = new ZoneRatio(values, values.Length);

            return result;
        }

        [Pure]
        public ZoneLayout GetZoneFor(Vector2 position)
        {
            var cell = CellMesh.GetCellFor(position);
            if (cell != null)
                return Zones.ElementAt(cell.Id);
            return null;
        }

        public double GetBaseHeight(float worldX, float worldZ)
        {
            return alglib.idwcalc(_baseHeight, new double[] { worldX, worldZ });
            //return alglib.idwcalc(_globalHeights, new double[] { worldX, worldZ });
        }

        /// <summary>
        /// Print some internal info
        /// </summary>
        public void PrintDebug()
        {
            var clustersCount = Zones.Select(z => z.ClusterId).Distinct().Count();
            Debug.LogFormat("Zones {0}, clusters {1}", Zones.Count(), clustersCount);
        }

        public void PrintInfluences(Vector2 worldPosition)
        {
            /*
            //Sum up zones influence
            foreach (var zone in Zones)
            {
                if (zone.Type != ZoneType.Empty)
                {
                    var idwWeighting = IDWShepardWeighting(zone.Center, worldPosition);
                    if(idwWeighting > 0)
                        Debug.LogFormat("{0} : value {1}, distance {2}", zone.Cell.Id, idwWeighting, Vector2.Distance(zone.Center, worldPosition));
                }
            }
            */
        }

        private readonly LandSettings _settings;
        private ZoneSettings[] _zoneSettings;
        private int _zoneMaxType;
        private float[,][] _sourceBitmap;
        private float[,][] _targetBitmap;
        private readonly FastNoise _globalHeight;
        private alglib.kdtree _influence;
        private alglib.idwinterpolant _baseHeight;
        private alglib.idwinterpolant _globalHeights;

        private void GetChunksFloodFill(ZoneLayout zone, Vector2i from, List<Vector2i> processed, List<Vector2i> result)
        {
            if (!processed.Contains(@from))
            {
                processed.Add(from);

                if (CheckChunkConservative(from, zone))
                {
                    result.Add(from);
                    GetChunksFloodFill(zone, from + Vector2i.Forward, processed, result);
                    GetChunksFloodFill(zone, from + Vector2i.Back, processed, result);
                    GetChunksFloodFill(zone, from + Vector2i.Left, processed, result);
                    GetChunksFloodFill(zone, from + Vector2i.Right, processed, result);
                }
            }
        }

        /// <summary>
        /// Check chunk corners and zone vertices
        /// </summary>
        /// <param name="chunkPosition"></param>
        /// <param name="zone"></param>
        /// <returns></returns>
        private bool CheckChunkConservative(Vector2i chunkPosition, ZoneLayout zone)
        {
            if (!zone.ChunkBounds.Contains(chunkPosition))
                return false;

            var chunkBounds = Chunk.GetBounds(chunkPosition);
            var floatBounds = (Bounds)chunkBounds;
            var chunkCorner1 = floatBounds.min.ConvertTo2D();
            var chunkCorner2 = new Vector2(floatBounds.min.x, floatBounds.max.z);
            var chunkCorner3 = floatBounds.max.ConvertTo2D();
            var chunkCorner4 = new Vector2(floatBounds.max.x, floatBounds.min.z);

            //Check chunk vertices in zone layout
            if (zone.Cell.IsClosed)
            {
                if (zone.Cell.IsContains(chunkCorner1) || zone.Cell.IsContains(chunkCorner2)
                    || zone.Cell.IsContains(chunkCorner3) || zone.Cell.IsContains(chunkCorner4))
                    return true;
            }
            else  //todo optimize open cell check by providing missed vertices for Cell.IsContains()
            {
                ZoneLayout zone1 = zone, zone2 = zone, zone3 = zone, zone4 = zone;
                var distanceCorner1 = float.MaxValue;
                var distanceCorner2 = float.MaxValue;
                var distanceCorner3 = float.MaxValue;
                var distanceCorner4 = float.MaxValue;
                
                foreach (var z in Zones)                                        
                {
                    if (Vector2.SqrMagnitude(z.Center - chunkCorner1) < distanceCorner1)
                    {
                        zone1 = z;
                        distanceCorner1 = Vector2.SqrMagnitude(z.Center - chunkCorner1);
                    }
                    if (Vector2.SqrMagnitude(z.Center - chunkCorner2) < distanceCorner2)
                    {
                        zone2 = z;
                        distanceCorner2 = Vector2.SqrMagnitude(z.Center - chunkCorner2);
                    }
                    if (Vector2.SqrMagnitude(z.Center - chunkCorner3) < distanceCorner3)
                    {
                        zone3 = z;
                        distanceCorner3 = Vector2.SqrMagnitude(z.Center - chunkCorner3);
                    }
                    if (Vector2.SqrMagnitude(z.Center - chunkCorner4) < distanceCorner4)
                    {
                        zone4 = z;
                        distanceCorner4 = Vector2.SqrMagnitude(z.Center - chunkCorner4);
                    }
                }
    
                if (zone1 == zone || zone2 == zone || zone3 == zone || zone4 == zone)
                    return true;
            }

            //Check zone vertices in chunk
            foreach (var vert in zone.Cell.Vertices)
                if (floatBounds.Contains(vert.ConvertTo3D()))
                    return true;

            return false;
        }

        private double IDWLocalShepard(Vector2 interpolatePoint, Vector2 point, double searchRadius)
        {
            double d = Vector2.Distance(interpolatePoint, point);

            var a = searchRadius - d;
            if (a < 0) a = 0;
            var b = a / (searchRadius * d);
            return b*b;
        }

        private alglib.idwinterpolant MakeClusterHeightInterpolator(ClusterInfo[] clusters)
        {
            var heightPoint = clusters.SelectMany(c => c.ClusterHeights).ToArray();

            //Build base height interpolator
            var points = new double[heightPoint.Length, 3];
            for (int i = 0; i < heightPoint.Length; i++)
            {
                points[i, 0] = heightPoint[i].x;
                points[i, 1] = heightPoint[i].z;
                points[i, 2] = heightPoint[i].y;
            }

            alglib.idwinterpolant result;
            alglib.idwbuildmodifiedshepard(points, points.GetLength(0), 2, 1, 10, 15, out result);
            return result;
        }
    }
}
