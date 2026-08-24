using System.Collections;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

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

                // Narysuj linię od wierzchołka w kierunku normalnej
                Gizmos.DrawLine(worldPos, worldPos + worldNormal * 1f); // 1f = długość strzałki

                // Opcjonalnie: mała kulka na końcu dla lepszej widoczności
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
    public void Initialize()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();
        
        _mesh = new Mesh();
        _meshFilter.sharedMesh = _mesh;
    }
    
    public NativeArray<Point> ModifyDensityLocal(Vector3 miningWorldPos, float addedDens)
    {
        Vector3Int localPos = Vector3Int.RoundToInt(transform.TransformPoint(miningWorldPos));

        if (localPos.x < 0 || localPos.y < 0 || localPos.z < 0) return default;
        if (localPos.x >= DensityArraySize || localPos.y >= DensityArraySize || localPos.z >= DensityArraySize) return default;

        //_points = CalculateDensityWithJob(transform.position, _type, _noise);
        // może generowanie, zapis i edycja densityArray
        return default;
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
        //Debug.Log($"<color = green>CHUNK MESH: Wierzchołki = {_vertices.Count}, Trójkąty = {_triangles.Count / 3}</color>");
    }
    
    public void SetMesh(Mesh.MeshDataArray meshArray, Bounds bounds, bool disableRenderer)
    {
        Mesh.ApplyAndDisposeWritableMeshData(meshArray, _mesh, MeshUpdateFlags.DontValidateIndices);
        _mesh.bounds = bounds;
        
        _mesh.name = $"{gameObject.name}_Mesh";
        
        // if (TryGetComponent<MeshRenderer>(out var renderer))
        // {
        //     renderer.enabled = _mesh.vertexCount > 0;
        // }
    }
    #endregion
}