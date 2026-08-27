using System;
using Unity.Collections;

namespace _Project.SurfaceNets.Data
{
    [Serializable]
    public class GeneratorData
    {
        public BurstSimplexNoise Noise;
        public int seed = 123456789;
        public BodyType type = BodyType.Asteroid;
        
        public void InitializeNoise()
        {
            Noise = new BurstSimplexNoise(seed, Allocator.Persistent);
        }

        public void Dispose()
        {
            Noise.Dispose();
        }
    }
}