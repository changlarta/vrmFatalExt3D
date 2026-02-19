using System;
using System.Linq;
using UnityEngine;

public static class SkinRecolorBaker
{
    // -Z 付近（鼻方向）の近傍平均サンプリング半径（テクスチャのピクセル単位）
    private const int SampleRadiusPx = 50;

    public static Texture2D BakeFinalFromFace(
        Texture2D skinTex,
        Texture2D addTex,
        SkinnedMeshRenderer faceSmr)
    {
        if (faceSmr == null) throw new ArgumentNullException(nameof(faceSmr));
        if (skinTex == null) throw new ArgumentNullException(nameof(skinTex));

        int w = skinTex.width;
        int h = skinTex.height;

        if (addTex != null && (addTex.width != w || addTex.height != h))
            throw new ArgumentException("SkinTex と addTex の解像度が一致していません。");

        var sampled = EstimateSkinColorFromFaceSmr(faceSmr);
        if (!sampled.HasValue)
            throw new InvalidOperationException("faceSmr から肌色を採色できませんでした。");

        // skinTex の「色・陰影の相対変化」を維持したまま、採色した肌色へ転写する
        var baseTex = RecolorFromReference(skinTex, new Color(sampled.Value.r, sampled.Value.g, sampled.Value.b, 1f));

        if (addTex == null) return baseTex;
        return CompositeOver(baseTex, addTex);
    }

    public static Texture2D BakeFinal(
        Texture2D skinTex, Texture2D addTex,
        Color baseSkinSrgb, Color targetSrgb)
    {
        if (skinTex == null) throw new ArgumentNullException(nameof(skinTex));

        int w = skinTex.width;
        int h = skinTex.height;

        if (addTex != null && (addTex.width != w || addTex.height != h))
            throw new ArgumentException("SkinTex と addTex の解像度が一致していません。");

        // baseSkinSrgb を参照色（= skinTex の基準色）として扱う
        var baseTex = RecolorFromReference(skinTex, new Color(targetSrgb.r, targetSrgb.g, targetSrgb.b, 1f), baseSkinSrgb);

        if (addTex == null) return baseTex;
        return CompositeOver(baseTex, addTex);
    }

    public static void ApplyToMToon10Lighting(Material mat, Texture2D litColorTexture, Texture2D shadeColorTexture)
    {
        if (mat == null) throw new ArgumentNullException(nameof(mat));
        if (litColorTexture == null) throw new ArgumentNullException(nameof(litColorTexture));
        if (shadeColorTexture == null) throw new ArgumentNullException(nameof(shadeColorTexture));

        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", litColorTexture);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", litColorTexture);
        if (mat.HasProperty("_ShadeTex")) mat.SetTexture("_ShadeTex", shadeColorTexture);
    }

    // skinTex を「参照色(refColor)に対する比」で分解し、targetColor へ転写する
    private static Texture2D RecolorFromReference(Texture2D skinTex, Color targetColorSrgb01, Color? refColorSrgb01 = null)
    {
        int w = skinTex.width;
        int h = skinTex.height;

        var sp = skinTex.GetPixels32();
        var dp = new Color32[sp.Length];

        const float EPS = 1e-6f;

        Color refc = refColorSrgb01 ?? EstimateAverageRgb(skinTex);
        float rr = Mathf.Max(Mathf.Clamp01(refc.r), EPS);
        float rg = Mathf.Max(Mathf.Clamp01(refc.g), EPS);
        float rb = Mathf.Max(Mathf.Clamp01(refc.b), EPS);

        float tr = Mathf.Clamp01(targetColorSrgb01.r);
        float tg = Mathf.Clamp01(targetColorSrgb01.g);
        float tb = Mathf.Clamp01(targetColorSrgb01.b);

        for (int i = 0; i < sp.Length; i++)
        {
            float sr = sp[i].r / 255f;
            float sg = sp[i].g / 255f;
            float sb = sp[i].b / 255f;

            // 参照色(refc)に対する比率（skinTex 側のディテールを保持）
            float mr = sr / rr;
            float mg = sg / rg;
            float mb = sb / rb;

            float outR = Mathf.Clamp01(tr * mr);
            float outG = Mathf.Clamp01(tg * mg);
            float outB = Mathf.Clamp01(tb * mb);

            // 暗部ほど赤みに寄せる（skinTex の暗さで制御）
            float refL = 0.2126f * rr + 0.7152f * rg + 0.0722f * rb;
            float srcL = 0.2126f * sr + 0.7152f * sg + 0.0722f * sb;

            float relL = srcL / Mathf.Max(refL, EPS);
            float darkness = 1f - Mathf.Clamp01(relL);

            // 仕様値：暗部の赤みの強さ
            const float RednessStrength = 0.35f;
            float f = Mathf.Clamp01(darkness * RednessStrength);

            outR = Mathf.Clamp01(outR * (1f + 1.00f * f));
            outG = Mathf.Clamp01(outG * (1f - 0.35f * f));
            outB = Mathf.Clamp01(outB * (1f - 0.35f * f));

            dp[i] = new Color32(
                (byte)Mathf.RoundToInt(outR * 255f),
                (byte)Mathf.RoundToInt(outG * 255f),
                (byte)Mathf.RoundToInt(outB * 255f),
                sp[i].a
            );
        }


        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        tex.SetPixels32(dp);
        tex.Apply(false, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }

    private static Color EstimateAverageRgb(Texture2D tex)
    {
        var px = tex.GetPixels32();
        long sumR = 0, sumG = 0, sumB = 0;
        long count = px.LongLength;
        if (count <= 0) return Color.white;

        for (long i = 0; i < count; i++)
        {
            sumR += px[i].r;
            sumG += px[i].g;
            sumB += px[i].b;
        }

        float inv = 1f / (count * 255f);
        return new Color(sumR * inv, sumG * inv, sumB * inv, 1f);
    }

    private static Texture2D CompositeOver(Texture2D baseTex, Texture2D overTex)
    {
        if (baseTex == null) throw new ArgumentNullException(nameof(baseTex));
        if (overTex == null) throw new ArgumentNullException(nameof(overTex));
        if (baseTex.width != overTex.width || baseTex.height != overTex.height)
            throw new ArgumentException("baseTex と overTex の解像度が一致していません。");

        int w = baseTex.width;
        int h = baseTex.height;

        var bp = baseTex.GetPixels32();
        var op = overTex.GetPixels32();
        var rp = new Color32[bp.Length];

        for (int i = 0; i < bp.Length; i++)
        {
            float br = bp[i].r / 255f;
            float bg = bp[i].g / 255f;
            float bb = bp[i].b / 255f;
            float ba = bp[i].a / 255f;

            float or = op[i].r / 255f;
            float og = op[i].g / 255f;
            float ob = op[i].b / 255f;
            float oa = op[i].a / 255f;

            float outA = oa + ba * (1f - oa);

            float outR = (or * oa + br * ba * (1f - oa));
            float outG = (og * oa + bg * ba * (1f - oa));
            float outB = (ob * oa + bb * ba * (1f - oa));

            if (outA > 0f)
            {
                outR /= outA;
                outG /= outA;
                outB /= outA;
            }
            else
            {
                outR = 0f;
                outG = 0f;
                outB = 0f;
            }

            rp[i] = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(outR * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(outG * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(outB * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(outA * 255f), 0, 255)
            );
        }

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        tex.SetPixels32(rp);
        tex.Apply(false, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }

    private static Color? EstimateSkinColorFromFaceSmr(SkinnedMeshRenderer smr)
    {
        if (smr == null) throw new ArgumentNullException(nameof(smr));
        if (smr.sharedMesh == null || smr.sharedMesh.vertexCount == 0)
            throw new InvalidOperationException("sharedMesh が null または空です。");

        if (SampleRadiusPx <= 0)
            throw new InvalidOperationException("SampleRadiusPx が不正です。");

        Mesh baked = new Mesh();
        try
        {
            smr.BakeMesh(baked);
            var verts = baked.vertices;
            if (verts == null || verts.Length == 0)
                throw new InvalidOperationException("BakeMesh の結果、頂点が取得できませんでした。");

            var l2w = smr.transform.localToWorldMatrix;
            var world = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
                world[i] = l2w.MultiplyPoint3x4(verts[i]);

            int viNegZ = ArgMaxDot(world, Vector3.back);
            if (viNegZ < 0)
                throw new InvalidOperationException("-Z極値頂点が取得できませんでした。");

            var mesh = smr.sharedMesh;
            var uv = mesh.uv;
            if (uv == null || uv.Length != mesh.vertexCount)
                throw new InvalidOperationException("uv が null または vertexCount と一致しません。");

            var v2s = BuildVertexToSubmeshTable(mesh);
            var mats = smr.sharedMaterials;
            if (mats == null || mats.Length == 0)
                throw new InvalidOperationException("sharedMaterials が空です。");

            Color c = SampleAverageAroundUvPixels(mats, v2s, uv, viNegZ, SampleRadiusPx);
            return new Color(c.r, c.g, c.b, 1f);
        }
        finally
        {
            UnityEngine.Object.Destroy(baked);
        }
    }

    private static Color SampleAverageAroundUvPixels(
        Material[] mats, int[] v2s, Vector2[] uv, int vi, int radiusPx)
    {
        if (uv == null) throw new ArgumentNullException(nameof(uv));
        if (mats == null) throw new ArgumentNullException(nameof(mats));
        if (v2s == null) throw new ArgumentNullException(nameof(v2s));
        if (vi < 0 || vi >= uv.Length) throw new ArgumentOutOfRangeException(nameof(vi));
        if (radiusPx <= 0) throw new ArgumentOutOfRangeException(nameof(radiusPx));

        int sub = (vi >= 0 && vi < v2s.Length) ? v2s[vi] : -1;
        if (sub < 0 || sub >= mats.Length || mats[sub] == null)
            throw new InvalidOperationException("頂点に対応するサブメッシュのマテリアルが取得できませんでした。");

        Material mat = mats[sub];

        var tex = GetAnyBaseTexture(mat);
        if (tex == null)
            throw new InvalidOperationException("ベーステクスチャ(_MainTex/_BaseMap/_BaseColorMap)が取得できませんでした。");

        bool createdTemp;
        var readable = GetReadableTexture(tex, out createdTemp);
        if (readable == null)
            throw new InvalidOperationException("テクスチャをReadable化できませんでした。");

        try
        {
            Vector2 tuv = uv[vi];
            if (mat.HasProperty("_BaseMap_ST"))
            {
                var st = mat.GetVector("_BaseMap_ST");
                tuv = new Vector2(tuv.x * st.x + st.z, tuv.y * st.y + st.w);
            }
            else if (mat.HasProperty("_MainTex_ST"))
            {
                var st = mat.GetVector("_MainTex_ST");
                tuv = new Vector2(tuv.x * st.x + st.z, tuv.y * st.y + st.w);
            }
            else if (mat.HasProperty("_BaseColorMap_ST"))
            {
                var st = mat.GetVector("_BaseColorMap_ST");
                tuv = new Vector2(tuv.x * st.x + st.z, tuv.y * st.y + st.w);
            }

            int w = readable.width;
            int h = readable.height;
            if (w <= 0 || h <= 0)
                throw new InvalidOperationException("Readableテクスチャの解像度が不正です。");

            float u = Mathf.Repeat(tuv.x, 1f);
            float v = Mathf.Repeat(tuv.y, 1f);
            int cx = Mathf.Clamp(Mathf.RoundToInt(u * (w - 1)), 0, w - 1);
            int cy = Mathf.Clamp(Mathf.RoundToInt(v * (h - 1)), 0, h - 1);

            var px = readable.GetPixels32();

            long sumR = 0, sumG = 0, sumB = 0;
            long count = 0;

            for (int dy = -radiusPx; dy <= radiusPx; dy++)
            {
                int y = Mod(cy + dy, h);
                int row = y * w;
                for (int dx = -radiusPx; dx <= radiusPx; dx++)
                {
                    int x = Mod(cx + dx, w);
                    var c = px[row + x];
                    sumR += c.r;
                    sumG += c.g;
                    sumB += c.b;
                    count++;
                }
            }

            if (count <= 0)
                throw new InvalidOperationException("平均化サンプル数が0になりました。");

            float inv = 1f / (count * 255f);
            return new Color(sumR * inv, sumG * inv, sumB * inv, 1f);
        }
        finally
        {
            if (createdTemp) UnityEngine.Object.Destroy(readable);
        }
    }

    private static int Mod(int a, int m)
    {
        int r = a % m;
        return (r < 0) ? (r + m) : r;
    }

    private static int ArgMaxDot(Vector3[] pts, Vector3 dir)
    {
        float best = float.NegativeInfinity;
        int idx = -1;
        for (int i = 0; i < pts.Length; i++)
        {
            float d = Vector3.Dot(pts[i], dir);
            if (d > best) { best = d; idx = i; }
        }
        return idx;
    }

    private static int[] BuildVertexToSubmeshTable(Mesh m)
    {
        var v2s = Enumerable.Repeat(-1, m.vertexCount).ToArray();
        for (int s = 0; s < m.subMeshCount; s++)
        {
            var tri = m.GetTriangles(s);
            for (int i = 0; i < tri.Length; i++)
            {
                int v = tri[i];
                if (v >= 0 && v < v2s.Length) v2s[v] = s;
            }
        }
        return v2s;
    }

    private static Texture2D GetAnyBaseTexture(Material m)
    {
        if (!m) return null;
        if (m.HasProperty("_MainTex"))
        {
            var t = m.GetTexture("_MainTex") as Texture2D;
            if (t) return t;
        }
        if (m.HasProperty("_BaseMap"))
        {
            var t = m.GetTexture("_BaseMap") as Texture2D;
            if (t) return t;
        }
        if (m.HasProperty("_BaseColorMap"))
        {
            var t = m.GetTexture("_BaseColorMap") as Texture2D;
            if (t) return t;
        }
        return null;
    }

    private static Texture2D GetReadableTexture(Texture2D src, out bool createdTemp)
    {
        createdTemp = false;
        if (!src) return null;

        try
        {
            src.GetPixel(0, 0);
            return src;
        }
        catch
        {
        }

        var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;

        var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, false);
        tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        tex.Apply(false, true);

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        createdTemp = true;
        return tex;
    }
}
