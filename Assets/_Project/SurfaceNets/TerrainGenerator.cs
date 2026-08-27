using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using _Project.SurfaceNets.Data;
using _Project.SurfaceNets.Generator;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Analytics;
using UnityEngine.Pool;
using Debug = UnityEngine.Debug;

namespace _Project.SurfaceNets
{
    public enum LOD
    {
        LOD0,
        LOD1,
        LOD2
    }

    public enum ChunkState
    {
        Generating,
        Loaded,
        Empty
    }
    
    public class ChunkInfo
    {
        public ChunkSN Chunk;
        public LOD LOD;
        public ChunkState State;
        public NativeArray<Point> Density;
        private bool Modified = false;
    }

    public class TerrainGenerator : MonoBehaviour
    {
        [SerializeField] private GeneratorData genData;

        [Space(15), Header("Chunk Generation")] 
        [SerializeField] private ChunkSN chunkPrefab;

        [SerializeField] private bool destroyAir = true;
        [SerializeField] private bool destroySolid = true;
        [SerializeField] private bool generateAsync = true;

        [Space(15), Header("Dynamic Render Distance")]
        [SerializeField] private Transform player;
        [SerializeField] private int renderDistance = 8;

        [Space(15), Header("LOD")]
        [SerializeField] private int lod0Step = 1;
        [SerializeField] private int lod1Step = 2;
        [SerializeField] private int lod2Step = 4;
        [SerializeField] private int lod0Distance = 4;
        [SerializeField] private int lod1Distance = 8;
        
        [Space(15), Header("Debug")]
        public bool measureGenTime = true;

        private static Vector3Int _renderLimit;
        private readonly Dictionary<Vector3Int, ChunkInfo> _chunks = new();
        private readonly List<Vector3Int> _chunksToUnload = new();
        private IObjectPool<ChunkSN> _chunkPool;
        private Vector3Int _lastChunkPlayerPos;

        private readonly List<long> times = new List<long>();

        #region UNITY
        private void Awake()
        {
            genData.InitializeNoise();

            int limit = CelestialBodyGenerator.GetSuggestedChunkRadius(genData.type, ChunkSN.Size);
            _renderLimit = new Vector3Int(limit, limit, limit);

            _chunkPool = new ObjectPool<ChunkSN>(
                createFunc: () => Instantiate(chunkPrefab, transform),
                actionOnGet: chunk => chunk.gameObject.SetActive(true),
                actionOnRelease: chunk => chunk.Reset(),
                actionOnDestroy: chunk => Destroy(chunk.gameObject),
                collectionCheck: true,
                defaultCapacity: 128,
                maxSize: 2046
            );
        }

        private void Update()
        {
            // objętość = (4/3) × π × r³
            UpdateChunksAroundPlayer();
        }

        private void OnDestroy()
        {
            genData.Dispose();
            _chunkPool.Clear();
            Debug.Log($"avg chunk gen time: {times.Average()}");
        }
        #endregion
        
        
        #region CHUNK GENERATION
        private void UpdateChunksAroundPlayer()
        {
            Vector3Int currentPlayerChunkPos = WorldPosToChunkIndex(player.position);
            if (currentPlayerChunkPos == _lastChunkPlayerPos) return;
            _lastChunkPlayerPos = currentPlayerChunkPos;
            
            AddNewChunks(currentPlayerChunkPos);
            CorrectLods(currentPlayerChunkPos);
            UnloadDistantChunks(currentPlayerChunkPos, renderDistance + 8);
        }
        
        private void UnloadDistantChunks(Vector3Int playerChunkPos, int maxDistance)
        {
            int maxDistSq = maxDistance * maxDistance;
            
            _chunksToUnload.Clear();
            foreach (KeyValuePair<Vector3Int, ChunkInfo> kvp in _chunks)
            {
                if(kvp.Value.State != ChunkState.Loaded) continue;
                Vector3Int delta = kvp.Key - playerChunkPos;
                int distSq = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;
                if (distSq > maxDistSq) _chunksToUnload.Add(kvp.Key);
            }
            
            foreach (Vector3Int key in _chunksToUnload)
            {
                if (!_chunks.TryGetValue(key, out ChunkInfo info)) 
                    continue;
                
                _chunkPool.Release(info.Chunk);
                info.Chunk = null;
                _chunks.Remove(key);
            }
        }

        private void CorrectLods(Vector3Int playerChunkPos)
        {
            foreach (KeyValuePair<Vector3Int, ChunkInfo> kvp in _chunks)
            {
                if(kvp.Value.State != ChunkState.Loaded) continue;
                
                Vector3Int delta = kvp.Key - playerChunkPos;
                int distSq = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;

                LOD correctLOD = GetLodByDistanceSq(distSq);
                if (kvp.Value.LOD != correctLOD)
                {
                    _chunkPool.Release(kvp.Value.Chunk);
                    kvp.Value.Chunk = null;
                    kvp.Value.LOD = correctLOD;
                    kvp.Value.State = ChunkState.Generating;
                    
                    if (generateAsync) _ = GenerateChunkAsync(kvp.Key);
                    else GenerateChunk(kvp.Key);
                }
            }
        }

        private void AddNewChunks(Vector3Int playerChunkPos)
        {
            // Odległość między dwoma punktami:
            // distance = sqrt((x2-x1)² + (y2-y1)² + (z2-z1)²)
            
            int renderDistSq = renderDistance * renderDistance;

            for (int x = -renderDistance; x <= renderDistance; x++)
            for (int y = -renderDistance; y <= renderDistance; y++)
            for (int z = -renderDistance; z <= renderDistance; z++)
            {
                int distSq = x * x + y * y + z * z;
                if (distSq > renderDistSq) continue; // poza kulą

                Vector3Int chunkIndex = playerChunkPos + new Vector3Int(x, y, z);

                if (math.abs(chunkIndex.x) > _renderLimit.x ||
                    math.abs(chunkIndex.y) > _renderLimit.y ||
                    math.abs(chunkIndex.z) > _renderLimit.z)
                    continue; // poza mapą
                
                // todo zamienić na TryAdd
                if (_chunks.ContainsKey(chunkIndex)) continue;
                
                LOD lod = GetLodByDistanceSq(distSq);
                
                _chunks.Add(chunkIndex, new ChunkInfo()
                {
                    LOD = lod,
                    State = ChunkState.Generating
                });
                
                if (generateAsync) _ = GenerateChunkAsync(chunkIndex);
                else GenerateChunk(chunkIndex);
            }
        }

        private Vector3Int WorldPosToChunkIndex(Vector3 playerPos)
        {
            Vector3 localPos = transform.InverseTransformPoint(playerPos);

            return new Vector3Int(
                Mathf.FloorToInt((localPos.x + ChunkSN.Offset.x) / ChunkSN.Size),
                Mathf.FloorToInt((localPos.y + ChunkSN.Offset.y) / ChunkSN.Size),
                Mathf.FloorToInt((localPos.z + ChunkSN.Offset.z) / ChunkSN.Size));
        }
        
        private LOD GetLodByDistanceSq(int distanceSq)
        {
            if (distanceSq <= lod0Distance * lod0Distance) return LOD.LOD0;
            if (distanceSq <= lod1Distance * lod1Distance) return LOD.LOD1;
            return LOD.LOD2;
        }
        
        private int GetLodStep(LOD lod)
        {
            switch (lod)
            {
                case LOD.LOD0: return lod0Step;
                case LOD.LOD1: return lod1Step;
                default: return lod2Step;
            }
        }
        
        private async Awaitable GenerateChunkAsync(Vector3Int chunkIndex)
        {
            if (!_chunks.TryGetValue(chunkIndex, out ChunkInfo chunkInfo)) return;
            
            Stopwatch sw = new Stopwatch();
            sw.Start();
            
            bool disableRenderer = false;
            NativeArray<Point> densityArray = new NativeArray<Point>();
            Vector3 chunkGenPos = chunkIndex * ChunkSN.Size - ChunkSN.Offset;
            
            if (chunkInfo.Density.IsCreated)
                densityArray = chunkInfo.Density;

            JobHandle handle = SurfaceNetsGenerator.ScheduleChunkMeshGeneration(
                chunkGenPos, genData.type, genData.Noise, GetLodStep(chunkInfo.LOD),
                ref densityArray,
                out Mesh.MeshDataArray meshDataArray,
                out NativeReference<Bounds> meshBounds);

            JobHandle.ScheduleBatchedJobs();

            while (!handle.IsCompleted)
                await Awaitable.NextFrameAsync();

            handle.Complete();

            if (meshDataArray[0].vertexCount == 0 && chunkInfo.LOD == LOD.LOD0) // tylko lod 0 moze oznaczać jako pusty
            {
                disableRenderer = true;
                bool isUnderground = densityArray[0].IsSolid;
                bool isAir = !isUnderground;

                if ((isAir && destroyAir) || (isUnderground && destroySolid))
                {
                    chunkInfo.State = ChunkState.Empty;
                    
                    if (!chunkInfo.Density.IsCreated) densityArray.Dispose();
                    meshDataArray.Dispose();
                    meshBounds.Dispose();

                    return;
                }
            }

            ChunkSN chunk = _chunkPool.Get();
            chunk.transform.localPosition = chunkGenPos;
            chunk.gameObject.name = $"Chunk_({chunkIndex.x}_{chunkIndex.y}_{chunkIndex.z})";
            chunk.SetMesh(meshDataArray, meshBounds.Value, disableRenderer);
            chunkInfo.Chunk = chunk;
            


            if (!chunkInfo.Density.IsCreated) densityArray.Dispose();
            meshBounds.Dispose();

            chunkInfo.State = ChunkState.Loaded;

            sw.Stop();
            times.Add(sw.ElapsedMilliseconds);
        }
        
        
        private void GenerateChunk(Vector3Int chunkIndex)
        {
           if (!_chunks.TryGetValue(chunkIndex, out ChunkInfo chunkInfo)) return;
            
            Stopwatch sw = new Stopwatch();
            sw.Start();
            
            bool disableRenderer = false;
            NativeArray<Point> densityArray = new NativeArray<Point>();
            Vector3 chunkGenPos = chunkIndex * ChunkSN.Size - ChunkSN.Offset;
            
            if (chunkInfo.Density.IsCreated)
                densityArray = chunkInfo.Density;

            JobHandle handle = SurfaceNetsGenerator.ScheduleChunkMeshGeneration(
                chunkGenPos, genData.type, genData.Noise, GetLodStep(chunkInfo.LOD),
                ref densityArray,
                out Mesh.MeshDataArray meshDataArray,
                out NativeReference<Bounds> meshBounds);

            JobHandle.ScheduleBatchedJobs();
            handle.Complete();

            if (meshDataArray[0].vertexCount == 0)
            {
                disableRenderer = true;
                bool isUnderground = densityArray[0].IsSolid;
                bool isAir = !isUnderground;

                if ((isAir && destroyAir) || (isUnderground && destroySolid))
                {
                    chunkInfo.State = ChunkState.Empty;
                    
                    if (!chunkInfo.Density.IsCreated) densityArray.Dispose();
                    meshDataArray.Dispose();
                    meshBounds.Dispose();

                    return;
                }
            }

            ChunkSN chunk = _chunkPool.Get();
            chunk.transform.localPosition = chunkGenPos;
            chunk.gameObject.name = $"Chunk_({chunkIndex.x}_{chunkIndex.y}_{chunkIndex.z})";
            chunk.SetMesh(meshDataArray, meshBounds.Value, disableRenderer);
            chunkInfo.Chunk = chunk;
            


            if (!chunkInfo.Density.IsCreated)
                densityArray.Dispose();
            meshBounds.Dispose();

            chunkInfo.State = ChunkState.Loaded;

            sw.Stop();
            times.Add(sw.ElapsedMilliseconds);
        }
        #endregion
    }
}