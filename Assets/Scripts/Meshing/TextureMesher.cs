﻿using System.Collections.Generic;
using System.Linq;
using TerrainDemo.Settings;
using TerrainDemo.Tools;
using UnityEngine;

namespace TerrainDemo.Meshing
{
    public class TextureMesher
    {
        public AverageTimer MeshTimer { get { return _meshTimer;} }
        public AverageTimer TextureTimer { get { return _textureTimer; } }

        public TextureMesher(ILandSettings settings, MesherSettings meshSettings)
        {
            _meshSettings = meshSettings;

            foreach (var block in settings.Blocks)
            {
                if(block.FlatTexture != null)
                    _blockSettings.Add(block.Block, block);
            }
        }

        ~TextureMesher()
        {
            //if(_renderTexture != null)                    //todo Call from main thread
            //    _renderTexture.Release();     
        }

        public ChunkModel Generate(Chunk chunk, Dictionary<Vector2i, Chunk> map)
        {
            _meshTimer.Start();

            var mesh = new Mesh();

            var verts = new Vector3[chunk.GridSize * chunk.GridSize];
            for (int z = 0; z < chunk.GridSize; z++)
                for (int x = 0; x < chunk.GridSize; x++)
                    verts[x + z * chunk.GridSize] = new Vector3(x * chunk.BlockSize, chunk.HeightMap[x, z], z * chunk.BlockSize);
            mesh.vertices = verts;

            var indx = new int[(chunk.GridSize - 1) * (chunk.GridSize - 1) * 4];
            for (int z = 0; z < chunk.GridSize - 1; z++)
                for (int x = 0; x < chunk.GridSize - 1; x++)
                {
                    var index = (x + z * (chunk.GridSize - 1)) * 4;
                    indx[index + 0] = x + z * chunk.GridSize;
                    indx[index + 1] = x + (z + 1) * chunk.GridSize; 
                    indx[index + 2] = x + 1 + (z + 1) * chunk.GridSize;
                    indx[index + 3] = x + 1 + z * chunk.GridSize;
                }
            mesh.SetIndices(indx, MeshTopology.Quads, 0);

            var uv = new Vector2[mesh.vertexCount];
            for (int i = 0; i < uv.Length; i++)
                uv[i] = new Vector2(verts[i].x/(chunk.GridSize - 1), verts[i].z/(chunk.GridSize - 1));
            mesh.uv = uv;

            //mesh.RecalculateNormals();
            mesh.normals = CalculateNormals(chunk, map);

            mesh.RecalculateBounds();

            _meshTimer.Stop();

            //Generate texture
            _textureTimer.Start();
            //var tex = GenerateTextureCPU(chunk);
            var tex = GenerateTextureShader(chunk, map);
            _textureTimer.Stop();

            var material = new Material(_meshSettings.Material);
            material.mainTexture = tex.Diffuse;

            return new ChunkModel {Mesh = mesh, Material = material };
        }

        private readonly MesherSettings _meshSettings;

        private readonly AverageTimer _meshTimer = new AverageTimer();
        private readonly AverageTimer _textureTimer = new AverageTimer();
        
        private readonly Dictionary<BlockType, BlockRenderSettings> _blockSettings = new Dictionary<BlockType, BlockRenderSettings>();
        private readonly Dictionary<BlockType, RenderTexture> _blockResult = new Dictionary<BlockType, RenderTexture>();

        //private Texture2D GenerateTextureCPU(Chunk chunk)
        //{
        //    var result = new Texture2D(1024, 1024, TextureFormat.RGB24, true);
        //    var pixels = result.GetPixels();

        //    for (int z = 0; z < 1024 - 1; z++)
        //        for (int x = 0; x < 1024 - 1; x++)
        //        {
        //            var blockType = chunk.BlockType[x/64, z/64];
        //            if (blockType == BlockType.Rock)
        //                pixels[x + z*1024] = _stone[x + z * 1024];
        //            else
        //                pixels[x + z*1024] = _grass[x + z * 1024];
        //        }

        //    result.SetPixels(pixels);
        //    result.Apply();

        //    return result;
        //}

        private RenderTexture GetRenderTexture()
        {
            //Cant reuse texture if drawing many textures for one frame
            var renderTexture = new RenderTexture(_meshSettings.TextureSize, _meshSettings.TextureSize, 0);
            renderTexture.wrapMode = TextureWrapMode.Clamp;
            renderTexture.enableRandomWrite = true;
            renderTexture.useMipMap = false;
            renderTexture.generateMips = false;
            renderTexture.Create();

            return renderTexture;
        }

        private RenderTexture GetTextureFor(BlockType block, Texture2D geoMap, HeightMap heightMap, int mapBorder)
        {
            var settings = _blockSettings[block];

            var renderTriTex = GetRenderTexture();
            var triShader = _meshSettings.TriplanarTextureShader;
            triShader.SetTexture(0, "FlatTexture", settings.FlatTexture);
            triShader.SetTexture(0, "SteepTexture", settings.SteepTexture);
            triShader.SetTexture(0, "HeightMap", heightMap.Map);
            triShader.SetTexture(0, "Normals", geoMap);
            triShader.SetInt("Border", mapBorder);
            triShader.SetFloat("Lower", heightMap.Lower);
            triShader.SetFloat("Upper", heightMap.Upper);
            triShader.SetFloat("SteepAngleFrom", settings.SteepAngles.x);
            triShader.SetFloat("SteepAngleTo", settings.SteepAngles.y);
            triShader.SetTexture(0, "Result", renderTriTex);
            triShader.Dispatch(0, renderTriTex.width / 8, renderTriTex.height / 8, 1);

            return renderTriTex;
        }

        private Textures GenerateTextureShader(Chunk chunk, Dictionary<Vector2i, Chunk> map)
        {
            var border = _meshSettings.MaskBorder;

            var mask = CalculateBlockMask(chunk, border, map);
            
            var geoMask = PrepareGeometryMask(mask);
            var heightMask = PrepareHeightMask(chunk);
            
            foreach (var blockType in _blockSettings.Keys)
            {
                var renderTriTex = GetTextureFor(blockType, geoMask, heightMask, border);
                _blockResult[blockType] = renderTriTex;
            }
            
            var blockMask = PrepareBlockTypeMask(mask);
            var renderTex = GetRenderTexture();
            //var renderTexNrm = GetRenderTexture();
            var shader = _meshSettings.TextureBlendShader;
            shader.SetTexture(0, "mask", blockMask);
            shader.SetInt("border", border);
            shader.SetFloat("turbulence", _meshSettings.Turbulence);
            shader.SetTexture(0, "noise", _meshSettings.NoiseTexture);
            if(_blockResult.ContainsKey(BlockType.Grass))
                shader.SetTexture(0, "grass", _blockResult[BlockType.Grass]);
            if (_blockResult.ContainsKey(BlockType.Rock))
                shader.SetTexture(0, "stone", _blockResult[BlockType.Rock]);
            if (_blockResult.ContainsKey(BlockType.Sand))
                shader.SetTexture(0, "sand", _blockResult[BlockType.Sand]);
            if (_blockResult.ContainsKey(BlockType.Water))
                shader.SetTexture(0, "water", _blockResult[BlockType.Water]);
            if (_blockResult.ContainsKey(BlockType.Snow))
                shader.SetTexture(0, "snow", _blockResult[BlockType.Snow]);
            shader.SetTexture(0, "result", renderTex);
            //shader.SetTexture(0, "resultNrm", renderTexNrm);
            shader.Dispatch(0, renderTex.width / 8, renderTex.height / 8, 1);
            
            //Render computed texture to another one to generate auto mipmaps
            var renderTex2 = new RenderTexture(renderTex.width, renderTex.height, 0);
            renderTex2.useMipMap = true;
            renderTex2.generateMips = true;
            renderTex2.wrapMode = renderTex.wrapMode;
            renderTex2.Create();
            Graphics.Blit(renderTex, renderTex2);

            //Destroy old textures
            renderTex.Release();
            Object.Destroy(renderTex);

            foreach (var blockResult in _blockResult)
            {
                blockResult.Value.Release();
                Object.Destroy(blockResult.Value);
            }
            _blockResult.Clear();

            return new Textures() {Diffuse = renderTex2/*, Normal = renderTex2Nrm*/};
        }

        private HeightMap PrepareHeightMask(Chunk chunk)
        {
            var lowerHeight = float.MaxValue;
            var upperHeight = float.MinValue;
            for (int z = 0; z < chunk.GridSize; z++)
                for (int x = 0; x < chunk.GridSize; x++)
                {
                    if (chunk.HeightMap[x, z] > upperHeight)
                        upperHeight = chunk.HeightMap[x, z];
                    if (chunk.HeightMap[x, z] < lowerHeight)
                        lowerHeight = chunk.HeightMap[x, z];
                }

            var mask = new Color[chunk.GridSize*chunk.GridSize];
            for (int z = 0; z < chunk.GridSize; z++)
                for (int x = 0; x < chunk.GridSize; x++)
                {
                    mask[x + z*chunk.GridSize] = new Color(0, Mathf.InverseLerp(lowerHeight, upperHeight, chunk.HeightMap[x, z]), 0);
                }

            var result = new Texture2D(chunk.GridSize, chunk.GridSize, TextureFormat.RGB24, false);
            result.wrapMode = TextureWrapMode.Clamp;
            result.SetPixels(mask);
            result.Apply(false, true);

            return new HeightMap {Lower = lowerHeight, Upper = upperHeight, Map = result};
        }

        private Texture2D PrepareBlockTypeMask(ChunkMaskBlock[,] blocks)
        {
            var result = new Color[blocks.Length];
            for (int z = 0; z <= blocks.GetUpperBound(1); z++)
                for (int x = 0; x <= blocks.GetUpperBound(0); x++)
                {
                    var blockType = blocks[x, z].Block;
                    if (blockType == BlockType.Grass)
                        result[x + z * blocks.GetLength(1)] = new Color(0, 1, 0, 0);
                    else if (blockType == BlockType.Sand)
                        result[x + z * blocks.GetLength(1)] = new Color(1, 0, 0, 0);
                    else if (blockType == BlockType.Water)
                        result[x + z * blocks.GetLength(1)] = new Color(0, 0, 1, 0);
                    else if (blockType == BlockType.Snow)
                        result[x + z * blocks.GetLength(1)] = new Color(0, 0, 0, 1);
                    //Stone - no color at all
                }

            var resultTexture = new Texture2D(blocks.GetLength(0), blocks.GetLength(1), TextureFormat.RGBA32, false);
            resultTexture.wrapMode = TextureWrapMode.Clamp;
            resultTexture.SetPixels(result);
            resultTexture.Apply(false, true);
            return resultTexture;
        }

        private Texture2D PrepareGeometryMask(ChunkMaskBlock[,] blocks)
        {
            var result = new Color[blocks.Length];
            for (int z = 0; z <= blocks.GetUpperBound(1); z++)
                for (int x = 0; x <= blocks.GetUpperBound(0); x++)
                {
                    var normal = blocks[x, z].Normal;
                    result[x + z * blocks.GetLength(1)].r = normal.x / 2 + 0.5f;
                    result[x + z * blocks.GetLength(1)].g = normal.y / 2 + 0.5f;
                    result[x + z * blocks.GetLength(1)].b = normal.z / 2 + 0.5f;
                    result[x + z * blocks.GetLength(1)].a = Vector3.Angle(normal, Vector3.up)/90f;
                }

            var resultTexture = new Texture2D(blocks.GetLength(0), blocks.GetLength(1), TextureFormat.RGBA32, false);
            resultTexture.wrapMode = TextureWrapMode.Clamp;
            resultTexture.SetPixels(result);
            resultTexture.Apply(false, true);
            return resultTexture;
        }

        private ChunkMaskBlock[,] CalculateBlockMask(Chunk chunk, int border, Dictionary<Vector2i, Chunk> map)
        {
            Chunk top, bottom, left, right, topleft, bottomleft, topright, bottomright;
            map.TryGetValue(chunk.Position + Vector2i.Forward, out top);
            map.TryGetValue(chunk.Position + Vector2i.Back, out bottom);
            map.TryGetValue(chunk.Position + Vector2i.Left, out left);
            map.TryGetValue(chunk.Position + Vector2i.Right, out right);
            map.TryGetValue(chunk.Position + Vector2i.Forward + Vector2i.Left, out topleft);
            map.TryGetValue(chunk.Position + Vector2i.Back + Vector2i.Left, out bottomleft);
            map.TryGetValue(chunk.Position + Vector2i.Forward + Vector2i.Right, out topright);
            map.TryGetValue(chunk.Position + Vector2i.Back + Vector2i.Right, out bottomright);

            var bc = chunk.BlocksCount;
            var blocks = new ChunkMaskBlock[chunk.BlocksCount + 2*border, chunk.BlocksCount + 2*border];

            CopyBlocks(chunk, blocks, new Bounds2i(Vector2i.Zero, Vector2i.One*(bc - 1)), Vector2i.One*border);

            if (border > 0)
            {
                if(bottomleft != null)
                    CopyBlocks(bottomleft, blocks, new Bounds2i(Vector2i.One * (bc - border), border, border), Vector2i.Zero);
                if (bottom != null)
                    CopyBlocks(bottom, blocks, new Bounds2i(new Vector2i(0, bc - border), bc, border), new Vector2i(border, 0));
                if (bottomright != null)
                    CopyBlocks(bottomright, blocks, new Bounds2i(new Vector2i(0, bc - border), border, border), new Vector2i(bc + border, 0));
                if (left != null)
                    CopyBlocks(left, blocks, new Bounds2i(new Vector2i(bc - border, 0), border, bc), new Vector2i(0, border));
                if (right != null)
                    CopyBlocks(right, blocks, new Bounds2i(new Vector2i(0, 0), border, bc), new Vector2i(bc+border, border));
                if (topleft != null)
                    CopyBlocks(topleft, blocks, new Bounds2i(new Vector2i(bc - border, 0), border, border), new Vector2i(0, bc + border));
                if (top != null)
                    CopyBlocks(top, blocks, new Bounds2i(new Vector2i(0, 0), bc, border), new Vector2i(border, bc+border));
                if (topright != null)
                    CopyBlocks(topright, blocks, new Bounds2i(Vector2i.Zero, border, border), new Vector2i(bc + border));
            }

            return blocks;
        }

        private void CopyBlocks(Chunk src, ChunkMaskBlock[,] dest, Bounds2i srcBounds, Vector2i destPosition)
        {
            for (int z = srcBounds.Min.Z; z <= srcBounds.Max.Z; z++)
            {
                var destPosZ = destPosition.Z + (z - srcBounds.Min.Z);
                for (int x = srcBounds.Min.X; x <= srcBounds.Max.X; x++)
                {
                    var destPosX = destPosition.X + (x - srcBounds.Min.X);
                    dest[destPosX, destPosZ].Block = src.BlockType[x, z];
                    dest[destPosX, destPosZ].Normal = src.NormalMap[x, z];
                }
            }
        }

        Vector3[] CalculateNormals(Chunk chunk, Dictionary<Vector2i, Chunk> map)
        {
            Chunk top, bottom, left, right;

            map.TryGetValue(chunk.Position + Vector2i.Forward, out top);
            map.TryGetValue(chunk.Position + Vector2i.Back, out bottom);
            map.TryGetValue(chunk.Position + Vector2i.Left, out left);
            map.TryGetValue(chunk.Position + Vector2i.Right, out right);

            Vector3[] result = new Vector3[chunk.GridSize * chunk.GridSize];

            //Inner loop
            for (int z = 1; z < chunk.GridSize - 1; z++)
                for (int x = 1; x < chunk.GridSize - 1; x++)
                    result[x + z*chunk.GridSize] = CalculateNormal(chunk.HeightMap[x - 1, z], chunk.HeightMap[x + 1, z],
                        chunk.HeightMap[x, z - 1], chunk.HeightMap[x, z + 1]);

            //Outer loops
            for (int i = 1; i < chunk.GridSize - 1; i++)
            {
                var x = 0;
                var z = i;
                if (left != null)
                    result[x + z*chunk.GridSize] = CalculateNormal(left.HeightMap[chunk.GridSize - 2, z],
                        chunk.HeightMap[x + 1, z], chunk.HeightMap[x, z - 1], chunk.HeightMap[x, z + 1]);
                x = chunk.GridSize - 1;
                if (right != null)
                    result[x + z * chunk.GridSize] = CalculateNormal(chunk.HeightMap[x - 1, z],  
                        right.HeightMap[1, z], chunk.HeightMap[x, z - 1], chunk.HeightMap[x, z + 1]);
                x = i;
                z = 0;
                if (bottom != null)
                    result[x + z * chunk.GridSize] = CalculateNormal(chunk.HeightMap[x - 1, z], chunk.HeightMap[x + 1, z], 
                        bottom.HeightMap[x, chunk.GridSize - 2], chunk.HeightMap[x, z + 1]);
                z = chunk.GridSize - 1;
                if (top != null)
                    result[x + z * chunk.GridSize] = CalculateNormal(chunk.HeightMap[x - 1, z], chunk.HeightMap[x + 1, z],
                        chunk.HeightMap[x, z - 1], top.HeightMap[x, 1]);
            }

            //Corners
            if (bottom != null && left != null)
            {
                var x = 0;
                var z = 0;
                result[x + z * chunk.GridSize] = CalculateNormal(left.HeightMap[chunk.GridSize - 2, z], chunk.HeightMap[x + 1, z],
                    bottom.HeightMap[x, chunk.GridSize - 2], chunk.HeightMap[x, z + 1]);
            }
            if (top != null && left != null)
            {
                var x = 0;
                var z = chunk.GridSize - 1;
                result[x + z * chunk.GridSize] = CalculateNormal(left.HeightMap[chunk.GridSize - 2, z], chunk.HeightMap[x + 1, z],
                    chunk.HeightMap[x, z - 1], top.HeightMap[x, 1]);
            }
            if (top != null && right != null)
            {
                var x = chunk.GridSize - 1;
                var z = chunk.GridSize - 1;
                result[x + z * chunk.GridSize] = CalculateNormal(chunk.HeightMap[x - 1, z], right.HeightMap[1, z],
                    chunk.HeightMap[x, z - 1], top.HeightMap[x, 1]);
            }
            if (bottom != null && right != null)
            {
                var x = chunk.GridSize - 1;
                var z = 0;
                result[x + z * chunk.GridSize] = CalculateNormal(chunk.HeightMap[x - 1, z], right.HeightMap[1, z],
                    bottom.HeightMap[x, chunk.GridSize - 2], chunk.HeightMap[x, z + 1]);
            }

            return result;
        }

        private Vector3 CalculateNormal(float heightX0, float heightx1, float heightZ0, float heightZ1)
        {
            //Based on http://gamedev.stackexchange.com/questions/70546/problem-calculating-normals-for-heightmaps
            var sx = heightX0 - heightx1;
            var sy = heightZ0 - heightZ1;

            return new Vector3(sx, 2, sy).normalized;
        }

        public struct ChunkModel
        {
            public Mesh Mesh;
            public Material Material;
        }

        public struct ChunkMaskBlock
        {
            public BlockType Block;
            //public float Inclination;
            public Vector3 Normal;
        }

        public struct Textures
        {
            public Texture Diffuse;
            public Texture Normal;
        }

        public struct HeightMap
        {
            public Texture2D Map;
            public float Lower;
            public float Upper;
        }
    }
}
