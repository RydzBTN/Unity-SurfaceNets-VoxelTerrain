using _Project.SurfaceNets.Density;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace _Project.SurfaceNets.Generator.Jobs
{
    [BurstCompile]
    public struct DensityJob : IJobParallelFor
    {
        public float3 ChunkWorldPos;
        public int DensityArraySize;
        public int BodyType;
        public int LodStep;
        
        [ReadOnly] public BurstSimplexNoise Noise;
        [WriteOnly] public NativeArray<Point> Densities;
        
        public void Execute(int index)
        {
            int3 pos = Grid3D.ToXYZ(index, DensityArraySize);
            int x = pos.x, y = pos.y, z = pos.z;

            float3 globalPos = ChunkWorldPos + (new float3(x - 1, y - 1, z - 1) * LodStep);

            float density = CelestialBodyGenerator.GenerateDensity(globalPos, (BodyType)BodyType, Noise);

            Densities[index] = new Point{Density = density};
        }
    }
}