using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace _Project.SurfaceNets.Generator
{ 
    [BurstCompile]
    public struct MeshJob : IJob
    {
        [ReadOnly] public NativeArray<Point> Density;
        public int DensitySize;
        public int VoxelSize;
        public int ChunkSize;
        
        //Output
        public NativeList<float3> Vertices;
        public NativeList<int> Triangles;
        
        //Temp
        [DeallocateOnJobCompletion]
        public NativeArray<short> VoxelVertexIndices;
        
        public void Execute()
        {
            // kroki dla Density
            int dStrideX = DensitySize * DensitySize;
            int dStrideY = DensitySize;
            int dStrideZ = 1;
            
            // kroki dla VoxelVertexIndices
            int vStrideX = VoxelSize * VoxelSize;
            int vStrideY = VoxelSize;
            int vStrideZ = 1;

            // =================  zmiana offsetow z 3D do 1D  ===================
            NativeArray<int> cornerOffsets1D = new NativeArray<int>(8, Allocator.Temp);
            for (int i = 0; i < 8; i++)
            {
                int3 offset = SurfaceNetsTables.CornerOffsets[i];
                cornerOffsets1D[i] = offset.x * dStrideX + offset.y * dStrideY + offset.z * dStrideZ;
            }

            // =================  CalculateVertices  ===================
            for (int i = 0; i < VoxelVertexIndices.Length; i++)
            {
                VoxelVertexIndices[i] = -1;
            }
            
            int voxel1DIndex = 0;
            for (int x = 0; x < VoxelSize; x++)
            for (int y = 0; y < VoxelSize; y++)
            for (int z = 0; z < VoxelSize; z++)
            {
                int densityIndex1D = x * dStrideX + y * dStrideY + z * dStrideZ;
                
                int cornerMask = 0;
                for (int i = 0; i < 8; i++)
                {
                    int index = densityIndex1D + cornerOffsets1D[i];
                    if (Density[index].IsSolid) cornerMask |= (1 << i);
                }
                if (cornerMask == 0 || cornerMask == 255)
                {
                    voxel1DIndex++;
                    continue;
                }
                
                float3 vertexSum = float3.zero;
                int cuts = 0;
                for (int i = 0; i < 12; i++)
                {
                    int2 edge = SurfaceNetsTables.EdgeCorners[i];
                    int corner1 = edge.x;
                    int corner2 = edge.y;
                    
                    bool isSolid1 = (cornerMask & (1 << corner1)) != 0;
                    bool isSolid2 = (cornerMask & (1 << corner2)) != 0;
                    
                    if (isSolid1 != isSolid2)
                    {
                        int index1 = densityIndex1D + cornerOffsets1D[corner1];
                        int index2 = densityIndex1D + cornerOffsets1D[corner2];
                        float dens1 = Density[index1].Density;
                        float dens2 = Density[index2].Density;
                        
                        float t = -dens1 / (dens2 - dens1);

                        float3 point1 = new float3(
                            x + SurfaceNetsTables.CornerOffsets[corner1].x,
                            y + SurfaceNetsTables.CornerOffsets[corner1].y,
                            z + SurfaceNetsTables.CornerOffsets[corner1].z
                        );
                        float3 point2 = new float3(
                            x + SurfaceNetsTables.CornerOffsets[corner2].x,
                            y + SurfaceNetsTables.CornerOffsets[corner2].y,
                            z + SurfaceNetsTables.CornerOffsets[corner2].z
                        );
                        vertexSum += math.lerp(point1, point2, t);
                        
                        cuts++;
                    }
                }

                if (cuts > 0)
                {
                    float3 finalPos = vertexSum / cuts;
                    Vertices.Add(finalPos);
                    VoxelVertexIndices[voxel1DIndex] = (short)(Vertices.Length - 1);
                }
                
                voxel1DIndex++;
            }
            
            
            // =================  GenerateTriangles  ===================
            for (int x = 0; x <= ChunkSize; x++)
            for (int y = 0; y <= ChunkSize; y++)
            for (int z = 0; z <= ChunkSize; z++)
            {
                int densityIndex1D = x * dStrideX + y * dStrideY + z * dStrideZ;
                
                // krawędzie wzdłuż osi X
                if (x < ChunkSize && y > 0 && z > 0)
                {
                    int i1 = densityIndex1D;
                    int i2 = densityIndex1D + dStrideX;
                    
                    bool s1 = Density[i1].IsSolid;
                    bool s2 = Density[i2].IsSolid;
                    
                    if (s1 != s2)
                    {
                        
                        int c0 = VoxelVertexIndices[(x * vStrideX) + ((y - 1) * vStrideY) + ((z - 1) * vStrideZ)];
                        int c1 = VoxelVertexIndices[(x * vStrideX) + ( y *      vStrideY) + ((z - 1) * vStrideZ)];
                        int c2 = VoxelVertexIndices[(x * vStrideX) + ( y *      vStrideY) + ( z *      vStrideZ)];
                        int c3 = VoxelVertexIndices[(x * vStrideX) + ((y - 1) * vStrideY) + ( z *      vStrideZ)];
                        AddQuad(c0, c1, c2, c3, !s1);
                    }
                }
                
                // krawędzie wzdłuż osi Y
                if (y < ChunkSize && x > 0 && z > 0)
                {
                    int i1 = densityIndex1D;
                    int i2 = densityIndex1D + dStrideY;
                    
                    bool s1 = Density[i1].IsSolid;
                    bool s2 = Density[i2].IsSolid;
                    
                    if (s1 != s2)
                    {
                        int c0 = VoxelVertexIndices[((x - 1) * vStrideX) + (y * vStrideY) + ((z - 1) * vStrideZ)];
                        int c1 = VoxelVertexIndices[( x *      vStrideX) + (y * vStrideY) + ((z - 1) * vStrideZ)];
                        int c2 = VoxelVertexIndices[( x *      vStrideX) + (y * vStrideY) + ( z *      vStrideZ)];
                        int c3 = VoxelVertexIndices[((x - 1) * vStrideX) + (y * vStrideY) + ( z *      vStrideZ)];
                        AddQuad(c0, c1, c2, c3, s1);
                    }
                }
                
                // krawędzie wzdłuż osi Z
                if (z < ChunkSize && x > 0 && y > 0)
                {
                    int i1 = densityIndex1D;
                    int i2 = densityIndex1D + dStrideZ;
                    
                    bool s1 = Density[i1].IsSolid;
                    bool s2 = Density[i2].IsSolid;
                    
                    if (s1 != s2)
                    {
                        int c0 = VoxelVertexIndices[((x - 1) * vStrideX) + ((y - 1) * vStrideY) + (z * vStrideZ)];
                        int c1 = VoxelVertexIndices[( x *      vStrideX) + ((y - 1) * vStrideY) + (z * vStrideZ)];
                        int c2 = VoxelVertexIndices[( x *      vStrideX) + ( y *      vStrideY) + (z * vStrideZ)];
                        int c3 = VoxelVertexIndices[((x - 1) * vStrideX) + ( y *      vStrideY) + (z * vStrideZ)];
                        AddQuad(c0, c1, c2, c3, !s1);
                    }
                }
            }
            
            
            // =========== Usuwanie nieużywanych wierzchołków ===========
            if (Triangles.Length > 0 && Vertices.Length > 0)
            {
                NativeArray<int> remap = new NativeArray<int>(Vertices.Length, Allocator.Temp);
                for (int i = 0; i < remap.Length; i++) remap[i] = -1;
                
                for (int i = 0; i < Triangles.Length; i++)
                {
                    remap[Triangles[i]] = 1;
                }
                
                NativeList<float3> compactVertices = new NativeList<float3>(Vertices.Length, Allocator.Temp);
                int newIndex = 0;
                for (int i = 0; i < Vertices.Length; i++)
                {
                    if (remap[i] != -1)
                    {
                        remap[i] = newIndex++;
                        compactVertices.Add(Vertices[i]);
                    }
                }
                
                for (int i = 0; i < Triangles.Length; i++)
                {
                    Triangles[i] = remap[Triangles[i]];
                }
                
                Vertices.Clear();
                Vertices.AddRange(compactVertices.AsArray());
            }
            
            
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddQuad(int c0, int c1, int c2, int c3, bool flipNormal)
        {
            if (c0 == -1 || c1 == -1 || c2 == -1 || c3 == -1) return;

            if (flipNormal)
            {
                Triangles.Add(c0); Triangles.Add(c2); Triangles.Add(c1);
                Triangles.Add(c0); Triangles.Add(c3); Triangles.Add(c2);
            }
            else
            {
                Triangles.Add(c0); Triangles.Add(c1); Triangles.Add(c2);
                Triangles.Add(c0); Triangles.Add(c2); Triangles.Add(c3);
            }
        }
    }
}