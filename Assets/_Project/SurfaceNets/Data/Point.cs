using Unity.Burst;
using Unity.Mathematics;

/// <summary>
/// Wartość density dla jednego punktu w siatce dla algorytmu Surface Nets.
/// Liczące się wartości mieszczą się w zakresie -1 do 1 więc zamiast używać float (4 bajty)
/// to można go skompresować do sbyte (1 bajt), aby zaoszczędzić miejsce w RAM aż 4 krotnie
/// przy niewielkiej utracie precyzji +-7,8mm.
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
