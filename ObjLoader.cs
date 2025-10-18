using System.Numerics;
using SixLabors.ImageSharp;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;

class ObjMesh
{
    public List<Vector3> Vertices = new();
    public List<Vector3> Normals = new();
    public List<Vector2> UVs = new();
    public List<(int v0, int v1, int v2)> Faces = new();
    public List<(int uv0, int uv1, int uv2)> UVFaces = new();
}

static class ObjLoader
{
    public static ObjMesh Load(string path)
    {
        var mesh = new ObjMesh();
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("v "))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                mesh.Vertices.Add(new Vector3(
                    float.Parse(parts[1], CultureInfo.InvariantCulture),
                    float.Parse(parts[2], CultureInfo.InvariantCulture),
                    float.Parse(parts[3], CultureInfo.InvariantCulture)
                ));
            }
            else if (line.StartsWith("vt "))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                mesh.UVs.Add(new Vector2(
                    float.Parse(parts[1], CultureInfo.InvariantCulture),
                    float.Parse(parts[2], CultureInfo.InvariantCulture)
                ));
            }
            else if (line.StartsWith("f "))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
                if (parts.Length < 3) continue;

                int[] vIdx = new int[parts.Length];
                int[] uvIdx = new int[parts.Length];

                for (int i = 0; i < parts.Length; i++)
                {
                    var p = parts[i].Split('/');
                    vIdx[i] = int.Parse(p[0]) - 1;
                    uvIdx[i] = (p.Length > 1 && p[1] != "") ? int.Parse(p[1]) - 1 : -1;
                }

                // fan triangulate
                for (int i = 1; i < parts.Length - 1; i++)
                {
                    mesh.Faces.Add((vIdx[0], vIdx[i], vIdx[i + 1]));
                    mesh.UVFaces.Add((uvIdx[0], uvIdx[i], uvIdx[i + 1]));
                }
            }
        }
        return mesh;
    }
}