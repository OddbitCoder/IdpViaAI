//#define TEST_SAMPLE

using System.Drawing;
using System.Drawing.Imaging;

// settings

const int thresh = 50; // determines good via candidates (results in less vias if lower) 
const int centerW = 3; // how much do we weigh the center more than the rim? (1 = both equally, >1 = center is weighed higher)

#if TEST_SAMPLE
const int iter = 3; // the number of self-training iterations (results in less vias if lower)
#else
const int iter = 1;
#endif
const int take = 2; // take top N closest vias when computing average similarity (results in less vias if higher)

const int maskViolThresh = 4; // tolerance to violating the mask (less tolerant if lower)

const int smear = 8; // propagates a weight in a feature vector to its neighboring dimensions (results in less vias if lower)

const int conflDiam = 20; // conflict diameter (vias are allowed to lie closer together if lower)
const int dVia = 28; // via copper diameter
const int dHole = 18; // via hole diameter

#if TEST_SAMPLE
const string basePath = @"C:\Work\ViaCV\data\sample\";
const string baseFlNm = "front-300dpi-test";
const string maskFlNm = "mask-300dpi-test.png";
#else
const string basePath = @"C:\Work\ViaCV\data\";
const string baseFlNm = "front-300dpi";
const string maskFlNm = "mask-300dpi.png";
#endif

// dot product similarity measure (since vectors are normalized, this is effectively cosine similarity measure)
static double Similarity(double[] v1, double[] v2)
{
    return v1.Select((x, i) => x * v2[i]).Sum();
}

// adds weight w to position/dimension pos in vec, and smears it 
static void AddToVec(double[] vec, int pos, int w)
{
    int from = Math.Max(0, pos - smear);
    int to = Math.Min(vec.Length - 1, pos + smear);
    for (int i = from; i <= to; i++)
    {
        vec[i] += w;
    }
}

// computes a feature vector for the dVia x dVia square at (_x,_y); stops if the mask is violated [too much] 
static double[] ComputeFeatureVector(Bitmap img, Bitmap mask, int _x, int _y, out int maskViol, int maskViolLimit = int.MaxValue)
{
    maskViol = 0;
    var vec = new double[256 * 3 * 2];
    var c = (x: _x + dVia/2d, y: _y + dVia/2d);
    for (int x = _x; x < _x + dVia; x++)
    {
        for (int y = _y; y < _y + dVia; y++) 
        {
            var pt = (x: (double)x, y: (double)y);
            // dist pt to c
            double d = Math.Sqrt(Math.Pow(pt.x - c.x, 2) + Math.Pow(pt.y - c.y, 2));
            if (d <= dVia / 2d)
            {
                maskViol += mask.GetPixel(x, y).R > 128 ? 1 : 0;
                if (maskViol >= maskViolLimit) { return null; }
            }
            if (d <= dHole/2d) // inner circle 
            {
                var px = img.GetPixel(x, y);
                // R
                AddToVec(vec, px.R / 1 + 256 * 0, centerW);
                // G
                AddToVec(vec, px.G / 1 + 256 * 1, centerW);
                // B
                AddToVec(vec, px.B / 1 + 256 * 2, centerW);
            } 
            else if (d <= dVia/2d) // outer rim
            {
                var px = img.GetPixel(x, y);
                // R
                AddToVec(vec, px.R / 1 + 256 * 3, 1);
                // G
                AddToVec(vec, px.G / 1 + 256 * 4, 1);
                // B
                AddToVec(vec, px.B / 1 + 256 * 5, 1);
            }
        }   
    }
    // normalize vector
    double len = Math.Sqrt(vec.Select(x => x * x).Sum());
    vec = vec.Select(x => x / len).ToArray();
    return vec;
}

// checks if (x1,y1) and (x2,y2) lie closer than d to each other
static bool Conflict(int x1, int y1, int x2, int y2, int d)
{
    return Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2) <= Math.Pow(d, 2); 
}

// filters out all items that are closer than d to any of fixedItems
static void FilterOut(IEnumerable<(int x, int y)> fixedItems, ref List<(byte sim, double[] vec, int x, int y)> items, int d)
{
    var newList = new List<(byte sim, double[] vec, int x, int y)>();
    items = items
        .Where(a => !fixedItems.Any(b => Conflict(a.x, a.y, b.x, b.y, d)))
        .ToList();
}

#if TEST_SAMPLE
// gold standard for the test sample
var goldStd = new List<(int x, int y)>
{
    (396, 86),
    (15, 160),
    (669, 90),
    (758, 89)
};
#else
// gold standard for the real scan at 300 dpi (front side)
var goldStd = new List<(int x, int y)>
{
    (240, 1133),
    (473, 1125),
    (1005, 1140),
    (1350, 1125),
    (1245, 1560),
    (1817, 1560),
    (1590, 2116),
    (1470, 2086),
    (106, 239)
};
#endif

// PCB image
var img = (Bitmap)Image.FromFile(Path.Combine(basePath, $"{baseFlNm}.jpg"));

// through-hole tabu mask image
var mask = (Bitmap)Image.FromFile(Path.Combine(basePath, maskFlNm));

// gold standard profiles (feature vectors)
var profl = new List<(int x, int y, double[] vec)>(
    goldStd.Select(a => (a.x, a.y, ComputeFeatureVector(img, mask, a.x, a.y, out int _)))
);

// target image (visualizes results)
var resVis = (Bitmap)Image.FromFile(Path.Combine(basePath, $"{baseFlNm}.jpg"));

// list of vias (results)
var vias = new List<(int x, int y, double[] vec)>(profl);

// compare each dVia x dVia square in img to profiles

for (int k = 0; k < iter; k++)
{
    Console.WriteLine($"Iter {k + 1}");

    // good via candidates
    var cand = new List<(byte sim, double[] vec, int x, int y)>();

    for (int x = 0; x < img.Width - dVia; x++)
    {
        for (int y = 0; y < img.Height - dVia; y++)
        {
            var vec = ComputeFeatureVector(img, mask, x, y, out int maskViol, maskViolThresh);
            // if we violate the mask, we skip this position
            if (maskViol >= maskViolThresh) { continue; }
            // compute average similarity
            double sim = profl.Select(x => Similarity(x.vec, vec))
                .OrderByDescending(x => x)
                .Take(take)
                .Average();
            // average sim as byte (0..255)
            byte sb = (byte)(Math.Min(sim, 1) * 255d);
            // via is a good candidate if it passes the thresh
            if (sb >= 255 - thresh) 
            {
                cand.Add((sb, vec, x, y));
            }
            //resVis.SetPixel(x + 14, y + 14, Color.FromArgb(255, sb, sb, sb));
        }
        Console.Write(".");
    }

    FilterOut(vias.Select(a => (a.x, a.y)), ref cand, conflDiam);
    
    cand = cand.OrderByDescending(x => x.sim).ToList();
    while (cand.Count > 0)
    {
        // take top candidate
        var topCand = cand[0];
        cand.RemoveAt(0);
        // convert it to via
        vias.Add((topCand.x, topCand.y, topCand.vec));
        // remove conflicting candidates
        FilterOut(new[] { (topCand.x, topCand.y) }, ref cand, conflDiam);
    }

    // "learn" new vias for the next iteration
    profl.Clear();
    profl.AddRange(vias);
    Console.WriteLine($"({profl.Count})");

} // end of iter

// output the resulting vias

using (var sw = new StreamWriter(Path.Combine(basePath, $"{baseFlNm}-cv.txt")))
{
    var g = Graphics.FromImage(resVis);
    foreach (var via in vias)
    {
        g.DrawEllipse(Pens.Red, via.x, via.y, dVia, dVia);
        sw.WriteLine($"({via.x},{via.y})");
    }
}

resVis.Save(Path.Combine(basePath, $"{baseFlNm}-cv.png"), ImageFormat.Png);
