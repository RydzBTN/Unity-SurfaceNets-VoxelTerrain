using _Project.SurfaceNets.Chunks.Density_Generator;
using Unity.Burst;
using Unity.Mathematics;

public enum BodyType
{
    Meteoroid,
    SmallAsteroid,
    Asteroid,
    Planetoid,
    Moon,
    Comet
}
// todo działanie na ScriptableObject/zewnętrzne dane
public struct CelestialBodyGenerator
{
    public static float GenerateDensity(float3 worldPos, BodyType bodyType, BurstSimplexNoise noise)
    {
        switch (bodyType)
        {
            case BodyType.Meteoroid:
                return Meteoroid(worldPos, noise);

            case BodyType.SmallAsteroid:
                return SmallAsteroid(worldPos, noise);

            case BodyType.Asteroid:
                return Asteroid(worldPos, noise);

            case BodyType.Planetoid:
                return Planetoid(worldPos, noise);
            
            case BodyType.Moon:
                return Moon(worldPos, noise);
            
            case BodyType.Comet:
                return Comet(worldPos, noise);

            default:
                return worldPos.y; // płaska powierzchnia
        }
    }
    

    // ============= METEOROID =============
    private static float Meteoroid(float3 pos, BurstSimplexNoise noise)
    {
        float radius = 8f;
        float distance = math.length(pos);

        float surfaceNoise = noise.Generate((float3)pos * 0.15f);

        return distance - (radius + surfaceNoise);
    }

    // ============= SMALL ASTEROID =============
    private static float SmallAsteroid(float3 pos, BurstSimplexNoise noise)
    {
        float radius = 25f;
        float distance = math.length(pos);

        float n = FractalNoise(pos, noise,
            octaves: 2,
            frequency: 0.04f,
            amplitude: 5f,
            lacunarity: 2.0f,
            persistence: 0.5f);

        return distance - (radius + n);
    }

    // ============= ASTEROID =============
    private static float Asteroid(float3 pos, BurstSimplexNoise noise)
    {
        float radius = 150;
        float distance = math.length(pos);

        float n = FractalNoise(pos, noise,
            octaves: 4,
            frequency: 0.02f,
            amplitude: 10f,
            lacunarity: 2.0f,
            persistence: 0.5f);

        return distance - (radius + n);
    }

    // ============= PLANETOID =============
    private static float Planetoid(float3 pos, BurstSimplexNoise noise)
    {
        float radius = 500f;
        float distance = math.length(pos);
        
        float terrain = FractalNoise(pos, noise,
            octaves: 5,
            frequency: 0.005f,
            amplitude: 20f,
            lacunarity: 2.0f,
            persistence: 0.5f);

        return distance - (radius + terrain);
    }

    // ============= MOON / PLANET =============
    private static float Moon(float3 pos, BurstSimplexNoise noise)
    {
        float radius = 2000f;
        float distance = math.length(pos);
        
        float terrain = FractalNoise(pos, noise,
            octaves: 5,
            frequency: 0.001f,
            amplitude: 50f,
            lacunarity: 2.0f,
            persistence: 0.5f);

        return distance - (radius + terrain);
    }

    // ============= COMET =============
    // todo dodać niesymetryczną warstwe lodu
    private static float Comet(float3 pos, BurstSimplexNoise noise)
    {
        float coreRadius = 15f;
        float distance = math.length(pos);

        // Rdzeń skalny
        float coreNoise = FractalNoise(pos, noise,
            octaves: 3,
            frequency: 0.1f,
            amplitude: 2.0f,
            lacunarity: 2.0f,
            persistence: 0.5f);

        return distance - (coreRadius + coreNoise);
    }
    
    // ============= FRACTAL BROWNIAN MOTION =============
    /// <param name="octaves">Liczba warstw szumu</param>
    /// <param name="frequency">Częstotliwość szumu</param>
    /// <param name="amplitude">Wysokość nierówności</param>
    /// <param name="lacunarity">Jak szybko rośnie częstotliwość</param>
    /// <param name="persistence">Jak szybko maleje amplituda</param>
    /// <returns></returns>
    private static float FractalNoise(float3 pos, BurstSimplexNoise noise,
        int octaves, float frequency, float amplitude, float lacunarity, float persistence)
    {
        float total = 0;
        float amp = amplitude;
        float freq = frequency;

        float3 offset = float3.zero;
        float3 offsetStep = new float3(78.2f, 126.1f, 238.6f);
        for (int i = 0; i < octaves; i++)
        {
            total += noise.Generate((pos + offset) * freq) * amp;
            offset += offsetStep;
            
            amp *= persistence;
            freq *= lacunarity;
        }
        
        return total;
    }
    
    #region HELPERS
    /// <summary>
    /// Zwraca promień ciała dla danego typu (przydatne do LOD i generowania chunków)
    /// </summary>
    public static float GetBodyRadius(BodyType bodyType)
    {
        switch (bodyType)
        {
            case BodyType.Meteoroid: return 8f;
            case BodyType.SmallAsteroid: return 25f;
            case BodyType.Asteroid: return 150;
            case BodyType.Planetoid: return 500f;
            case BodyType.Moon: return 2000f;
            case BodyType.Comet: return 15f;
            default: return 50f;
        }
    }

    /// <summary>
    /// Zwraca przewidywaną granice terenu w liczbie chunków w lini prostej od środka ciała niebieskiego (0,0,0)
    /// </summary>
    [BurstCompile]
    public static int GetSuggestedChunkRadius(BodyType bodyType, int chunkSize)
    {
        float radius = GetBodyRadius(bodyType);
        int chunksNeeded = (int)math.ceil(radius / chunkSize) ;
        return chunksNeeded + 2;
    }
    #endregion
}