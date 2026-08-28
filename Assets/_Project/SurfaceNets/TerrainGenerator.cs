using System.Collections.Generic;
using _Project.SurfaceNets.Data;
using _Project.SurfaceNets.Generator;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Pool;

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
        Air,
        Solid
    }
    
    public class ChunkInfo
    {
        public ChunkSN Chunk;
        public LOD LOD;
        public ChunkState State;
        public Point[] Density = null;
        public bool ToRemove = false;
        private bool Modified = false;
    }

    public class TerrainGenerator : MonoBehaviour
    {
        [SerializeField] private GeneratorData genData;

        [Space(15), Header("Chunk Generation")] 
        [SerializeField] private ChunkSN chunkPrefab;
        [SerializeField] private int maxConcurrentGen = 16;
        

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
        [SerializeField] private int currentGen = 0;
        
        private readonly Dictionary<Vector3Int, ChunkInfo> _chunks = new();
        private readonly Queue<Vector3Int> _generationQueue = new();
        private readonly List<Vector3Int> _chunksToUnload = new();
        
        private IObjectPool<ChunkSN> _chunkPool;
        private Vector3Int _lastChunkPlayerPos;
        private static Vector3Int _renderLimit;
        
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
            RenderChunks();
        }

        private void OnDestroy()
        {
            genData.Dispose();
            _chunkPool.Clear();
        }
        #endregion
        
        
        #region CHUNK GENERATION
        private void UpdateChunksAroundPlayer()
        {
            Vector3Int currentPlayerChunkPos = WorldPosToChunkIndex(player.position);
            if (currentPlayerChunkPos == _lastChunkPlayerPos) return;
            _lastChunkPlayerPos = currentPlayerChunkPos;
            
            ClearDistantChunks(currentPlayerChunkPos, renderDistance + 2);
            AddNewChunks(currentPlayerChunkPos);
            CorrectLods(currentPlayerChunkPos);
        }
        
        private void ClearDistantChunks(Vector3Int playerChunkPos, int maxDistance)
        {
            int maxDistSq = maxDistance * maxDistance;
            _chunksToUnload.Clear();
            
            foreach (KeyValuePair<Vector3Int, ChunkInfo> kvp in _chunks)
            {
                // todo osobne czyszczenie pamięci dla pustych chunkow
                if (kvp.Value.State == ChunkState.Air || kvp.Value.State == ChunkState.Solid)
                    continue;
                
                Vector3Int delta = kvp.Key - playerChunkPos;
                int distSq = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;
                if (distSq > maxDistSq) 
                    _chunksToUnload.Add(kvp.Key);
            }
            
            foreach (Vector3Int key in _chunksToUnload) 
                UnloadChunk(key);
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
                if (distSq > renderDistSq)
                    continue; // poza kulą
                
                Vector3Int chunkIndex = playerChunkPos + new Vector3Int(x, y, z);
                
                if (math.abs(chunkIndex.x) > _renderLimit.x ||
                    math.abs(chunkIndex.y) > _renderLimit.y ||
                    math.abs(chunkIndex.z) > _renderLimit.z)
                    continue; // poza mapą
                
                // todo zamienić na TryAdd
                if (_chunks.ContainsKey(chunkIndex)) 
                    continue;
                
                LOD lod = GetLodByDistanceSq(distSq);
                
                _chunks.Add(chunkIndex, new ChunkInfo()
                {
                    LOD = lod,
                    State = ChunkState.Generating
                });
                
                _generationQueue.Enqueue(chunkIndex);
            }
        }
        
        private void CorrectLods(Vector3Int playerChunkPos)
        {
            foreach (KeyValuePair<Vector3Int, ChunkInfo> kvp in _chunks)
            {
                if(kvp.Value.State != ChunkState.Loaded) 
                    continue;
                
                Vector3Int delta = kvp.Key - playerChunkPos;
                int distSq = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;

                LOD correctLOD = GetLodByDistanceSq(distSq);
                if (kvp.Value.LOD != correctLOD)
                {
                    _chunkPool.Release(kvp.Value.Chunk);
                    kvp.Value.Chunk = null;
                    kvp.Value.LOD = correctLOD;
                    kvp.Value.State = ChunkState.Generating;

                    _generationQueue.Enqueue(kvp.Key);
                }
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
        
        private void RenderChunks()
        {
            if(_generationQueue.Count == 0 ) return;
            while (currentGen < maxConcurrentGen && _generationQueue.Count > 0)
            {
                Vector3Int chunkIndex = _generationQueue.Dequeue();
                _ = GenerateChunkAsync(chunkIndex);
                currentGen++;
            }
        }
        
        private async Awaitable GenerateChunkAsync(Vector3Int chunkIndex)
        {
            try
            {
                if (!_chunks.TryGetValue(chunkIndex, out ChunkInfo chunkInfo)) 
                    return;
                
                if (chunkInfo.ToRemove)
                {
                    _chunks.Remove(chunkIndex);
                    return;
                }
                
                Vector3 chunkGenPos = chunkIndex * ChunkSN.Size - ChunkSN.Offset;
                NativeArray<Point> densityArray = chunkInfo.Density != null
                ? new NativeArray<Point>(chunkInfo.Density, Allocator.TempJob)
                : new NativeArray<Point>();

                JobHandle handle = SurfaceNetsGenerator.ScheduleChunkMeshGeneration(
                    chunkGenPos, genData.type, genData.Noise, GetLodStep(chunkInfo.LOD),
                    ref densityArray,
                    out Mesh.MeshDataArray meshDataArray,
                    out NativeReference<Bounds> meshBounds);

                JobHandle.ScheduleBatchedJobs();
                
                while (!handle.IsCompleted)
                    await Awaitable.NextFrameAsync();
                
                handle.Complete();

                // po odczekaniu 1 klatki
                try
                {
                    // czy chunk nie został skasowany przez UnloadDistantChunks w trakcie czekania
                    if (!_chunks.TryGetValue(chunkIndex, out chunkInfo)|| chunkInfo.ToRemove)
                    {
                        _chunks.Remove(chunkIndex);
                        meshDataArray.Dispose();
                        return;
                    }
                        
                    
                    if (meshDataArray[0].vertexCount == 0)
                    {
                        bool isSolid = densityArray[0].IsSolid;
                        bool isAir = !isSolid;

                        if (isAir) chunkInfo.State = ChunkState.Air;
                        if (isSolid) chunkInfo.State = ChunkState.Solid;
                        meshDataArray.Dispose();
                        return;
                    }

                    ChunkSN chunk = _chunkPool.Get();
                    chunk.transform.localPosition = chunkGenPos;
                    chunk.gameObject.name = $"Chunk_({chunkIndex.x}_{chunkIndex.y}_{chunkIndex.z})";
                    chunk.SetMesh(meshDataArray, meshBounds.Value);
                    
                    chunkInfo.Chunk = chunk;
                    chunkInfo.State = ChunkState.Loaded;
                }
                finally
                {
                    if (densityArray.IsCreated)
                        densityArray.Dispose();
                       
                    
                    meshBounds.Dispose();
                }
            }
            finally
            {
                currentGen--;
            }
        }
        
        private void UnloadChunk(Vector3Int chunkIndex)
        {
            if (!_chunks.TryGetValue(chunkIndex, out ChunkInfo info))
                return;
            
            if (info.State == ChunkState.Generating)
            {
                info.ToRemove = true;
                return;
            }
            
            _chunks.Remove(chunkIndex);
            
            if (info.State == ChunkState.Loaded)
            {
                _chunkPool.Release(info.Chunk);
                info.Chunk = null;
            }
        }
        
        #endregion
    }
}