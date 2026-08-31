using Unity.Burst;
using Unity.Mathematics;

/// <summary>
/// Wartość density w sbyte dla jednego punktu w siatce dla algorytmu Surface Nets.
/// </summary>
[BurstCompile]
public struct Point
{
    private sbyte _density;
    
    public bool IsSolid => _density < 0;
    public float Density
    {
        get => _density * 0.00787401574803149606f; // 1 / 127
        set => _density = (sbyte)math.clamp(value * 127f, -128f, 127f);
    }
}
