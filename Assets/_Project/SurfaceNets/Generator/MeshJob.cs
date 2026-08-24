using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace _Project.SurfaceNets.Generator
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
        
        //Output
        public Mesh.MeshData OutputMeshData;
        public NativeList<float3> Vertices;
        public NativeList<int> Triangles;
        public NativeReference<Bounds> OutputBounds;
        
        //Temp
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
            
            
            // =================  Zapis do MeshData  ===================
            if (Vertices.Length == 0 || Triangles.Length == 0)
            {
                // Pusty chunk (np. samo powietrze lub sama lita skała)
                var emptyDesc = new NativeArray<VertexAttributeDescriptor>(0, Allocator.Temp);
                OutputMeshData.SetVertexBufferParams(0, emptyDesc);
                emptyDesc.Dispose();
                
                OutputMeshData.SetIndexBufferParams(0, IndexFormat.UInt32);
                
                OutputMeshData.subMeshCount = 1;
                OutputMeshData.SetSubMesh(0, new SubMeshDescriptor(0, 0, MeshTopology.Triangles));
                return;
            }
            
             // definicja formatu wierzchołka w buforze GPU
            var vertexAttributes = new NativeArray<VertexAttributeDescriptor>(2, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            vertexAttributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position);
            vertexAttributes[1] = new VertexAttributeDescriptor(VertexAttribute.Normal);

            OutputMeshData.SetVertexBufferParams(Vertices.Length, vertexAttributes);
            vertexAttributes.Dispose();
            OutputMeshData.SetIndexBufferParams(Triangles.Length, IndexFormat.UInt32);

            // wskaźniki do buforów MeshData
            NativeArray<VertexLayout> outVertices = OutputMeshData.GetVertexData<VertexLayout>();
            NativeArray<int> outIndices = OutputMeshData.GetIndexData<int>();
            outIndices.CopyFrom(Triangles.AsArray()); // od razu przepisanie trojkątów
            
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
                    Normal = float3.zero
                };
            }
            
            // obliczenie normalow
            for (int i = 0; i < Triangles.Length; i += 3)
            {
                int i0 = Triangles[i];
                int i1 = Triangles[i + 1];
                int i2 = Triangles[i + 2];

                float3 v0 = outVertices[i0].Position;
                float3 v1 = outVertices[i1].Position;
                float3 v2 = outVertices[i2].Position;

                float3 triNormal = math.cross(v1 - v0, v2 - v0);

                var vert0 = outVertices[i0];
                var vert1 = outVertices[i1];
                var vert2 = outVertices[i2];

                vert0.Normal += triNormal;
                vert1.Normal += triNormal;
                vert2.Normal += triNormal;

                outVertices[i0] = vert0;
                outVertices[i1] = vert1;
                outVertices[i2] = vert2;
            }

            // normalizacja
            for (int i = 0; i < outVertices.Length; i++)
            {
                var vert = outVertices[i];
                float lenSq = math.lengthsq(vert.Normal);
                if (lenSq > 1e-6f)
                {
                    vert.Normal *= math.rsqrt(lenSq);
                }
                outVertices[i] = vert;
            }
            
            float3 size = max - min;
            float3 center = min + (size * 0.5f);
            OutputBounds.Value = new Bounds(center, size);

            OutputMeshData.subMeshCount = 1;
            OutputMeshData.SetSubMesh(0, new SubMeshDescriptor(0, Triangles.Length, MeshTopology.Triangles), MeshUpdateFlags.DontRecalculateBounds);
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