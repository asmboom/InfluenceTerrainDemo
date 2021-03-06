﻿using JetBrains.Annotations;
using TerrainDemo.Layout;
using TerrainDemo.Settings;
using UnityEngine;

namespace TerrainDemo.Generators.Debug
{
    public class ConeGenerator : ZoneGenerator
    {
        public ConeGenerator(ZoneLayout zone, [NotNull] LandLayout land, [NotNull] ILandSettings landSettings) : base(zone, land, landSettings)
        {
        }

        protected override float GenerateBaseHeight(float worldX, float worldZ, IZoneNoiseSettings settings)
        {
            var distanceFromCenter = Vector2.Distance(Vector2.zero, new Vector2(worldX, worldZ));
            distanceFromCenter = Mathf.Max(distanceFromCenter, 0.1f);
            var height = Mathf.Clamp((100/distanceFromCenter), 0, 50);
            return settings.Height + height;
        }
    }
}
