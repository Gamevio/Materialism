using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

static class BakeMap
{

    static Random rng = new();

    public static float[] ComputeCurvature(ObjMesh mesh)
    {
        int vCount = mesh.Vertices.Count;
        float[] curvature = new float[vCount];

        // Build adjacency list
        var adjacency = new Dictionary<int, List<int>>();
        for (int i = 0; i < mesh.Faces.Count; i++)
        {
            var (v0, v1, v2) = mesh.Faces[i];

            void AddEdge(int a, int b)
            {
                if (!adjacency.ContainsKey(a)) adjacency[a] = new List<int>();
                if (!adjacency[a].Contains(b)) adjacency[a].Add(b);
            }

            AddEdge(v0, v1); AddEdge(v1, v0);
            AddEdge(v1, v2); AddEdge(v2, v1);
            AddEdge(v2, v0); AddEdge(v0, v2);
        }

        // Compute per-vertex normals
        Vector3[] normals = new Vector3[vCount];
        for (int i = 0; i < vCount; i++)
            normals[i] = EstimateNormal(mesh, i);

        // Compute average angular deviation
        Parallel.For(0, vCount, i =>
        {
            var n0 = normals[i];
            if (!adjacency.ContainsKey(i) || adjacency[i].Count == 0)
            {
                curvature[i] = 0.5f; // neutral value
                return;
            }

            float sum = 0;
            foreach (int j in adjacency[i])
            {
                var n1 = normals[j];
                float dot = Math.Clamp(Vector3.Dot(Vector3.Normalize(n0), Vector3.Normalize(n1)), -1f, 1f);
                float angle = 1f - dot; // angular difference
                sum += angle;
            }
            curvature[i] = sum / adjacency[i].Count;
        });

        // Normalize 0–1
        float min = curvature.Min();
        float max = curvature.Max();
        float range = max - min + 1e-6f;
        for (int i = 0; i < vCount; i++)
            curvature[i] = (curvature[i] - min) / range;

        Console.WriteLine("Curvature computation complete!");
        return curvature;
    }


    public static float[] ComputeAO(ObjMesh mesh, int raysPerVertex)
    {
        float[] ao = new float[mesh.Vertices.Count];
        var tris = mesh.Faces.Select(f => (
            mesh.Vertices[f.v0],
            mesh.Vertices[f.v1],
            mesh.Vertices[f.v2]
        )).ToList();

        Console.WriteLine($"Casting {raysPerVertex} rays per vertex (~{mesh.Vertices.Count * raysPerVertex} total)...");

        Parallel.For(0, mesh.Vertices.Count, i =>
        {
            var pos = mesh.Vertices[i];
            var normal = EstimateNormal(mesh, i);
            int occluded = 0;

            // Use a thread-local RNG to avoid race conditions
            var localRng = new Random(Guid.NewGuid().GetHashCode());

            for (int r = 0; r < raysPerVertex; r++)
            {
                Vector3 dir = RandomHemisphereDirection(normal, localRng);
                if (RayHitsGeometry(pos + normal * 0.0001f, dir, tris))
                    occluded++;
            }

            ao[i] = 1.0f - (float)occluded / raysPerVertex;

            // Optional progress output (every 100 vertices)
            if (i % 100 == 0)
            {
                lock (Console.Out)
                {
                    Console.Write($"\rAO progress: {i}/{mesh.Vertices.Count} ({100.0 * i / mesh.Vertices.Count:F1}%)");
                }
            }
        });
        Console.WriteLine("\nAO computation complete!");

        return ao;
    }

    static Vector3 EstimateNormal(ObjMesh mesh, int vidx)
    {
        var faces = mesh.Faces.Where(f => f.v0 == vidx || f.v1 == vidx || f.v2 == vidx);
        Vector3 sum = Vector3.Zero;
        foreach (var f in faces)
        {
            var v0 = mesh.Vertices[f.v0];
            var v1 = mesh.Vertices[f.v1];
            var v2 = mesh.Vertices[f.v2];
            var n = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
            sum += n;
        }
        return Vector3.Normalize(sum);
    }

    static bool RayHitsGeometry(Vector3 origin, Vector3 dir, List<(Vector3, Vector3, Vector3)> tris)
    {
        foreach (var (v0, v1, v2) in tris)
        {
            if (RayTriangleIntersect(origin, dir, v0, v1, v2, out float t))
            {
                if (t > 0.0001f && t < 1.0f) // small distance = occluded
                    return true;
            }
        }
        return false;
    }

    static bool RayTriangleIntersect(Vector3 orig, Vector3 dir, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
    {
        t = 0;
        const float EPS = 1e-6f;
        var e1 = v1 - v0;
        var e2 = v2 - v0;
        var pvec = Vector3.Cross(dir, e2);
        float det = Vector3.Dot(e1, pvec);
        if (Math.Abs(det) < EPS) return false;
        float invDet = 1.0f / det;
        var tvec = orig - v0;
        float u = Vector3.Dot(tvec, pvec) * invDet;
        if (u < 0 || u > 1) return false;
        var qvec = Vector3.Cross(tvec, e1);
        float v = Vector3.Dot(dir, qvec) * invDet;
        if (v < 0 || u + v > 1) return false;
        t = Vector3.Dot(e2, qvec) * invDet;
        return t > EPS;
    }

    static Vector3 RandomHemisphereDirection(Vector3 normal, Random rng)
    {
        float u = (float)rng.NextDouble();
        float v = (float)rng.NextDouble();
        float theta = 2 * MathF.PI * u;
        float z = v;
        float r = MathF.Sqrt(1 - z * z);
        Vector3 dir = new(r * MathF.Cos(theta), r * MathF.Sin(theta), z);
        return AlignHemisphere(dir, normal);
    }

    static Vector3 AlignHemisphere(Vector3 dir, Vector3 normal)
    {
        Vector3 up = new(0, 0, 1);
        Vector3 axis = Vector3.Cross(up, normal);
        float angle = MathF.Acos(Vector3.Dot(up, normal));
        if (axis.LengthSquared() < 1e-6f) return normal;
        var q = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle);
        return Vector3.Transform(dir, q);
    }

    public static Image<L8> BakeToTexture(ObjMesh mesh, float[] map, int size)
    {
        var img = new Image<L8>(size, size);
        for (int i = 0; i < mesh.Faces.Count; i++)
        {
            var face = mesh.Faces[i];
            var uvFace = mesh.UVFaces[i];
            if (uvFace.uv0 < 0 || uvFace.uv1 < 0 || uvFace.uv2 < 0) continue;

            Vector2[] uvs =
            {
                mesh.UVs[uvFace.uv0],
                mesh.UVs[uvFace.uv1],
                mesh.UVs[uvFace.uv2]
            };
            float[] vals = { map[face.v0], map[face.v1], map[face.v2] };

            FillTriangle(img, uvs, vals);
        }
        return img;
    }

    static void FillTriangle(Image<L8> img, Vector2[] uvs, float[] vals)
    {
        int w = img.Width, h = img.Height;
        Vector2 FlipY(Vector2 uv) => new Vector2(uv.X, 1.0f - uv.Y);

        Vector2 p0 = Vector2.Clamp(FlipY(uvs[0]), Vector2.Zero, Vector2.One) * new Vector2(w - 1, h - 1);
        Vector2 p1 = Vector2.Clamp(FlipY(uvs[1]), Vector2.Zero, Vector2.One) * new Vector2(w - 1, h - 1);
        Vector2 p2 = Vector2.Clamp(FlipY(uvs[2]), Vector2.Zero, Vector2.One) * new Vector2(w - 1, h - 1);

        int minX = (int)MathF.Max(0, MathF.Min(p0.X, MathF.Min(p1.X, p2.X)));
        int maxX = (int)MathF.Min(w - 1, MathF.Max(p0.X, MathF.Max(p1.X, p2.X)));
        int minY = (int)MathF.Max(0, MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y)));
        int maxY = (int)MathF.Min(h - 1, MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y)));

        float area = Edge(p0, p1, p2);
        if (MathF.Abs(area) < 1e-6f) return;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new(x + 0.5f, y + 0.5f);
                float w0 = Edge(p1, p2, p);
                float w1 = Edge(p2, p0, p);
                float w2 = Edge(p0, p1, p);

                if ((w0 * area >= 0) && (w1 * area >= 0) && (w2 * area >= 0))
                {
                    w0 /= area; w1 /= area; w2 /= area;
                    float ao = w0 * vals[0] + w1 * vals[1] + w2 * vals[2];
                    byte c = (byte)(Math.Clamp(ao, 0, 1) * 255);
                    img[x, h - 1 - y] = new L8(c);
                }
            }
        }
    }

    static float Edge(Vector2 a, Vector2 b, Vector2 c)
        => (c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X);
}