using System.Collections.Generic;
using System.Linq;

namespace AsusFanControl.Core
{
    public class FanCurvePoint
    {
        public int Temperature { get; set; }
        public int Speed { get; set; }

        public FanCurvePoint(int temperature, int speed)
        {
            Temperature = temperature;
            Speed = speed;
        }

        public FanCurvePoint() { }
    }

    public class FanCurve
    {
        public List<FanCurvePoint> Points { get; set; } = new List<FanCurvePoint>();

        public int GetTargetSpeed(int currentTemp)
        {
            if (Points == null || Points.Count == 0)
                return 0;

            // Points are kept sorted by temperature (sorted on load and after edits).
            // If below first point
            if (currentTemp <= Points[0].Temperature)
                return Points[0].Speed;

            // If above last point
            if (currentTemp >= Points[Points.Count - 1].Temperature)
                return Points[Points.Count - 1].Speed;

            // Interpolate
            for (int i = 0; i < Points.Count - 1; i++)
            {
                var p1 = Points[i];
                var p2 = Points[i + 1];

                if (currentTemp >= p1.Temperature && currentTemp <= p2.Temperature)
                {
                    if (p1.Temperature == p2.Temperature)
                        return p2.Speed;

                    // Linear interpolation: speed = s1 + (s2 - s1) * (temp - t1) / (t2 - t1)
                    double tRatio = (double)(currentTemp - p1.Temperature) / (p2.Temperature - p1.Temperature);
                    return (int)(p1.Speed + (p2.Speed - p1.Speed) * tRatio);
                }
            }

            return Points[Points.Count - 1].Speed;
        }

        public override string ToString()
        {
            // Simple serialization: "temp:speed,temp:speed"
            return string.Join(",", Points.Select(p => $"{p.Temperature}:{p.Speed}"));
        }

        public static FanCurve FromString(string data)
        {
            var curve = new FanCurve();
            if (string.IsNullOrWhiteSpace(data))
                return curve;

            var parts = data.Split(',');
            foreach (var part in parts)
            {
                var kv = part.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[0], out int t) && int.TryParse(kv[1], out int s))
                {
                    curve.Points.Add(new FanCurvePoint(t, s));
                }
            }
            curve.Points.Sort((a, b) => a.Temperature.CompareTo(b.Temperature));
            return curve;
        }
    }
}
