using Godot;
using System;

namespace DeltaProject.Infrastructure.Rendering;

/// <summary>
/// Procedural low-poly goldfish mesh built with SurfaceTool.
/// Surface layout: 0=body  1=caudal  2=dorsal  3=pec_L  4=pec_R
/// </summary>
public static class GoldfishMeshBuilder
{
    private const int Rings = 13;
    private const int Segs  = 8;

    // Profile rows: [z_pos, x_radius, y_radius] — nose(0) → peduncle(12)
    private static readonly float[,] Prof = {
        { -0.50f, 0.030f, 0.022f },
        { -0.42f, 0.068f, 0.052f },
        { -0.33f, 0.112f, 0.088f },
        { -0.25f, 0.142f, 0.112f },
        { -0.17f, 0.162f, 0.128f },
        { -0.08f, 0.176f, 0.140f },
        {  0.00f, 0.168f, 0.134f },
        {  0.08f, 0.150f, 0.120f },
        {  0.17f, 0.128f, 0.102f },
        {  0.25f, 0.100f, 0.080f },
        {  0.33f, 0.074f, 0.059f },
        {  0.42f, 0.050f, 0.040f },
        {  0.50f, 0.028f, 0.022f },
    };

    public static ArrayMesh Build()
    {
        var mesh = new ArrayMesh();
        BuildBody(mesh);
        BuildCaudalFin(mesh);
        BuildDorsalFin(mesh);
        BuildPectoralFin(mesh, left: true);
        BuildPectoralFin(mesh, left: false);
        return mesh;
    }

    // ── Surface builders ──────────────────────────────────────────────────────

    private static void BuildBody(ArrayMesh mesh)
    {
        var st = BeginTris();

        var apex   = new Vector3(0f, 0f, Prof[0, 0] - 0.04f);
        var apexUV = new Vector2(0f, 0.5f);
        for (int s = 0; s < Segs; s++)
        {
            int n = (s + 1) % Segs;
            Tri(st, apex, Pt(0, n), Pt(0, s), apexUV, BodyUV(0, n), BodyUV(0, s));
        }

        for (int r = 0; r < Rings - 1; r++)
            for (int s = 0; s < Segs; s++)
            {
                int n = (s + 1) % Segs;
                Quad(st,
                    Pt(r, s),     Pt(r, n),     Pt(r+1, n),   Pt(r+1, s),
                    BodyUV(r, s), BodyUV(r, n), BodyUV(r+1,n),BodyUV(r+1,s));
            }

        st.GenerateNormals();
        st.Commit(mesh);
    }

    private static void BuildCaudalFin(ArrayMesh mesh)
    {
        var st = BeginTris();

        var ped = new Vector3( 0.00f,  0.00f, 0.50f);
        var fUL = new Vector3(-0.06f,  0.07f, 0.63f);
        var fUR = new Vector3( 0.06f,  0.07f, 0.63f);
        var fLL = new Vector3(-0.06f, -0.07f, 0.63f);
        var fLR = new Vector3( 0.06f, -0.07f, 0.63f);
        var tUL = new Vector3(-0.16f,  0.23f, 0.92f);
        var tUR = new Vector3( 0.16f,  0.23f, 0.92f);
        var tLL = new Vector3(-0.16f, -0.23f, 0.92f);
        var tLR = new Vector3( 0.16f, -0.23f, 0.92f);

        var uPed = new Vector2(1.00f, 0.50f);
        var uFU  = new Vector2(1.05f, 0.66f);
        var uFL  = new Vector2(1.05f, 0.34f);
        var uTU  = new Vector2(1.30f, 0.82f);
        var uTL  = new Vector2(1.30f, 0.18f);

        DTri(st, ped, fUL, fUR, uPed, uFU, uFU);
        DTri(st, ped, fUR, fLR, uPed, uFU, uFL);
        DTri(st, ped, fLR, fLL, uPed, uFL, uFL);
        DTri(st, ped, fLL, fUL, uPed, uFL, uFU);

        DQuad(st, fUL, tUL, tUR, fUR, uFU, uTU, uTU, uFU);
        DQuad(st, fLL, fLR, tLR, tLL, uFL, uFL, uTL, uTL);

        st.GenerateNormals();
        st.Commit(mesh);
    }

    private static void BuildDorsalFin(ArrayMesh mesh)
    {
        var st    = BeginTris();
        int rBase = 3;

        float[] dorH  = { 0.03f, 0.10f, 0.15f, 0.13f, 0.08f, 0.03f };
        int     count = dorH.Length;

        var bPt = new Vector3[count];
        var tPt = new Vector3[count];
        var bUV = new Vector2[count];
        var tUV = new Vector2[count];

        for (int i = 0; i < count; i++)
        {
            bPt[i] = Pt(rBase + i, 2);
            bUV[i] = new Vector2(Prof[rBase + i, 0] + 0.5f, 1.0f);
            tPt[i] = bPt[i] + new Vector3(0f, dorH[i], 0f);
            tUV[i] = new Vector2(bUV[i].X, 1.0f + dorH[i] * 3f);
        }

        for (int i = 0; i < count - 1; i++)
            DQuad(st, bPt[i], bPt[i+1], tPt[i+1], tPt[i],
                      bUV[i], bUV[i+1], tUV[i+1], tUV[i]);

        st.GenerateNormals();
        st.Commit(mesh);
    }

    private static void BuildPectoralFin(ArrayMesh mesh, bool left)
    {
        var   st = BeginTris();
        float sd = left ? -1f : 1f;
        float xR = Prof[4, 1], yR = Prof[4, 2], z = Prof[4, 0];

        var r0  = new Vector3(sd * xR * 0.90f,  yR * 0.10f,  z - 0.05f);
        var r1  = new Vector3(sd * xR,           0f,          z);
        var r2  = new Vector3(sd * xR * 0.85f,  -yR * 0.30f, z + 0.06f);
        var tip = new Vector3(sd * (xR + 0.16f), -yR * 0.10f, z + 0.03f);

        var uR = new Vector2(0.33f, 0.5f);
        var uT = new Vector2(0.33f, left ? 0f : 1f);

        DTri(st, r0, r1, tip, uR, uR, uT);
        DTri(st, r1, r2, tip, uR, uR, uT);

        st.GenerateNormals();
        st.Commit(mesh);
    }

    // ── Geometry helpers ──────────────────────────────────────────────────────

    private static SurfaceTool BeginTris()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        return st;
    }

    private static void Tri(SurfaceTool st,
        Vector3 a, Vector3 b, Vector3 c,
        Vector2 ua, Vector2 ub, Vector2 uc)
    {
        st.SetUV(ua); st.AddVertex(a);
        st.SetUV(ub); st.AddVertex(b);
        st.SetUV(uc); st.AddVertex(c);
    }

    private static void Quad(SurfaceTool st,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d,
        Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
    {
        Tri(st, a, b, c, ua, ub, uc);
        Tri(st, a, c, d, ua, uc, ud);
    }

    private static void DTri(SurfaceTool st,
        Vector3 a, Vector3 b, Vector3 c,
        Vector2 ua, Vector2 ub, Vector2 uc)
    {
        Tri(st, a, b, c, ua, ub, uc);
        Tri(st, a, c, b, ua, uc, ub);
    }

    private static void DQuad(SurfaceTool st,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d,
        Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
    {
        Quad(st, a, b, c, d, ua, ub, uc, ud);
        Quad(st, d, c, b, a, ud, uc, ub, ua);
    }

    private static Vector3 Pt(int r, int s)
    {
        float a = (float)(2.0 * Math.PI * s / Segs);
        return new Vector3(Prof[r, 1] * MathF.Cos(a), Prof[r, 2] * MathF.Sin(a), Prof[r, 0]);
    }

    private static Vector2 BodyUV(int r, int s)
    {
        float a = (float)(2.0 * Math.PI * s / Segs);
        return new Vector2(Prof[r, 0] + 0.5f, (MathF.Sin(a) + 1f) * 0.5f);
    }
}
