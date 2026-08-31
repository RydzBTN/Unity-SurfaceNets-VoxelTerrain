using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Mathematics;

namespace _Project.SurfaceNets.Density
{
    /// <summary>
    /// Pomaga przemieniać indexy tablic 3D [,,] na 1D [] i odwrotnie
    /// </summary>
    public static class Grid3D
    {
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 ToXYZ(int index, int size)
        {
            int sizeSquare = size * size;
            return new int3(
                index / (size * size),
                (index / size) % size,
                index % size
            );
        }
        
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToIndex(int x, int y, int z, int size)
        {
            return x * size * size + y * size + z;
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToIndex(int3 position, int size)
        {
            return ToIndex(position.x, position.y, position.z, size);
        }
    }
}