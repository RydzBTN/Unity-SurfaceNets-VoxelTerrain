using Unity.Mathematics;
using UnityEngine;

namespace _Project.SurfaceNets.Generator
{
    public class SurfaceNetsTables
    {
        public static readonly int3[] CornerOffsets = new int3[8]
        {
            new int3(0, 0, 0), new int3(1, 0, 0),
            new int3(0, 1, 0), new int3(1, 1, 0),
            new int3(0, 0, 1), new int3(1, 0, 1),
            new int3(0, 1, 1), new int3(1, 1, 1)
        };

        // 12 krawędzi łączących odpowiednie pary narożników
        public static readonly int2[] EdgeCorners = new int2[12]
        {
            new int2(0, 1), new int2(2, 3), new int2(4, 5), new int2(6, 7), // wzdłuż X
            new int2(0, 2), new int2(1, 3), new int2(4, 6), new int2(5, 7), // wzdłuż Y
            new int2(0, 4), new int2(1, 5), new int2(2, 6), new int2(3, 7) // wzdłuż Z
        };
    }
}