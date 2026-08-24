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
    public int chunkCount = 0;
    public int currentgen = 0;
    
    [SerializeField] private BodyData bodyData;
    
    [Space(15), Header("Chunk Generation"), Space(5)]
    [SerializeField] private ChunkSN chunkPrefab;
    [SerializeField] private bool destroyAir = false;
    [SerializeField] private bool destroySolid = false;
    [SerializeField] private bool generateAsync = true;
    
    
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
    private readonly Dictionary<Vector3Int, ChunkSN> _loadedChunks = new Dictionary<Vector3Int, ChunkSN>();
    private readonly Dictionary<Vector3Int, NativeArray<Point>> _modifiedChunks = new Dictionary<Vector3Int, NativeArray<Point>>();
    private readonly HashSet<Vector3Int> _destroyedChunks = new HashSet<Vector3Int>();
    private readonly HashSet<Vector3Int> _generatingChunks = new HashSet<Vector3Int>();
    private BurstSimplexNoise _noise;
    private Vector3Int _lastChunkPlayerPos;
    private readonly List<Vector3Int> chunksToUnload = new List<Vector3Int>();

    private List<long> times = new List<long>();
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
        chunkCount = _loadedChunks.Count;
        currentgen = _generatingChunks.Count;
    }
    
    private void OnDestroy()
    {
        _noise.Dispose();
        Debug.Log($"avg chunk gen time: {times.Average()}");
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
            if (generateAsync) _= GenerateChunkAsync(chunkIndex);
            else GenerateChunk(chunkIndex);
            
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
    
    private void UnloadDistantChunks(Vector3Int playerChunkIndex, int maxDistance)
    {
        chunksToUnload.Clear();
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
            //if (chunk != null) 
            Destroy(chunk.gameObject);
            _loadedChunks.Remove(i);
        }
    }
    
    
    private async Awaitable GenerateChunkAsync(Vector3Int chunkIndex)
    {
        Stopwatch sw = new Stopwatch();
        sw.Start();
        
        bool disableRenderer = false;
        NativeArray<Point> densityArray = new NativeArray<Point>();
        Vector3 chunkWorldPos = chunkIndex * ChunkSN.Size - ChunkSN.Offset;
        
        if (_modifiedChunks.TryGetValue(chunkIndex, out NativeArray<Point> points))
            densityArray = points;
        
        JobHandle handle = SurfaceNetsGenerator.ScheduleChunkGeneration(
            chunkWorldPos, bodyData.type, _noise,
            ref densityArray,
            out Mesh.MeshDataArray meshDataArray,
            out NativeReference<Bounds> meshBounds);
            
        JobHandle.ScheduleBatchedJobs();
        
        while (!handle.IsCompleted)
            await Awaitable.NextFrameAsync();
        
        handle.Complete();
        
        if (meshDataArray[0].vertexCount == 0)
        {
            disableRenderer = true;
            bool isUnderground = densityArray[0].IsSolid;
            bool isAir = !isUnderground;

            if ((isAir && destroyAir) || (isUnderground && destroySolid))
            {
                _generatingChunks.Remove(chunkIndex);
                _destroyedChunks.Add(chunkIndex);
                
                if (!points.IsCreated) densityArray.Dispose();
                meshDataArray.Dispose();
                meshBounds.Dispose();
                
                return;
            }
        }
        
        ChunkSN chunk = Instantiate(chunkPrefab, chunkWorldPos, Quaternion.identity, transform);
        chunk.gameObject.name = $"Chunk_({chunkIndex.x}_{chunkIndex.y}_{chunkIndex.z})";
        chunk.Initialize();
        chunk.SetMesh(meshDataArray, meshBounds.Value, disableRenderer);
        
        if (!points.IsCreated) 
            densityArray.Dispose();
        meshBounds.Dispose();
        
        _loadedChunks.Add(chunkIndex, chunk);
        _generatingChunks.Remove(chunkIndex);
        
        sw.Stop();
        times.Add(sw.ElapsedMilliseconds);
    }
    
    
    // nie aktualizowana na bieżąco
    private void GenerateChunk(Vector3Int chunkIndex)
    {
        Stopwatch sw = new Stopwatch();
        sw.Start();

        bool disableRenderer = false;
        NativeArray<Point> densityArray = new NativeArray<Point>();
        Vector3 chunkWorldPos = chunkIndex * ChunkSN.Size - ChunkSN.Offset;
        
        if (_modifiedChunks.TryGetValue(chunkIndex, out NativeArray<Point> points))
            densityArray = points;
        
        JobHandle handle = SurfaceNetsGenerator.ScheduleChunkGeneration(
            chunkWorldPos, bodyData.type, _noise,
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
                _destroyedChunks.Add(chunkIndex);
                _generatingChunks.Remove(chunkIndex);
                
                if (!points.IsCreated) densityArray.Dispose();
                meshDataArray.Dispose();
                meshBounds.Dispose();
                
                return;
            }
        }
        
        ChunkSN chunk = Instantiate(chunkPrefab, chunkWorldPos, Quaternion.identity, transform);
        chunk.gameObject.name = $"Chunk_({chunkIndex.x}_{chunkIndex.y}_{chunkIndex.z})";
        chunk.Initialize();
        chunk.SetMesh(meshDataArray, meshBounds.Value, disableRenderer);
        
        if (!points.IsCreated) 
            densityArray.Dispose();
        meshBounds.Dispose();
        _loadedChunks.Add(chunkIndex, chunk);
        _generatingChunks.Remove(chunkIndex);
        
        sw.Stop();
        times.Add(sw.ElapsedMilliseconds);
    }
}