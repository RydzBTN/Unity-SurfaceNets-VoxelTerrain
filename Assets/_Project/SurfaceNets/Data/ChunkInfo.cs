using _Project.SurfaceNets.Chunks;

namespace _Project.SurfaceNets.Data
{
    public enum ChunkState
    {
        Generating,
        Loaded,
        Air,
        Solid
    }
    
    public class ChunkInfo
    {
        public Chunk Chunk;
        public Point[] Density = null;
        public LOD LOD;
        public ChunkState State;
        public int GenId = 0;
    }
}