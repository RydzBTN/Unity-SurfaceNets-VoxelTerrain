using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace _Project.SurfaceNets.Generator.Jobs
{
    [StructLayout(LayoutKind.Sequential)]
    public struct VertexLayout
    {
        public float3 Position;
        public float3 Normal;
    }
    
    [BurstCompile]
    public struct MeshJob : IJob
    {
        [ReadOnly] public NativeArray<Point> Density;
        public int DensitySize;
        public int VoxelSize;
        public int ChunkSize;
        public int LodStep;
        
        // Output
        public Mesh.MeshData OutputMeshData;
        public NativeReference<Bounds> OutputBounds;
        
        // Temp
        public NativeList<float3> Vertices;
        public NativeList<float3> Normals;
        public NativeList<int> Triangles;
        public NativeArray<int> VoxelVertexIndices;
        
        public void Execute()
        {
            int dStrideX = DensitySize * DensitySize;
            int dStrideY = DensitySize;
            int dStrideZ = 1;
            
            int vStrideX = VoxelSize * VoxelSize;
            int vStrideY = VoxelSize;
            int vStrideZ = 1;

            // =================  Zmiana offsetów z 3D do 1D  ===================
            NativeArray<int> cornerOffsets1D = new NativeArray<int>(8, Allocator.Temp);
            for (int i = 0; i < 8; i++)
            {
                int3 offset = SurfaceNetsTables.CornerOffsets[i];
                cornerOffsets1D[i] = offset.x * dStrideX + offset.y * dStrideY + offset.z * dStrideZ;
            }
            
            // =================  CalculateVertices & Normals  ===================
            for (int i = 0; i < VoxelVertexIndices.Length; i++)
            {
                VoxelVertexIndices[i] = -1;
            }
            
            int voxel1DIndex = 0;
            for (int x = 0; x < VoxelSize; x++)
            for (int y = 0; y < VoxelSize; y++)
            for (int z = 0; z < VoxelSize; z++)
            {
                int densityIndex1D = (x + 1) * dStrideX + (y + 1) * dStrideY + (z + 1) * dStrideZ; // 1 woksel marginesu
                
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
                    
                    float3 g000 = GetCentralGradient(densityIndex1D + cornerOffsets1D[0], dStrideX, dStrideY, dStrideZ);
                    float3 g100 = GetCentralGradient(densityIndex1D + cornerOffsets1D[1], dStrideX, dStrideY, dStrideZ);
                    float3 g010 = GetCentralGradient(densityIndex1D + cornerOffsets1D[2], dStrideX, dStrideY, dStrideZ);
                    float3 g110 = GetCentralGradient(densityIndex1D + cornerOffsets1D[3], dStrideX, dStrideY, dStrideZ);
                    float3 g001 = GetCentralGradient(densityIndex1D + cornerOffsets1D[4], dStrideX, dStrideY, dStrideZ);
                    float3 g101 = GetCentralGradient(densityIndex1D + cornerOffsets1D[5], dStrideX, dStrideY, dStrideZ);
                    float3 g011 = GetCentralGradient(densityIndex1D + cornerOffsets1D[6], dStrideX, dStrideY, dStrideZ);
                    float3 g111 = GetCentralGradient(densityIndex1D + cornerOffsets1D[7], dStrideX, dStrideY, dStrideZ);
                    
                    float u = finalPos.x - x;
                    float v = finalPos.y - y;
                    float w = finalPos.z - z;
            
                    // interpolacja gradientu do pozycji wierzchołka
                    float3 g00 = math.lerp(g000, g100, u);
                    float3 g10 = math.lerp(g010, g110, u);
                    float3 g01 = math.lerp(g001, g101, u);
                    float3 g11 = math.lerp(g011, g111, u);
            
                    float3 g0 = math.lerp(g00, g10, v);
                    float3 g1 = math.lerp(g01, g11, v);
            
                    float3 normalVec = math.lerp(g0, g1, w);

                    float lenSq = math.lengthsq(normalVec);
                    float3 finalNormal = lenSq > 1e-6f ? normalVec * math.rsqrt(lenSq) : new float3(0, 1, 0);
                    
                    Vertices.Add(finalPos * LodStep);
                    Normals.Add(finalNormal);
                    VoxelVertexIndices[voxel1DIndex] = (Vertices.Length - 1);
                }
                
                voxel1DIndex++;
            }
            
            cornerOffsets1D.Dispose();
            
            // =================  GenerateTriangles  ===================
            for (int x = 0; x <= ChunkSize; x++)
            for (int y = 0; y <= ChunkSize; y++)
            for (int z = 0; z <= ChunkSize; z++)
            {
                int densityIndex1D = (x + 1) * dStrideX + (y + 1) * dStrideY + (z + 1) * dStrideZ; // 1 woksel marginesu
                
                // Krawędzie wzdłuż osi X
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
                
                // Krawędzie wzdłuż osi Y
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
                
                // Krawędzie wzdłuż osi Z
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
                NativeList<float3> compactNormals = new NativeList<float3>(Normals.Length, Allocator.Temp);
                
                int newIndex = 0;
                for (int i = 0; i < Vertices.Length; i++)
                {
                    if (remap[i] != -1)
                    {
                        remap[i] = newIndex++;
                        compactVertices.Add(Vertices[i]);
                        compactNormals.Add(Normals[i]);
                    }
                }
                
                for (int i = 0; i < Triangles.Length; i++)
                {
                    Triangles[i] = remap[Triangles[i]];
                }
                
                Vertices.Clear();
                Vertices.AddRange(compactVertices.AsArray());
                
                Normals.Clear();
                Normals.AddRange(compactNormals.AsArray());
                
                remap.Dispose();
                compactVertices.Dispose();
                compactNormals.Dispose();
            }
            
            // =================  Zapis do MeshData  ===================
            if (Vertices.Length == 0 || Triangles.Length == 0)
            {
                var emptyDesc = new NativeArray<VertexAttributeDescriptor>(0, Allocator.Temp);
                OutputMeshData.SetVertexBufferParams(0, emptyDesc);
                emptyDesc.Dispose();
                
                OutputMeshData.SetIndexBufferParams(0, IndexFormat.UInt32);
                OutputMeshData.subMeshCount = 1;
                OutputMeshData.SetSubMesh(0, new SubMeshDescriptor(0, 0));
                return;
            }
            
            var vertexAttributes = new NativeArray<VertexAttributeDescriptor>(2, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            vertexAttributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position);
            vertexAttributes[1] = new VertexAttributeDescriptor(VertexAttribute.Normal);

            OutputMeshData.SetVertexBufferParams(Vertices.Length, vertexAttributes);
            vertexAttributes.Dispose();
            OutputMeshData.SetIndexBufferParams(Triangles.Length, IndexFormat.UInt32);

            NativeArray<VertexLayout> outVertices = OutputMeshData.GetVertexData<VertexLayout>();
            NativeArray<int> outIndices = OutputMeshData.GetIndexData<int>();
            outIndices.CopyFrom(Triangles.AsArray());
            
            float3 min = new float3(float.MaxValue);
            float3 max = new float3(float.MinValue);
            for (int i = 0; i < Vertices.Length; i++)
            {
                float3 pos = Vertices[i];
                min = math.min(min, pos);
                max = math.max(max, pos);

                outVertices[i] = new VertexLayout
                {
                    Position = pos,
                    Normal = Normals[i]
                };
            }
            
            float3 size = max - min;
            float3 center = min + (size * 0.5f);
            OutputBounds.Value = new Bounds(center, size);

            OutputMeshData.subMeshCount = 1;
            OutputMeshData.SetSubMesh(0, new SubMeshDescriptor(0, Triangles.Length), MeshUpdateFlags.DontRecalculateBounds);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float3 GetCentralGradient(int idx, int sX, int sY, int sZ)
        {
            float dx = Density[idx + sX].Density - Density[idx - sX].Density;
            float dy = Density[idx + sY].Density - Density[idx - sY].Density;
            float dz = Density[idx + sZ].Density - Density[idx - sZ].Density;
            return new float3(dx, dy, dz);
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