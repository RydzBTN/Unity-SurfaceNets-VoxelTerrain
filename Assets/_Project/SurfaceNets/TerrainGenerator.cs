using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class TerrainGenerator : MonoBehaviour
{
    #region CONSTANTS AND CONFIGURATION
    [Header("World Gen")]
    [SerializeField] private BodyData bodyData;
    
    [Header("Chunk Generation"), Space(15)]
    [SerializeField] private ChunkSN chunkPrefab;
    [SerializeField] private int maxConcurrentGen = 64;
    [SerializeField] private bool destroyAir = false;
    [SerializeField] private bool destroySolid = false;

    [Header("Dynamic Render Distance")]
    [SerializeField] private Transform player;
    [SerializeField] private int renderDistance = 8;
    
    [Header("LOD")]
    [SerializeField] private int lod0Step = 1;
    private const int lod0Distance = 2;
    [SerializeField] private int lod1Step = 2;
    private const int lod1Distance = 6;
    [SerializeField] private int lod2Step = 4;
    
    private static Vector3Int renderLimit;
    #endregion
    
    #region FIELDS
        #region GENERATION
        private readonly Dictionary<Vector3Int, ChunkSN> _loadedChunks = new Dictionary<Vector3Int, ChunkSN>();
        private readonly Dictionary<Vector3Int, NativeArray<Point>> _modifiedChunks = new Dictionary<Vector3Int, NativeArray<Point>>();
        private readonly HashSet<Vector3Int> _destroyedChunks = new HashSet<Vector3Int>();
        private readonly Queue<Vector3Int> _generateQueue = new Queue<Vector3Int>();
        private readonly HashSet<Vector3Int> _generatingChunks = new HashSet<Vector3Int>();
        private int _activeGenerations = 0;
        #endregion
    
    private BurstSimplexNoise _noise;
    private Vector3Int _lastChunkPlayerPos;
    private List<int> genTimes = new List<int>();
    #endregion

    #region UNITY
    private void Awake()
    {
        _noise = new BurstSimplexNoise(bodyData.seed, Allocator.Persistent);
        
        int limit = CelestialBodyGenerator.GetSuggestedChunkRadius(bodyData.type, ChunkSN.Size);
        renderLimit = new Vector3Int(limit, limit, limit);
    }
    
    private void Update()
    {
        UpdateChunksAroundPlayer();
        ProcessGenerationQueue();
    }

    private void OnDestroy()
    {
        _noise.Dispose();
        
        Debug.Log($"average genTime: {genTimes.Average()} ms");
    }
    
    #endregion
    
    private void UpdateChunksAroundPlayer()
    {
        // Odległość między dwoma punktami:
        // float distance = sqrt((x2-x1)² + (y2-y1)² + (z2-z1)²)
        
        Vector3Int currentChunkPlayerPos = WorldPosToChunkIndex(player.position);
        if(currentChunkPlayerPos == _lastChunkPlayerPos) return;
        _lastChunkPlayerPos = currentChunkPlayerPos;
        
        int renderDistSq = renderDistance * renderDistance;
        
        for (int x = -renderDistance; x <= renderDistance; x++)
        for (int y = -renderDistance; y <= renderDistance; y++)
        for (int z = -renderDistance; z <= renderDistance; z++)
        {
            int distSq = x*x + y*y + z*z;
            if (distSq > renderDistSq) continue; // poza kulą
            
            Vector3Int chunkIndex = currentChunkPlayerPos + new Vector3Int(x, y, z);
           
            if(math.abs(chunkIndex.x) > renderLimit.x || math.abs(chunkIndex.y) > renderLimit.y || math.abs(chunkIndex.z) > renderLimit.z)
                continue; // poza mapą
            
            if (_loadedChunks.ContainsKey(chunkIndex) || _destroyedChunks.Contains(chunkIndex) || _generatingChunks.Contains(chunkIndex)) 
                continue; // już załadowany, zniszczony lub w trakcie generowania

            int lodStep = GetLodStep(distSq);
            
            _generatingChunks.Add(chunkIndex);
            _generateQueue.Enqueue(chunkIndex);
            
        }
        UnloadDistantChunks(currentChunkPlayerPos, renderDistance + 4);
    }
    
    private Vector3Int WorldPosToChunkIndex(Vector3 playerPos)
    {
        return new Vector3Int(
            Mathf.FloorToInt((playerPos.x + ChunkSN.Offset.x) / ChunkSN.Size),
            Mathf.FloorToInt((playerPos.y + ChunkSN.Offset.y) / ChunkSN.Size),
            Mathf.FloorToInt((playerPos.z + ChunkSN.Offset.z) / ChunkSN.Size));
    }
    
    private int GetLodStep(int distanceSq)
    {
        if (distanceSq <= 4) return lod0Step;
        if (distanceSq <= 36) return lod1Step;
        return lod2Step;
    }
    
    // todo sprawdzić InvokeRepeating(nameof(CleanupDistantDestroyedChunks), 300f, 300f);
    private void UnloadDistantChunks(Vector3Int playerChunkIndex, int maxDistance)
    {
        List<Vector3Int> chunksToUnload = new List<Vector3Int>();
        int maxDistSq = maxDistance * maxDistance;

        foreach (KeyValuePair<Vector3Int,ChunkSN> kvp in _loadedChunks)
        {
            Vector3Int delta = kvp.Key - playerChunkIndex;
            int distSq = delta.x*delta.x + delta.y*delta.y + delta.z*delta.z;

            if (distSq > maxDistSq) chunksToUnload.Add(kvp.Key);
        }
        
        foreach (Vector3Int i in chunksToUnload)
        {
            ChunkSN chunk = _loadedChunks[i];
            if (chunk != null) Destroy(chunk.gameObject);
            _loadedChunks.Remove(i);
        }
    }

    
    
    private void ProcessGenerationQueue()
    {
        while (_activeGenerations < maxConcurrentGen && _generateQueue.Count > 0)
        {
            Vector3Int chunkIndex = _generateQueue.Dequeue();
            
            StartCoroutine(GenerateChunk(chunkIndex));
            _activeGenerations++;
        }
    }
    
    private IEnumerator GenerateChunk(Vector3Int chunkIndex)
    {
        Stopwatch sw = new Stopwatch();
        sw.Start();
        
        NativeArray<Point> densityArray;
        Vector3 chunkWorldPos = chunkIndex * ChunkSN.Size - ChunkSN.Offset;
        
        if (_modifiedChunks.TryGetValue(chunkIndex, out NativeArray<Point> points))
        {
            densityArray = points;
        }
        else
        {
            JobHandle handleDensity = SurfaceNetsGenerator.ScheduleDensityJob(
                chunkWorldPos, bodyData.type, _noise, out densityArray);
            
            while (!handleDensity.IsCompleted)
                yield return null;
            handleDensity.Complete();
            
            var (isAir, isUnderground) = SurfaceNetsGenerator.CheckIsSurface(densityArray);
            if ((isAir && destroyAir) || (isUnderground && destroySolid))
            {
                _generatingChunks.Remove(chunkIndex);
                _activeGenerations--;
                _destroyedChunks.Add(chunkIndex);
                    
                densityArray.Dispose(); 
                yield break;
            }
        }
        
        
        ChunkSN chunk = Instantiate(chunkPrefab, chunkWorldPos, Quaternion.identity, transform);
        chunk.gameObject.name = $"Chunk_({chunkIndex.x}_{chunkIndex.y}_{chunkIndex.z})";
        chunk.Initialize(bodyData.type, _noise);
        
        
        yield return StartCoroutine(chunk.GenerateMeshCoroutine(densityArray));
        if(!_modifiedChunks.ContainsKey(chunkIndex)) densityArray.Dispose();
        
        sw.Stop();
        genTimes.Add((int)sw.ElapsedMilliseconds);
        
        _loadedChunks.Add(chunkIndex, chunk);
        _generatingChunks.Remove(chunkIndex);
        _activeGenerations--;
    }
}