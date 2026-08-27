using Unity.Mathematics;

namespace _Project.SurfaceNets.Density_Generator
{
    public static class SDFMath
    {
        /// <summary>
        /// ZWYKŁA SUMA (Skleja dwie bryły razem)
        /// </summary>
        public static float Union(float d1, float d2) => math.min(d1, d2);

        /// <summary>
        /// WYCINANIE odejmuje bryłę d2 z bryły d1
        /// </summary>
        public static float Subtract(float d1, float d2) => math.max(d1, -d2);

        /// <summary>
        /// CZĘŚĆ WSPÓLNA
        /// </summary>
        public static float Intersect(float d1, float d2) => math.max(d1, d2);

        /// <summary>
        /// PŁYNNA SUMA
        /// </summary>
        /// <param name="k">siła wygładzania</param>
        public static float SmoothUnion(float d1, float d2, float k)
        {
            float h = math.clamp(0.5f + 0.5f * (d2 - d1) / k, 0.0f, 1.0f);
            return math.lerp(d2, d1, h) - k * h * (1.0f - h);
        }
        
        /// <summary>
        /// PŁYNNE WYCINANIE
        /// </summary>
        /// <param name="k">siła wygładzania</param>
        public static float SmoothSubtract(float d1, float d2, float k)
        {
            float h = math.clamp(0.5f - 0.5f * (d2 + d1) / k, 0.0f, 1.0f);
            return math.lerp(d1, -d2, h) + k * h * (1.0f - h);
        }
    }
}