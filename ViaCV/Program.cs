using System.Drawing;
using System.Drawing.Imaging;

// settings

int thresh = 50; // lower thresh, less vias 
int centerW = 3; // how much do we weight center more than rim? (1 = both equally)

int iter = 1;// 2; // less iterations, less vias but ofc faster
int take = 2; // more here, less vias

int clashThresh = 4;

// leave as is (for now)

int smear = 8;// 5; // lower smear, less vias 

int conflDiam = 20; // lower conflict diameter => vias are allowed to lie closer together
int dVia = 28; // via diameter
int dHole = 18; // via "hole" diameter

double Similarity(double[] v1, double[] v2)
{
    double s = 0;
    for (int i = 0; i < v1.Length; i++)
    { 
        s += v1[i] * v2[i];
    }
    return s;
}

void AddToVec(double[] vec, int pos, int w)
{
    int from = Math.Max(0, pos - smear);
    int to = Math.Min(vec.Length - 1, pos + smear);
    for (int i = from; i <= to; i++)
    {
        vec[i] += w;
    }
}

// load image
//var img = Image.FromFile(@"C:\Users\miha\Desktop\vezje-idp\v4\sample\front-300dpi-test.jpg");
var img = Image.FromFile(@"C:\Users\miha\Desktop\vezje-idp\v4\front-300dpi.jpg");

// load TH tabu image
//var thTabu = (Bitmap)Image.FromFile(@"C:\Users\miha\Desktop\vezje-idp\v4\sample\test-mask-300dpi.png");
var thTabu = (Bitmap)Image.FromFile(@"C:\Users\miha\Desktop\vezje-idp\v4\mask-300dpi.png");

double[] ComputeFeatureVector(Bitmap bmp, int _x, int _y, int d1, int d2, out int thClash, int thClashLimit = Int32.MaxValue)
{
    thClash = 0;
    var vec = new double[256 * 3 * 2];
    var c = (x: _x + d1/2d, y: _y + d1/2d);
    for (int x = _x; x < _x + d1; x++)
    {
        for (int y = _y; y < _y + d1; y++) 
        {
            var pt = (x: (double)x, y: (double)y);
            // dist p to c
            double d = Math.Sqrt(Math.Pow(pt.x - c.x, 2) + Math.Pow(pt.y - c.y, 2));
            if (d <= d1 / 2d)
            {
                thClash += thTabu.GetPixel(x, y).R > 128 ? 1 : 0;
                if (thClash >= thClashLimit) { return null; }
            }
            if (d <= d2/2d) // inner circle 
            {
                var px = bmp.GetPixel(x, y);
                // R
                AddToVec(vec, px.R / 1 + 256 * 0, centerW);
                // G
                AddToVec(vec, px.G / 1 + 256 * 1, centerW);
                // B
                AddToVec(vec, px.B / 1 + 256 * 2, centerW);
            } 
            else if (d <= d1/2d) // outer rim
            {
                var px = bmp.GetPixel(x, y);
                // R
                AddToVec(vec, px.R / 1 + 256 * 3, 1);
                // G
                AddToVec(vec, px.G / 1 + 256 * 4, 1);
                // B
                AddToVec(vec, px.B / 1 + 256 * 5, 1);
            }
        }   
    }
    // norm
    double len = 0;
    for (int i = 0; i < vec.Length; i++)
    {
        len += vec[i] * vec[i];
    }
    len = Math.Sqrt(len);
    for (int i = 0; i < vec.Length; i++)
    {
        vec[i] /= len;
    }
    return vec;
}

bool Conflict(int x1, int y1, int x2, int y2, int d)
{
    return Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2) <= Math.Pow(d, 2); 
}

//// gold standard for test sample
//var goldStd = new List<(int x, int y)>
//{
//    (396, 86),
//    (15, 160),
//    (669, 90),
//    (758, 89)
//};

// gold standard for front
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

var profl = new List<(int x, int y, double[] vec)>();

foreach (var item in goldStd)
{
    profl.Add((item.x, item.y, ComputeFeatureVector((Bitmap)img, item.x, item.y, dVia, dHole, out int _)));
};

// compare each dVia x dVia square to the profile
//var r = new Bitmap(img.Width, img.Height);
//var r = Image.FromFile(@"C:\Users\miha\Desktop\vezje-idp\v4\sample\front-300dpi-test.jpg");
var r = Image.FromFile(@"C:\Users\miha\Desktop\vezje-idp\v4\front-300dpi.jpg");

var vias = new List<(int x, int y, double[] vec)>();
vias.AddRange(profl);

for (int k = 0; k < iter; k++)
{
    Console.WriteLine($"iter {k + 1}");

    var cand = new List<(byte sim, double[] vec, int x, int y)>();

    for (int x = 0; x < img.Width - dVia; x++)
    {
        for (int y = 0; y < img.Height - dVia; y++)
        {
            var vec = ComputeFeatureVector((Bitmap)img, x, y, dVia, dHole, out int thClash, clashThresh);
            if (thClash >= clashThresh)
            {
              //  Console.WriteLine(thClash);
                continue;
            }
            double sim = 0;
            var sims = profl.Select(x => Similarity(x.vec, vec))
                .OrderByDescending(x => x)
                .Take(take);
            foreach (var s in sims)
            {
                sim += s;
            }
            sim /= sims.Count();
            byte sb = (byte)(Math.Min(sim, 1) * 255d);
            if (sb >= 255 - thresh) 
            {
                cand.Add((sb, vec, x, y));
            }
            ((Bitmap)r).SetPixel(x + 14, y + 14, Color.FromArgb(255, sb, sb, sb));
        }
        Console.Write(".");
    }

    var cand2 = new List<(byte sim, double[] vec, int x, int y)>();
    foreach (var item in cand)
    {
        bool confl = false;
        foreach (var via in vias)
        {
            if (Conflict(item.x, item.y, via.x, via.y, conflDiam))
            {
                confl = true;
                break;
            }
        }
        if (!confl)
        {
            cand2.Add(item);
        }
    }
    cand = cand2;

    cand = cand.OrderByDescending(x => x.sim).ToList();
    while (cand.Count > 0)
    {
        // take top candidate
        var topCand = cand[0];
        cand.RemoveAt(0);
        // convert it to via
        vias.Add((topCand.x, topCand.y, topCand.vec));
        // remove conflicting candidates
        cand2 = new List<(byte sim, double[] vec, int x, int y)>();
        foreach (var item in cand)
        {
            if (!Conflict(item.x, item.y, topCand.x, topCand.y, conflDiam))
            { 
                cand2.Add(item);
            }
        }
        cand = cand2;
    }

    profl.Clear();
    profl.AddRange(vias);
    Console.WriteLine($"profl size: {profl.Count} ");

} // iter

// draw vias

using (var sw = new StreamWriter(@"C:\Users\miha\Desktop\vezje-idp\v4\front-300dpi-cv.txt"))
{
    var g = Graphics.FromImage(r);
    foreach (var via in vias)
    {
        g.DrawEllipse(Pens.Red, via.x, via.y, dVia, dVia);
        sw.WriteLine($"({via.x},{via.y})");
    }
}

r.Save(@"C:\Users\miha\Desktop\vezje-idp\v4\front-300dpi-cv.jpg", ImageFormat.Png);

