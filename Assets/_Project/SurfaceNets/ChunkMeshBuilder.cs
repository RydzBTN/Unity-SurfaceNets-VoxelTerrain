using _Project.SurfaceNets.Data;
using _Project.SurfaceNets.Generator;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace _Project.SurfaceNets
{
    public struct MeshBuildResult
    {
        public Mesh.MeshDataArray MeshData;
        public Bounds Bounds;
        public bool IsEmpty;
        public bool IsSolid;

        public void Dispose()
        {
            if (MeshData.Length > 0) MeshData.Dispose();
        }
    }
    
    public static class ChunkMeshBuilder
    {
        public static async Awaitable<MeshBuildResult> BuildAsync(
            Vector3 chunkGenPos,
            GeneratorData genData,
            int lodStep,
            Point[] density)
        {
            NativeArray<Point> densityArray = density != null
                ? new NativeArray<Point>(density, Allocator.TempJob)
                : new NativeArray<Point>();

            JobHandle handle = SurfaceNetsGenerator.ScheduleChunkMeshGeneration(
                chunkGenPos, genData.type, genData.Noise, lodStep,
                ref densityArray,
                out Mesh.MeshDataArray meshDataArray,
                out NativeReference<Bounds> meshBounds);

            JobHandle.ScheduleBatchedJobs();

            while (!handle.IsCompleted)
                await Awaitable.NextFrameAsync();
            handle.Complete();

            MeshBuildResult result = new MeshBuildResult
            {
                MeshData = meshDataArray,
                Bounds = meshBounds.Value,
                IsEmpty = meshDataArray[0].vertexCount == 0,
                IsSolid = densityArray.IsCreated && densityArray[0].IsSolid
            };

            if (densityArray.IsCreated)
                densityArray.Dispose();

            meshBounds.Dispose();

            return result;
        }
    }
}