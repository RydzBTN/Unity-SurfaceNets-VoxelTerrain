using System;
using System.Collections.Generic;
using _Project.SurfaceNets.Generator;
using _Project.SurfaceNets.JobHelpers;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public static class SurfaceNetsGenerator
{
    public static JobHandle ScheduleChunkGeneration(
        Vector3 chunkWorldPos,
        BodyType type,
        BurstSimplexNoise noise,
        ref NativeArray<Point> densities,
        out NativeList<float3> vertices,
        out NativeList<int> triangles)
    {
        int densityCount = ChunkSN.DensityArraySize * ChunkSN.DensityArraySize * ChunkSN.DensityArraySize;
        int totalVoxels = ChunkSN.VoxelArraySize * ChunkSN.VoxelArraySize * ChunkSN.VoxelArraySize;
        vertices = new NativeList<float3>(1000,Allocator.Persistent);
        triangles = new NativeList<int>(5000,Allocator.Persistent);
        NativeArray<short> voxelIndices = new NativeArray<short>(totalVoxels, Allocator.TempJob);
        
        JobHandle densityHandle = new JobHandle();
        if (!densities.IsCreated)
        {
            densities = new NativeArray<Point>(densityCount, Allocator.Persistent);
        
            DensityJob densityJob = new DensityJob
            {
                ChunkWorldPos = chunkWorldPos,
                DensityArraySize = ChunkSN.DensityArraySize,
                BodyType = (int)type,
                Noise = noise,
                Densities = densities
            };
            densityHandle = densityJob.Schedule(densityCount, 64);
            JobHandle.ScheduleBatchedJobs();
        }
        
        
        MeshJob meshJob = new MeshJob
        {
            Density = densities,
            
            DensitySize = ChunkSN.DensityArraySize,
            VoxelSize = ChunkSN.VoxelArraySize,
            ChunkSize = ChunkSN.Size,
            
            Vertices = vertices,
            Triangles = triangles,
            VoxelVertexIndices = voxelIndices
        };
        
        JobHandle handle = meshJob.Schedule(densityHandle);
        
        return handle;
    }
    
    public static (bool isAir, bool isUnder) CheckIsSurface(NativeArray<Point> densities)
    {
        bool hasSolid = false;
        bool hasAir = false;
        
        for (int i = 0; i < densities.Length; i++)
        {
            if (densities[i].IsSolid) hasSolid = true;
            else hasAir = true;
            
            if (hasSolid && hasAir)
            {
                return (false, false);
            }
        }

        return (!hasSolid, !hasAir);
    }

    #region C# MESH GENERATION (DEPRECATED)
    private static void CalculateVertices(NativeArray<Point> densityArray, out Vector3[] vertices, out Vector3[] normals, out short[,,] voxelVertexIndices)
    {
        List<Vector3> verticesList = new List<Vector3>();

        voxelVertexIndices =
            new short[ChunkSN.VoxelArraySize, ChunkSN.VoxelArraySize,
                ChunkSN.VoxelArraySize]; // Tablica indeksów wierzchołków dla każdego voxela (-1 = brak wierzchołka)

        for (int x = 0; x < ChunkSN.VoxelArraySize; x++)
        for (int y = 0; y < ChunkSN.VoxelArraySize; y++)
        for (int z = 0; z < ChunkSN.VoxelArraySize; z++)
        {
            voxelVertexIndices[x, y, z] = -1;

            int cornerMask = 0;
            for (int i = 0; i < 8; i++)
            {
                int3 cornerOffset = SurfaceNetsTables.CornerOffsets[i];
                int index = Grid3D.ToIndex(x + cornerOffset.x, y + cornerOffset.y, z + cornerOffset.z,
                    ChunkSN.DensityArraySize);
                // oznaczamy to jako 1 na masce 0000 0000
                if (densityArray[index].IsSolid)
                    cornerMask |= (1 << i);
            }

            if (cornerMask == 0 || cornerMask == 255) continue;

            Vector3 vertexSum = Vector3.zero;
            sbyte cuts = 0;
            for (int i = 0; i < 12; i++)
            {
                int2 edge = SurfaceNetsTables.EdgeCorners[i];
                int corner1 = edge.x;
                int corner2 = edge.y;

                bool isSolid1 = (cornerMask & (1 << corner1)) != 0;
                bool isSolid2 = (cornerMask & (1 << corner2)) != 0;

                // jeżeli jedna krawędź jest w terenie a druga nie to przechodzi przez nie powierzchnia
                if (isSolid1 != isSolid2)
                {
                    Vector3Int point1 = new Vector3Int(
                        x + SurfaceNetsTables.CornerOffsets[corner1].x,
                        y + SurfaceNetsTables.CornerOffsets[corner1].y,
                        z + SurfaceNetsTables.CornerOffsets[corner1].z
                    );
                    Vector3Int point2 = new Vector3Int(
                        x + SurfaceNetsTables.CornerOffsets[corner2].x,
                        y + SurfaceNetsTables.CornerOffsets[corner2].y,
                        z + SurfaceNetsTables.CornerOffsets[corner2].z
                    );
                    int index1 = Grid3D.ToIndex(point1.x, point1.y, point1.z, ChunkSN.DensityArraySize);
                    int index2 = Grid3D.ToIndex(point2.x, point2.y, point2.z, ChunkSN.DensityArraySize);

                    float dens1 = densityArray[index1].Density;
                    float dens2 = densityArray[index2].Density;

                    float t = -dens1 / (dens2 - dens1);
                    vertexSum += Vector3.Lerp(point1, point2, t);

                    cuts++;
                }
            }

            if (cuts <= 0) continue;

            Vector3 finalPos = vertexSum / cuts; //+ Offset;
            verticesList.Add(finalPos);

            voxelVertexIndices[x, y, z] = (short)(verticesList.Count - 1);
        }

        vertices = verticesList.ToArray();
        normals = Array.Empty<Vector3>();
    }
    private static void GenerateTriangles(NativeArray<Point> densityArray, short[,,] voxelVertexIndices, out int[] triangles)
    {
        List<int> trianglesList = new List<int>();

        for (int x = 0; x <= ChunkSN.Size; x++)
        for (int y = 0; y <= ChunkSN.Size; y++)
        for (int z = 0; z <= ChunkSN.Size; z++)
        {
            // krawędzie wzdłuż osi X
            if (x < ChunkSN.Size && y > 0 && z > 0)
            {
                int i1 = Grid3D.ToIndex(x, y, z, ChunkSN.DensityArraySize);
                int i2 = Grid3D.ToIndex(x + 1, y, z, ChunkSN.DensityArraySize);

                bool s1 = densityArray[i1].IsSolid;
                bool s2 = densityArray[i2].IsSolid;
                if (s1 != s2)
                {
                    int c0 = voxelVertexIndices[x, y - 1, z - 1];
                    int c1 = voxelVertexIndices[x, y, z - 1];
                    int c2 = voxelVertexIndices[x, y, z];
                    int c3 = voxelVertexIndices[x, y - 1, z];
                    AddQuad(c0, c1, c2, c3, !s1, trianglesList);
                }
            }

            // krawędzie wzdłuż osi Y
            if (y < ChunkSN.Size && x > 0 && z > 0)
            {
                int i1 = Grid3D.ToIndex(x, y, z, ChunkSN.DensityArraySize);
                int i2 = Grid3D.ToIndex(x, y + 1, z, ChunkSN.DensityArraySize);

                bool s1 = densityArray[i1].IsSolid;
                bool s2 = densityArray[i2].IsSolid;
                if (s1 != s2)
                {
                    int c0 = voxelVertexIndices[x - 1, y, z - 1];
                    int c1 = voxelVertexIndices[x, y, z - 1];
                    int c2 = voxelVertexIndices[x, y, z];
                    int c3 = voxelVertexIndices[x - 1, y, z];
                    AddQuad(c0, c1, c2, c3, s1, trianglesList);
                }
            }

            // krawędzie wzdłuż osi Z
            if (z < ChunkSN.Size && x > 0 && y > 0)
            {
                int i1 = Grid3D.ToIndex(x, y, z, ChunkSN.DensityArraySize);
                int i2 = Grid3D.ToIndex(x, y, z + 1, ChunkSN.DensityArraySize);

                bool s1 = densityArray[i1].IsSolid;
                bool s2 = densityArray[i2].IsSolid;
                if (s1 != s2)
                {
                    int c0 = voxelVertexIndices[x - 1, y - 1, z];
                    int c1 = voxelVertexIndices[x, y - 1, z];
                    int c2 = voxelVertexIndices[x, y, z];
                    int c3 = voxelVertexIndices[x - 1, y, z];
                    AddQuad(c0, c1, c2, c3, !s1, trianglesList);
                }
            }
        }

        triangles = trianglesList.ToArray();
    }
    private static void AddQuad(int c0, int c1, int c2, int c3, bool flipNormal, List<int> triangles)
    {
        // Jeśli któryś sześcian nie zawierał wierzchołka -> nie twórz ścianki
        if (c0 == -1 || c1 == -1 || c2 == -1 || c3 == -1) return;

        if (flipNormal)
        {
            triangles.Add(c0);
            triangles.Add(c2);
            triangles.Add(c1);
            triangles.Add(c0);
            triangles.Add(c3);
            triangles.Add(c2);
        }
        else
        {
            triangles.Add(c0);
            triangles.Add(c1);
            triangles.Add(c2);
            triangles.Add(c0);
            triangles.Add(c2);
            triangles.Add(c3);
        }
    }
    #endregion
}












