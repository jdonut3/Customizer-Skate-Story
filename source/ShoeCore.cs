// Pure logic for the shoe editor (no Unity types): picture views of the shoe, painting, fill, nearest-point lookups.
using System;
using System.Collections.Generic;

public class PointGrid
{
    const float Cell = 0.012f;
    float[] p; int n;
    Dictionary<int, List<int>> cells = new Dictionary<int, List<int>>();
    static int Key(int ix, int iy, int iz) { return (ix + 512) | ((iy + 512) << 10) | ((iz + 512) << 20); }
    static int C(float v) { return (int)Math.Floor(v / Cell); }
    public PointGrid(float[] pts)
    {
        p = pts; n = pts.Length / 3;
        for (int i = 0; i < n; i++)
        {
            int k = Key(C(p[3 * i]), C(p[3 * i + 1]), C(p[3 * i + 2]));
            List<int> l;
            if (!cells.TryGetValue(k, out l)) { l = new List<int>(); cells[k] = l; }
            l.Add(i);
        }
    }
    public void Within(float x, float y, float z, float r, List<int> result)
    {
        int ax = C(x - r), bx = C(x + r), ay = C(y - r), by = C(y + r), az = C(z - r), bz = C(z + r); float r2 = r * r;
        for (int ix = ax; ix <= bx; ix++) for (int iy = ay; iy <= by; iy++) for (int iz = az; iz <= bz; iz++)
        {
            List<int> l;
            if (!cells.TryGetValue(Key(ix, iy, iz), out l)) continue;
            for (int j = 0; j < l.Count; j++)
            {
                int i = l[j]; float dx = p[3 * i] - x, dy = p[3 * i + 1] - y, dz = p[3 * i + 2] - z;
                if (dx * dx + dy * dy + dz * dz <= r2) result.Add(i);
            }
        }
    }
    public int Nearest(float x, float y, float z, float maxDist)
    {
        int best = -1; float bd = maxDist * maxDist;
        int cx = C(x), cy = C(y), cz = C(z);
        int reach = (int)Math.Ceiling(Math.Min(maxDist, 0.03f) / Cell);
        for (int ring = 1; ring <= reach; ring++)
        {
            for (int ix = cx - ring; ix <= cx + ring; ix++) for (int iy = cy - ring; iy <= cy + ring; iy++) for (int iz = cz - ring; iz <= cz + ring; iz++)
            {
                List<int> l;
                if (!cells.TryGetValue(Key(ix, iy, iz), out l)) continue;
                for (int j = 0; j < l.Count; j++)
                {
                    int i = l[j]; float dx = p[3 * i] - x, dy = p[3 * i + 1] - y, dz = p[3 * i + 2] - z; float d = dx * dx + dy * dy + dz * dz;
                    if (d < bd) { bd = d; best = i; }
                }
            }
            if (best >= 0 && bd <= (ring * Cell) * (ring * Cell)) return best;   // nothing nearer can be in an outer ring
        }
        if (best >= 0 || maxDist <= 0.03f) return best;
        for (int i = 0; i < n; i++)
        {
            float dx = p[3 * i] - x, dy = p[3 * i + 1] - y, dz = p[3 * i + 2] - z; float d = dx * dx + dy * dy + dz * dz;
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }
}

public class ShoeCore
{
    public const int Views = 4;          // 0, 1 = the two sides, 2 = top, 3 = bottom
    public int triCount;
    public float[] pos;                  // 9 numbers for each triangle
    public float[] cen;                  // 3 numbers for each triangle
    float[] nrm;
    public byte[] slot;                  // part number of each triangle
    public byte[] baseSlot;              // the part numbers that came with the mesh
    public float minx, maxx, miny, maxy, minz, maxz;
    public int[] vw = new int[Views], vh = new int[Views];
    public int[][] idBuf = new int[Views][];
    float[][] zBuf = new float[Views][];
    List<int> hits = new List<int>();
    public PointGrid grid;
    Dictionary<long, List<int>> vertTris;
    const int Margin = 4;
    float scale;

    public ShoeCore(float[] positions, byte[] slots, int viewWidth)
    {
        pos = positions; slot = slots; baseSlot = (byte[])slots.Clone(); triCount = pos.Length / 9;
        cen = new float[triCount * 3]; nrm = new float[triCount * 3];
        minx = miny = minz = 1e9f; maxx = maxy = maxz = -1e9f;
        for (int t = 0; t < triCount; t++)
        {
            int o = 9 * t;
            for (int k = 0; k < 3; k++)
            {
                float x = pos[o + 3 * k], y = pos[o + 3 * k + 1], z = pos[o + 3 * k + 2];
                if (x < minx) minx = x; if (x > maxx) maxx = x; if (y < miny) miny = y; if (y > maxy) maxy = y; if (z < minz) minz = z; if (z > maxz) maxz = z;
                cen[3 * t] += x / 3f; cen[3 * t + 1] += y / 3f; cen[3 * t + 2] += z / 3f;
            }
            float ax = pos[o + 3] - pos[o], ay = pos[o + 4] - pos[o + 1], az = pos[o + 5] - pos[o + 2];
            float bx = pos[o + 6] - pos[o], by = pos[o + 7] - pos[o + 1], bz = pos[o + 8] - pos[o + 2];
            float nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
            float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz); if (len < 1e-12f) len = 1f;
            nrm[3 * t] = nx / len; nrm[3 * t + 1] = ny / len; nrm[3 * t + 2] = nz / len;
        }
        // 4 sample points for each triangle (the middle, and one near each corner), so large triangles are found well
        float[] smp = new float[triCount * 12];
        for (int t = 0; t < triCount; t++) for (int d = 0; d < 3; d++)
        {
            float a = pos[9 * t + d], b = pos[9 * t + 3 + d], c = pos[9 * t + 6 + d];
            smp[12 * t + d] = (a + b + c) / 3f; smp[12 * t + 3 + d] = (4 * a + b + c) / 6f; smp[12 * t + 6 + d] = (a + 4 * b + c) / 6f; smp[12 * t + 9 + d] = (a + b + 4 * c) / 6f;
        }
        grid = new PointGrid(smp);
        scale = (viewWidth - 2 * Margin) / (maxy - miny);
        for (int v = 0; v < Views; v++)
        {
            vw[v] = viewWidth;
            vh[v] = (int)Math.Ceiling((v < 2 ? (maxz - minz) : (maxx - minx)) * scale) + 2 * Margin;
            Raster(v);
        }
    }
    void Project(int v, float x, float y, float z, out float u, out float w, out float d)
    {
        switch (v)
        {
            case 0: u = (y - miny) * scale; w = (z - minz) * scale; d = x; break;
            case 1: u = (maxy - y) * scale; w = (z - minz) * scale; d = -x; break;
            case 2: u = (y - miny) * scale; w = (x - minx) * scale; d = z; break;
            default: u = (y - miny) * scale; w = (maxx - x) * scale; d = -z; break;
        }
        u += Margin; w += Margin;
    }
    void Raster(int v)
    {
        int W = vw[v], H = vh[v];
        int[] ids = new int[W * H]; float[] zb = new float[W * H];
        for (int i = 0; i < ids.Length; i++) { ids[i] = -1; zb[i] = -1e9f; }
        for (int t = 0; t < triCount; t++)
        {
            int o = 9 * t; float x0, y0, d0, x1, y1, d1, x2, y2, d2;
            Project(v, pos[o], pos[o + 1], pos[o + 2], out x0, out y0, out d0);
            Project(v, pos[o + 3], pos[o + 4], pos[o + 5], out x1, out y1, out d1);
            Project(v, pos[o + 6], pos[o + 7], pos[o + 8], out x2, out y2, out d2);
            float area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
            if (Math.Abs(area) < 1e-9f) continue;
            int xa = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2)))), xb = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
            int ya = Math.Max(0, (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2)))), yb = Math.Min(H - 1, (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))));
            for (int py = ya; py <= yb; py++) for (int px = xa; px <= xb; px++)
            {
                float fx = px + 0.5f, fy = py + 0.5f;
                float w0 = ((x1 - fx) * (y2 - fy) - (x2 - fx) * (y1 - fy)) / area;
                float w1 = ((x2 - fx) * (y0 - fy) - (x0 - fx) * (y2 - fy)) / area;
                float w2 = 1f - w0 - w1;
                if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f) continue;
                float d = w0 * d0 + w1 * d1 + w2 * d2; int i = py * W + px;
                if (d > zb[i]) { zb[i] = d; ids[i] = t; }
            }
        }
        idBuf[v] = ids; zBuf[v] = zb;
    }
    // Writes a picture of one view. rgba: 4 bytes for each pixel, row 0 is the bottom row. pal: 3 bytes (R, G, B) for each part.
    public void Colorize(int v, byte[] rgba, byte[] pal)
    {
        int W = vw[v], H = vh[v]; int[] ids = idBuf[v];
        for (int py = 0; py < H; py++) for (int px = 0; px < W; px++)
        {
            int i = py * W + px, t = ids[i], o = 4 * i;
            if (t < 0) { rgba[o] = 34; rgba[o + 1] = 36; rgba[o + 2] = 42; rgba[o + 3] = 255; continue; }
            int s = slot[t];
            float nd = v == 0 ? nrm[3 * t] : v == 1 ? -nrm[3 * t] : v == 2 ? nrm[3 * t + 2] : -nrm[3 * t + 2];
            float sh = 0.62f + 0.38f * Math.Abs(nd);
            bool edge = false;
            if (px + 1 < W) { int t2 = ids[i + 1]; if (t2 < 0 || slot[t2] != s) edge = true; }
            if (py + 1 < H) { int t2 = ids[i + W]; if (t2 < 0 || slot[t2] != s) edge = true; }
            if (edge) sh *= 0.55f;
            rgba[o] = (byte)(pal[3 * s] * sh); rgba[o + 1] = (byte)(pal[3 * s + 1] * sh); rgba[o + 2] = (byte)(pal[3 * s + 2] * sh); rgba[o + 3] = 255;
        }
    }
    public int TriAt(int v, int px, int py)
    {
        if (px < 0 || py < 0 || px >= vw[v] || py >= vh[v]) return -1;
        return idBuf[v][py * vw[v] + px];
    }
    // The 3D point under a pixel of a view. False when the pixel shows the background.
    public bool Unproject(int v, int px, int py, out float x, out float y, out float z)
    {
        x = y = z = 0f;
        if (px < 0 || py < 0 || px >= vw[v] || py >= vh[v] || idBuf[v][py * vw[v] + px] < 0) return false;
        float u = (px + 0.5f - Margin) / scale, w = (py + 0.5f - Margin) / scale, d = zBuf[v][py * vw[v] + px];
        switch (v)
        {
            case 0: y = miny + u; z = minz + w; x = d; break;
            case 1: y = maxy - u; z = minz + w; x = -d; break;
            case 2: y = miny + u; x = minx + w; z = d; break;
            default: y = miny + u; x = maxx - w; z = -d; break;
        }
        return true;
    }
    // Paints the triangles near a point. "like" is the triangle under the pointer: faces that point the opposite way
    // (the other side of a thin part) are not painted.
    bool PaintSphere(float x, float y, float z, float r, byte s, int like)
    {
        bool changed = false;
        hits.Clear(); grid.Within(x, y, z, r, hits);
        for (int i = 0; i < hits.Count; i++)
        {
            int t = hits[i] / 4;
            if (slot[t] == s) continue;
            if (nrm[3 * t] * nrm[3 * like] + nrm[3 * t + 1] * nrm[3 * like + 1] + nrm[3 * t + 2] * nrm[3 * like + 2] < -0.25f) continue;
            slot[t] = s; changed = true;
        }
        return changed;
    }
    // One brush stamp. It paints every triangle near the 3D point under the pointer (also the small ones that the picture
    // does not show), and every triangle that is visible in the brush circle.
    public bool PaintAt(int v, int px, int py, int radius, byte s, bool mirror)
    {
        bool changed = false; int W = vw[v], H = vh[v]; float cx = (minx + maxx) * 0.5f; float r3 = (radius + 0.5f) / scale;
        float x, y, z;
        if (Unproject(v, px, py, out x, out y, out z))
        {
            changed |= PaintSphere(x, y, z, r3, s, idBuf[v][py * W + px]);
            if (mirror)
            {
                int m = grid.Nearest(2f * cx - x, y, z, 0.015f);
                if (m >= 0) { int t = m / 4; changed |= PaintSphere(cen[3 * t], cen[3 * t + 1], cen[3 * t + 2], r3, s, t); }
            }
        }
        for (int dy = -radius; dy <= radius; dy++) for (int dx = -radius; dx <= radius; dx++)
        {
            if (dx * dx + dy * dy > radius * radius) continue;
            int qx = px + dx, qy = py + dy; if (qx < 0 || qy < 0 || qx >= W || qy >= H) continue;
            int t = idBuf[v][qy * W + qx]; if (t < 0) continue;
            // only triangles near the depth of the middle point, so the brush does not reach a far surface behind an edge
            if (slot[t] != s) { slot[t] = s; changed = true; }
        }
        return changed;
    }
    static long VKey(float x, float y, float z)
    {
        long a = (long)Math.Round(x * 20000f) + 100000, b = (long)Math.Round(y * 20000f) + 100000, c = (long)Math.Round(z * 20000f) + 100000;
        return a | (b << 20) | (c << 40);
    }
    public bool Fill(int start, byte s)
    {
        if (start < 0 || slot[start] == s) return false;
        if (vertTris == null)
        {
            vertTris = new Dictionary<long, List<int>>();
            for (int t = 0; t < triCount; t++) for (int k = 0; k < 3; k++)
            {
                long key = VKey(pos[9 * t + 3 * k], pos[9 * t + 3 * k + 1], pos[9 * t + 3 * k + 2]);
                List<int> l; if (!vertTris.TryGetValue(key, out l)) { l = new List<int>(); vertTris[key] = l; }
                l.Add(t);
            }
        }
        byte old = slot[start]; Queue<int> q = new Queue<int>(); q.Enqueue(start); slot[start] = s;
        Dictionary<int, int> hits = new Dictionary<int, int>();
        while (q.Count > 0)
        {
            int t = q.Dequeue(); hits.Clear();
            for (int k = 0; k < 3; k++)
            {
                List<int> l = vertTris[VKey(pos[9 * t + 3 * k], pos[9 * t + 3 * k + 1], pos[9 * t + 3 * k + 2])];
                for (int j = 0; j < l.Count; j++) { int u = l[j]; if (u == t || slot[u] != old) continue; int c; hits.TryGetValue(u, out c); hits[u] = c + 1; }
            }
            foreach (KeyValuePair<int, int> kv in hits) if (kv.Value >= 2 && slot[kv.Key] == old) { slot[kv.Key] = s; q.Enqueue(kv.Key); }
        }
        return true;
    }
    public int NearestTri(float x, float y, float z, float maxDist) { int j = grid.Nearest(x, y, z, maxDist); return j < 0 ? -1 : j / 4; }
    // For another shoe mesh: which triangle of this shoe matches each of its triangles.
    // The other mesh can have a different size, so positions are compared as fractions of each shoe's own box.
    public int[] MapMesh(float[] otherPos, bool mirrorX)
    {
        int n = otherPos.Length / 9; int[] map = new int[n];
        float ax = 1e9f, ay = 1e9f, az = 1e9f, bx = -1e9f, by = -1e9f, bz = -1e9f;
        for (int i = 0; i < otherPos.Length; i += 3)
        {
            float x = otherPos[i], y = otherPos[i + 1], z = otherPos[i + 2];
            if (x < ax) ax = x; if (x > bx) bx = x; if (y < ay) ay = y; if (y > by) by = y; if (z < az) az = z; if (z > bz) bz = z;
        }
        for (int t = 0; t < n; t++)
        {
            int o = 9 * t;
            float fx = ((otherPos[o] + otherPos[o + 3] + otherPos[o + 6]) / 3f - ax) / (bx - ax);
            float fy = ((otherPos[o + 1] + otherPos[o + 4] + otherPos[o + 7]) / 3f - ay) / (by - ay);
            float fz = ((otherPos[o + 2] + otherPos[o + 5] + otherPos[o + 8]) / 3f - az) / (bz - az);
            if (mirrorX) fx = 1f - fx;
            int j = NearestTri(minx + fx * (maxx - minx), miny + fy * (maxy - miny), minz + fz * (maxz - minz), 10f);
            map[t] = j < 0 ? 0 : j;
        }
        return map;
    }
    public void ResetLayout() { Array.Copy(baseSlot, slot, slot.Length); }
}
