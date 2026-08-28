using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ChunkSN : MonoBehaviour
{
    #region CONSTANTS, STATICS & CONFIGURATION
    [Header("Debug")]
    [SerializeField] private bool showGizmos = false;
    [SerializeField] private bool showBounds = false;
    
    public const int Size = 16;
    public const int VoxelArraySize = Size + 1;
    public const int DensityArraySize = Size + 2;
    public static readonly Vector3Int Offset = new Vector3Int(Size / 2, Size / 2, Size / 2);
    #endregion

    #region FIELDS
    private MeshFilter _meshFilter;
    private Mesh _mesh;
    private MeshRenderer _meshRenderer;
    #endregion
    
    #region UNITY
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        
        if (showGizmos)
        {
            if (_meshFilter == null) return;

            Vector3[] vertices = _meshFilter.sharedMesh.vertices;
            Vector3[] normals = _meshFilter.sharedMesh.normals;

            Gizmos.color = Color.softRed;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldPos = transform.TransformPoint(vertices[i]);
                Vector3 worldNormal = transform.TransformDirection(normals[i]);
                
                Gizmos.DrawLine(worldPos, worldPos + worldNormal * 1f);
                Gizmos.DrawSphere(worldPos + worldNormal * 1f, 0.05f);
            }
        }
        
        if (showBounds)
        {
            Gizmos.color = Color.darkOrange;
            Gizmos.DrawWireCube(transform.position + Offset, new Vector3(Size, Size, Size));
        }
       
    }
    #endregion
    
    #region PUBLIC API
    private void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();
        
        _mesh = new Mesh();
        _meshFilter.sharedMesh = _mesh;
    }
    
    public void Reset()
    {
        _mesh.Clear();
        gameObject.SetActive(false);
    }
    #endregion
    
    #region MESH GENERATION
    public void SetMesh(NativeList<float3> vertices, NativeList<int> triangles)
    {
        _mesh.Clear();
        
        _mesh.SetVertices(vertices.AsArray());
        _mesh.SetIndices(triangles.AsArray(), MeshTopology.Triangles, 0);
        
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        
        _mesh.name = $"{gameObject.name}_Mesh";
        
        _meshFilter.sharedMesh = _mesh;
    }
    
    public void SetMesh(Mesh.MeshDataArray meshArray, Bounds bounds)
    {
        Mesh.ApplyAndDisposeWritableMeshData(meshArray, _mesh, MeshUpdateFlags.DontValidateIndices);
        _mesh.bounds = bounds;
        
        _mesh.name = $"{gameObject.name}_Mesh";
    }
    #endregion
}