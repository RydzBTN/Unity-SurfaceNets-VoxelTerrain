using System.Collections.Generic;
using _Project.SurfaceNets.Data;
using _Project.SurfaceNets.Generator;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Pool;

namespace _Project.SurfaceNets.Chunks
{
    public enum LOD
    {
        LOD0,
        LOD1
    }
    
    public class TerrainGenerator : MonoBehaviour
    {
        [SerializeField] private GeneratorData genData;

        [Space(15), Header("Chunk Generation")] 
        [SerializeField] private Chunk chunkPrefab;

        [SerializeField] private int maxConcurrentGen = 16;

        [Space(15), Header("Dynamic Render Distance")] 
        [SerializeField] private Transform player;

        [SerializeField] private int renderDistance = 8;

        [Space(15), Header("LOD")] 
        [SerializeField] private int lod0Step = 1;
        [SerializeField] private int lod1Step = 4;
        [SerializeField] private int lod0Distance = 8;

        [Space(15), Header("Debug")] 
        [SerializeField] private int currentGen = 0;
        
        
        //kierunki dla każdej z 3 osi
        private struct ShellChunks
        {
            public int3[] X;
            public int3[] Y;
            public int3[] Z;
            
            public int3[] Get(int axis) => axis switch
            {
                1 => X,
                2 => Y,
                _ => Z
            };
        }
        
        private ShellChunks _renderShell;
        private ShellChunks _unloadShell;
        private ShellChunks _lod0Shell;
        
        private readonly Dictionary<Vector3Int, ChunkInfo> _chunks = new();
        private readonly Queue<Vector3Int> _pendingQueue = new();
        private readonly HashSet<Vector3Int> _pendingSet = new();
        private readonly List<Vector3Int> _chunksToUnload = new();

        private IObjectPool<Chunk> _chunkPool;
        private Vector3Int _lastChunkPlayerPos;
        private static Vector3Int _renderLimit;


        // ------------------------------------------------------------------
        // UNITY
        // ------------------------------------------------------------------
        private void Awake()
        {
            genData.InitializeNoise();

            int limit = CelestialBodyGenerator.GetSuggestedChunkRadius(genData.type, Chunk.Size);
            _renderLimit = new Vector3Int(limit, limit, limit);

            _chunkPool = new ObjectPool<Chunk>(
                createFunc: () => Instantiate(chunkPrefab, transform),
                actionOnGet: chunk => chunk.gameObject.SetActive(true),
                actionOnRelease: chunk => chunk.Reset(),
                actionOnDestroy: chunk => Destroy(chunk.gameObject),
                collectionCheck: true,
                defaultCapacity: 128,
                maxSize: 2046
            );
            
            _renderShell = BuildShell(renderDistance);
            _unloadShell = BuildShell(renderDistance + 2);
            _lod0Shell = BuildShell(lod0Distance);
        }

        private void Start()
        {
            FullRebuildChunks();
        }
        
        private void Update()
        {
            UpdateChunksAroundPlayer();
            RenderChunks();
        }
        
        private void OnDestroy()
        {
            genData.Dispose();
            _chunkPool.Clear();
        }
        
        
        // ------------------------------------------------------------------
        // CHUNK SHELL BUILDING
        // ------------------------------------------------------------------
        private static HashSet<int3> BuildSphere(int radius)
        {
            var set = new HashSet<int3>();
            int r2 = radius * radius;
            for (int x = -radius; x <= radius; x++)
            for (int y = -radius; y <= radius; y++)
            for (int z = -radius; z <= radius; z++)
                if (x * x + y * y + z * z <= r2)
                    set.Add(new int3(x, y, z));
            return set;
        }

        private static int3[] BuildAxisShell(HashSet<int3> sphere, int3 direction)
        {
            var list = new List<int3>();
                
            foreach (int3 o in sphere)
                if (!sphere.Contains(o - direction))
                    list.Add(o);
                        
            return list.ToArray();
        }
        
        private static ShellChunks BuildShell(int radius)
        {
            HashSet<int3> sphere = BuildSphere(radius);
            
            return new ShellChunks
            {
                X = BuildAxisShell(sphere, new int3(1, 0, 0)),
                Y = BuildAxisShell(sphere, new int3(0, 1, 0)),
                Z = BuildAxisShell(sphere, new int3(0, 0, 1)),
            };
        }
        
        private static int3 GetAxis(int axis) => axis switch
        {
            1 => new int3(1, 0, 0),
            2 => new int3(0, 1, 0),
            _ => new int3(0, 0, 1)
        };
        
        
        // ------------------------------------------------------------------
        // SHELL UPDATE
        // ------------------------------------------------------------------
        private void UpdateChunksAroundPlayer()
        {
            Vector3Int currentPlayerChunkPos = WorldPosToChunkIndex(player.position);
            
            if(currentPlayerChunkPos == _lastChunkPlayerPos) return;
            
            int3 delta = ToInt3(currentPlayerChunkPos) - ToInt3(_lastChunkPlayerPos);
            int shift = math.abs(delta.x) + math.abs(delta.y) + math.abs(delta.z);

            if (shift > renderDistance)
            {
                FullRebuildChunks();
            }
            else
            {
                int3 center = ToInt3(_lastChunkPlayerPos);
                int3 finalPos = ToInt3(currentPlayerChunkPos);

                StepAxis(ref center, finalPos, delta.x, 1);
                StepAxis(ref center, finalPos, delta.y, 2);
                StepAxis(ref center, finalPos, delta.z, 3);
            }
            
            
            _lastChunkPlayerPos = currentPlayerChunkPos;
        }
        
        private void StepAxis(ref int3 center, int3 finalPos, int axisDelta, int axis)
        {
            if (axisDelta == 0) return;

            int sign = axisDelta > 0 ? 1 : -1;
            int count = math.abs(axisDelta);
            
            for (int i = 0; i < count; i++)
                Step(ref center, finalPos, axis, sign);
        }

        private void Step(ref int3 center, int3 finalPos, int axis, int sign)
        {
            int3[] renderChunks = _renderShell.Get(axis);
            int3[] unloadChunks = _unloadShell.Get(axis);
            int3[] lod0Chunks = _lod0Shell.Get(axis);
            
            int unloadSign = sign;
            int loadSign = -sign;
            
            UnloadChunks(unloadChunks, unloadSign, center);
            
            int3 newCenter = center + (sign * GetAxis(axis));
            
            CheckLod(lod0Chunks, unloadSign, center,    finalPos);
            CheckLod(lod0Chunks, loadSign,   newCenter, finalPos);
            
            center = newCenter;
            
            LoadChunks(renderChunks, loadSign, center);
        }

        private void UnloadChunks(int3[] offsets, int sign, int3 center)
        {
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3Int key = ToVector3Int(center + (offsets[i] * sign));

                if (_chunks.TryGetValue(key, out ChunkInfo info)
                    && info.State != ChunkState.Air
                    && info.State != ChunkState.Solid)
                {
                    UnloadChunk(key);
                }
            }
        }

        private void LoadChunks(int3[] offsets, int sign, int3 center)
        {
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3Int key = ToVector3Int(center + sign * offsets[i]);
                
                if (math.abs(key.x) > _renderLimit.x ||
                    math.abs(key.y) > _renderLimit.y ||
                    math.abs(key.z) > _renderLimit.z)
                    continue;
                
                if (_chunks.ContainsKey(key)) continue;

                _chunks.Add(key, new ChunkInfo { State = ChunkState.Generating });
                EnqueuePending(key);
            }
        }

        private void CheckLod(int3[] offsets, int sign, int3 center, int3 finalPlayerPos)
        {
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3Int key = ToVector3Int(center + sign * offsets[i]);
                
                if (!_chunks.TryGetValue(key, out ChunkInfo info))
                    continue;
                if(info.State == ChunkState.Air || info.State == ChunkState.Solid) 
                    continue;
                
                int3 delta = ToInt3(key) - finalPlayerPos;
                int distSq = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;
                LOD correctLOD = GetLodByDistanceSq(distSq);
                
                if (info.LOD != correctLOD)
                {
                    info.LOD = correctLOD;
                    info.State = ChunkState.Generating;
                    EnqueuePending(key);
                }
            }
        }
        
        
        // ------------------------------------------------------------------
        // FULL REBUILD
        // ------------------------------------------------------------------
        private void FullRebuildChunks()
        {
            Vector3Int currentPlayerChunkPos = WorldPosToChunkIndex(player.position);
            ClearDistantChunks(currentPlayerChunkPos, renderDistance + 2);
            CorrectLods(currentPlayerChunkPos);
            AddNewChunks(currentPlayerChunkPos);
        }
        private void ClearDistantChunks(Vector3Int playerChunkPos, int maxDistance)
        {
            int maxDistSq = maxDistance * maxDistance;
            _chunksToUnload.Clear();

            foreach (KeyValuePair<Vector3Int, ChunkInfo> kvp in _chunks)
            {
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

                if (_chunks.ContainsKey(chunkIndex)) continue;

                _chunks.Add(chunkIndex, new ChunkInfo() { State = ChunkState.Generating });
                EnqueuePending(chunkIndex);
            }
        }
        private void CorrectLods(Vector3Int playerChunkPos)
        {
            foreach (KeyValuePair<Vector3Int, ChunkInfo> kvp in _chunks)
            {
                if (kvp.Value.State == ChunkState.Air || kvp.Value.State == ChunkState.Solid)
                    continue;
                
                Vector3Int delta = kvp.Key - playerChunkPos;
                int distSq = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;

                LOD correctLOD = GetLodByDistanceSq(distSq);
                if (kvp.Value.LOD != correctLOD)
                {
                    kvp.Value.LOD = correctLOD;
                    kvp.Value.State = ChunkState.Generating;
                    EnqueuePending(kvp.Key);
                }
            }
        }
        
        
        
        // ------------------------------------------------------------------
        // RENDER QUEUE
        // ------------------------------------------------------------------
        private void EnqueuePending(Vector3Int chunkIndex)
        {
            if (_pendingSet.Add(chunkIndex))
                _pendingQueue.Enqueue(chunkIndex);
        }
        
        private Vector3Int DequeuePending()
        {
            Vector3Int chunkIndex = _pendingQueue.Dequeue();
            _pendingSet.Remove(chunkIndex);
            return chunkIndex;
        }
        
        private void RenderChunks()
        {
            if (_pendingQueue.Count == 0) return;
            
            Vector3Int playerChunkPos = WorldPosToChunkIndex(player.position);
            int maxDistSq = renderDistance * renderDistance;
            
            while (currentGen < maxConcurrentGen && _pendingQueue.Count > 0)
            {
                Vector3Int chunkIndex = DequeuePending();
                
                Vector3Int delta = chunkIndex - playerChunkPos;
                int distSq = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;

                if (distSq > maxDistSq)
                    UnloadChunk(chunkIndex);

                if (!_chunks.TryGetValue(chunkIndex, out ChunkInfo info))
                    continue;

                info.LOD = GetLodByDistanceSq(distSq);
                info.GenId++;
                
                _ = GenerateChunkAsync(chunkIndex, info);
                currentGen++;
            }
        }
        
        private async Awaitable GenerateChunkAsync(Vector3Int chunkIndex, ChunkInfo orgChunkInfo)
        {
            try
            {
                int genId = orgChunkInfo.GenId;
                Vector3 chunkGenPos = chunkIndex * Chunk.Size - Chunk.Offset;
                int lodStep = GetLodStep(orgChunkInfo.LOD);
                
                MeshBuildResult result = await ChunkMeshBuilder.BuildAsync(
                    chunkGenPos, genData, lodStep, orgChunkInfo.Density);

                if (!_chunks.TryGetValue(chunkIndex, out ChunkInfo chunkInfo)
                    || orgChunkInfo != chunkInfo
                    || chunkInfo.GenId != genId)
                {
                    result.Dispose();
                    return;
                }
                
                if (result.IsEmpty)
                {
                    if (chunkInfo.Chunk != null)
                    {
                        _chunkPool.Release(chunkInfo.Chunk);
                        chunkInfo.Chunk = null;
                    }

                    chunkInfo.State = result.IsSolid ? ChunkState.Solid : ChunkState.Air;
                    result.Dispose();
                    return;
                }
                
                Chunk chunk = chunkInfo.Chunk == null ? _chunkPool.Get() : chunkInfo.Chunk;
                chunk.transform.localPosition = chunkGenPos;
                chunk.gameObject.name = $"Chunk_({chunkIndex.x}_{chunkIndex.y}_{chunkIndex.z})";
                chunk.SetMesh(result.MeshData, result.Bounds);

                chunkInfo.Chunk = chunk;
                chunkInfo.State = ChunkState.Loaded;
            }
            finally
            {
                currentGen--;
            }
        }
        
        private void UnloadChunk(Vector3Int chunkIndex)
        {
            _pendingSet.Remove(chunkIndex);

            if (!_chunks.Remove(chunkIndex, out ChunkInfo info))
                return;

            info.GenId++; // unieważnia trwające w tle joby dla tego chunka
            
            if (info.Chunk != null)
            {
                _chunkPool.Release(info.Chunk);
                info.Chunk = null;
            }
        }
        
        
        
        
        
        
        // ------------------------------------------------------------------
        // HELPERS
        // ------------------------------------------------------------------
        private static int3 ToInt3(Vector3Int v) => new int3(v.x, v.y, v.z);
        private static Vector3Int ToVector3Int(int3 v) => new Vector3Int(v.x, v.y, v.z);
        
        private Vector3Int WorldPosToChunkIndex(Vector3 playerPos)
        {
            Vector3 localPos = transform.InverseTransformPoint(playerPos);

            return new Vector3Int(
                Mathf.FloorToInt((localPos.x + Chunk.Offset.x) / Chunk.Size),
                Mathf.FloorToInt((localPos.y + Chunk.Offset.y) / Chunk.Size),
                Mathf.FloorToInt((localPos.z + Chunk.Offset.z) / Chunk.Size));
        }
        
        private LOD GetLodByDistanceSq(int distanceSq) 
        {
        
            if (distanceSq <= lod0Distance * lod0Distance) return LOD.LOD0;
            return LOD.LOD1;
        }

        private int GetLodStep(LOD lod)
        {
            switch (lod)
            {
                case LOD.LOD0: return lod0Step;
                default: return lod1Step;
            }
        }
    }
}