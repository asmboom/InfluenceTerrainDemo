﻿using System;
using System.Collections.Generic;
using TerrainDemo.Hero;
using TerrainDemo.Layout;
using TerrainDemo.Tools;
using UnityEditor;
using UnityEngine;
using Input = TerrainDemo.Hero.Input;

namespace TerrainDemo.Settings
{
    public class ObserverSettings : MonoBehaviour, IObserver
    {
        public CameraType Camera;
        [Range(0, 100)]
        public int AOIRange = 50;
        public bool ShowChunksValue;

        public float Speed = 10;
        public float RotationSpeed = 180;

        public float FOV { get { return _camera.fieldOfView; } }
        public Vector3 Position { get { return _camera.transform.position; } }
        public Quaternion Rotation { get { return _camera.transform.rotation; } }

        public float Range { get { return AOIRange; } }

        /// <summary>
        /// Get chunk positions order by valuable
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ChunkPositionValue> ValuableChunkPos(float range)
        {
            var chunkCenterPos = Chunk.GetPosition(new Vector2(Position.x, Position.z));
            var chunkRange = (int)(range/Chunk.Size);

            var result = new List<ChunkPositionValue>();
            //Get all chunk positions in a range
            foreach (var chunkPos in new Bounds2i(chunkCenterPos, chunkRange))
                if (Vector2i.Distance(chunkCenterPos, chunkPos)*Chunk.Size < range)
                    result.Add(new ChunkPositionValue()
                    {
                        Position = chunkPos,
                        Value = GetChunkPositionValue(chunkPos, range)
                    });

            result.Sort();
            return result;
        }

        /// <summary>
        /// Get 'flat' value (dont take account on height)
        /// </summary>
        /// <param name="position"></param>
        /// <param name="range"></param>
        /// <returns></returns>
        public float GetPositionValue(Vector2 position, float range)
        {
            var observerPos = new Vector2(Position.x, Position.z);

            //Distance estimation
            var distanceEst = Mathf.Sqrt(Mathf.Clamp01(1 - Vector2.Distance(position, observerPos)/range));
            if (distanceEst < 0.0001f) return 0;

            //Angle estimation
            var chunkDir = position - observerPos;
            var observerDir = new Vector2(_camera.transform.forward.x, _camera.transform.forward.z);

            //Decrease estimation for position behind my back
            if (Vector2.Angle(chunkDir, observerDir) > FOV / 2)
                distanceEst *= Mathf.InverseLerp(180, FOV / 2, Vector2.Angle(chunkDir, observerDir));

            return distanceEst;
        }

        /// <summary>
        /// Get 'flat' value (dont take account on height)
        /// </summary>
        /// <param name="chunkPos"></param>
        /// <param name="range"></param>
        /// <returns></returns>
        public float GetChunkPositionValue(Vector2i chunkPos, float range)
        {
            //Fast pass
            if (chunkPos == Chunk.GetPosition(Position))
                return 1;

            var chunkCenterPos = Chunk.GetCenter(chunkPos);
            return GetPositionValue(chunkCenterPos, range);
        }

        public bool IsZoneVisible(ZoneLayout zone)
        {
            var observerPos = new Vector2(Position.x, Position.z);

            foreach (var zoneVert in zone.Cell.Vertices)
                if (Vector2.Distance(zoneVert, observerPos) < Range)
                    return true;

            return false;
        }

        public event Action Changed = delegate {};

        private Camera _camera;

        private Vector3 _oldPosition;
        private Quaternion _oldRotation;
        private float _lastOldCheck;
        private float _currentRotation;

        private void InputOnRotate(float rotateDir)
        {
            _currentRotation += rotateDir*RotationSpeed*Time.deltaTime;
            var rotation = Quaternion.Euler(25, _currentRotation, 0);
            transform.rotation = rotation;
        }

        private void InputOnMove(Vector3 moveDir)
        {
            moveDir = Rotation * moveDir;
            moveDir.y = 0;
            transform.position += moveDir*Time.deltaTime*Speed;
        }

        #region Unity

        void Start()
        {
            Changed();

            var input = GetComponent<Input>();
            input.Move += InputOnMove;
            input.Rotate += InputOnRotate;

        }

        void Update()
        {
            if (Time.time - _lastOldCheck > 0.5f && (Position != _oldPosition || Rotation != _oldRotation))
            {
                _lastOldCheck = Time.time;
                _oldPosition = Position;
                _oldRotation = Rotation;
                Changed();
            }
        }

        void OnValidate()
        {
            if (Application.isPlaying)
            {
                if (Camera == CameraType.SceneCamera)
                    _camera = SceneView.currentDrawingSceneView.camera;
                else
                    _camera = UnityEngine.Camera.main;
            }
        }

        void OnDrawGizmos()
        {
            if (Application.isPlaying && ShowChunksValue)
            {
                foreach (var valuableChunk in ValuableChunkPos(Range))
                    DrawRectangle.ForGizmo(
                        Chunk.GetBounds(valuableChunk.Position), 
                        Color.Lerp(Color.black, Color.red, valuableChunk.Value), true);
            }
        }

        #endregion

        public enum CameraType
        {
            SceneCamera,
            MainCamera,
        }

        public struct ChunkPositionValue : IComparable<ChunkPositionValue>
        {
            public Vector2i Position;
            public float Value;
            public int CompareTo(ChunkPositionValue other)
            {
                return other.Value.CompareTo(Value);
            }
        }
    }
}
