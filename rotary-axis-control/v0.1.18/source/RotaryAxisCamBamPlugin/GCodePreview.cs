using CamBam.CAD;
using CamBam.Geom;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace GRBLMachine
{
  /// <summary>
  /// Lightweight, read-only drawing preview for the G0/G1 motion in a sent NC file.
  /// Preview geometry is kept out of the user's CAD document.
  /// </summary>
  internal sealed class GCodePreview
  {
    private const int MaximumPreviewPoints = 250000;
    private static readonly Regex Words = new Regex(@"([A-Z])\s*([-+]?(?:\d*\.\d+|\d+\.?\d*))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly CultureInfo EnUs = CultureInfo.InvariantCulture;

    internal readonly List<Polyline> Rapids = new List<Polyline>();
    internal readonly List<Polyline> Cuts = new List<Polyline>();
    internal double MinX = double.PositiveInfinity;
    internal double MinY = double.PositiveInfinity;
    internal double MaxX = double.NegativeInfinity;
    internal double MaxY = double.NegativeInfinity;
    internal int MotionCount;
    internal int ArcMovesSkipped;
    internal bool Truncated;

    internal static GCodePreview Load(string fileName, Units drawingUnits)
    {
      GCodePreview preview = new GCodePreview();
      double x = 0, y = 0, z = 0, unitScale = 1;
      bool absolute = true;
      int motion = 0;
      Polyline current = null;
      List<Polyline> destination = null;
      int retainedPoints = 0;
      int stride = Math.Max(1, (int)Math.Ceiling(new FileInfo(fileName).Length / 4000000.0));
      int moveNumber = 0;
      preview.Truncated = stride > 1;

      using (StreamReader reader = File.OpenText(fileName))
      {
        string raw;
        while ((raw = reader.ReadLine()) != null)
        {
        string line = StripComments(raw).ToUpperInvariant();
        if (line.Length == 0) continue;

        Dictionary<char, double> values = new Dictionary<char, double>();
        foreach (Match match in Words.Matches(line))
        {
          double value;
          if (double.TryParse(match.Groups[2].Value, NumberStyles.Float, EnUs, out value))
          {
            char letter = Char.ToUpperInvariant(match.Groups[1].Value[0]);
            values[letter] = value;
            if (letter == 'G')
            {
              if (Math.Abs(value - 20) < 0.0001) unitScale = ConvertLength(1, true, drawingUnits);
              else if (Math.Abs(value - 21) < 0.0001) unitScale = ConvertLength(1, false, drawingUnits);
              else if (Math.Abs(value - 90) < 0.0001) absolute = true;
              else if (Math.Abs(value - 91) < 0.0001) absolute = false;
              else if (Math.Abs(value - 0) < 0.0001) motion = 0;
              else if (Math.Abs(value - 1) < 0.0001) motion = 1;
              else if (Math.Abs(value - 2) < 0.0001 || Math.Abs(value - 3) < 0.0001) motion = 2;
            }
          }
        }

        bool hasXYZ = values.ContainsKey('X') || values.ContainsKey('Y') || values.ContainsKey('Z');
        if (!hasXYZ || (motion != 0 && motion != 1 && motion != 2)) continue;

        double nx = values.ContainsKey('X') ? (absolute ? values['X'] * unitScale : x + values['X'] * unitScale) : x;
        double ny = values.ContainsKey('Y') ? (absolute ? values['Y'] * unitScale : y + values['Y'] * unitScale) : y;
        double nz = values.ContainsKey('Z') ? (absolute ? values['Z'] * unitScale : z + values['Z'] * unitScale) : z;
        if (motion == 2)
        {
          // Do not draw arcs as misleading straight chords. Keep the modal position
          // correct so subsequent relative linear moves still start at the right point.
          preview.ArcMovesSkipped++;
          x = nx; y = ny; z = nz;
          current = null;
          destination = null;
          continue;
        }
        if (Math.Abs(nx - x) + Math.Abs(ny - y) + Math.Abs(nz - z) > 0.0000001)
        {
          List<Polyline> nextDestination = motion == 0 ? preview.Rapids : preview.Cuts;
          if (destination != nextDestination || current == null)
          {
            current = new Polyline();
            current.Add(x, y, z);
            current.Add(nx, ny, nz);
            nextDestination.Add(current);
            destination = nextDestination;
            retainedPoints += 2;
          }
          else
          {
            // Large raster files can contain millions of moves. Keep a bounded preview,
            // sampling intermediate points while always including the final position.
            moveNumber++;
            if (moveNumber % stride == 0 && retainedPoints < MaximumPreviewPoints)
            {
              current.Add(nx, ny, nz);
              retainedPoints++;
            }
            else if (retainedPoints >= MaximumPreviewPoints)
              preview.Truncated = true;
          }

          preview.MotionCount++;
          preview.MinX = Math.Min(preview.MinX, nx);
          preview.MinY = Math.Min(preview.MinY, ny);
          preview.MaxX = Math.Max(preview.MaxX, nx);
          preview.MaxY = Math.Max(preview.MaxY, ny);
        }
        x = nx; y = ny; z = nz;
        }
      }

      return preview;
    }

    private static string StripComments(string line)
    {
      int semicolon = line.IndexOf(';');
      if (semicolon >= 0) line = line.Substring(0, semicolon);
      while (true)
      {
        int start = line.IndexOf('(');
        if (start < 0) break;
        int end = line.IndexOf(')', start + 1);
        line = end < 0 ? line.Substring(0, start) : line.Remove(start, end - start + 1);
      }
      return line;
    }

    private static double ConvertLength(double value, bool inches, Units drawingUnits)
    {
      double millimeters = inches ? value * 25.4 : value;
      switch (drawingUnits)
      {
        case Units.Inches: return millimeters / 25.4;
        case Units.Centimeters: return millimeters / 10;
        case Units.Meters: return millimeters / 1000;
        case Units.Thousandths: return millimeters / 25.4 * 1000;
        default: return millimeters;
      }
    }
  }
}
