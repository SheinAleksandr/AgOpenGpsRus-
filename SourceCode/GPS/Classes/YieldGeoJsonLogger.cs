using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AgOpenGPS
{
    public static class YieldGeoJsonLogger
    {
        private const string GeoJsonlFileName = "YieldLog.geojsonl";
        private const string GeoJsonFileName = "YieldLog.geojson";

        public static void AppendFeatureLine(
            string fieldDirectory,
            DateTime timeUtc,
            double latitude,
            double longitude,
            double speedKmh,
            double yieldCpha,
            double toolWidthM,
            double headingDeg,
            string crop,
            double scaleK,
            double delaySec)
        {
            if (string.IsNullOrWhiteSpace(fieldDirectory))
                return;

            Directory.CreateDirectory(fieldDirectory);
            string path = Path.Combine(fieldDirectory, GeoJsonlFileName);

            string line = BuildFeatureJson(
                timeUtc,
                latitude,
                longitude,
                speedKmh,
                yieldCpha,
                toolWidthM,
                headingDeg,
                crop,
                scaleK,
                delaySec);

            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }

        public static bool TryBuildFeatureCollection(string fieldDirectory, out string resultPath)
        {
            resultPath = string.Empty;
            if (string.IsNullOrWhiteSpace(fieldDirectory))
                return false;

            string sourcePath = Path.Combine(fieldDirectory, GeoJsonlFileName);
            if (!File.Exists(sourcePath))
                return false;

            string targetPath = Path.Combine(fieldDirectory, GeoJsonFileName);
            string[] lines = File.ReadAllLines(sourcePath, Encoding.UTF8);

            using (StreamWriter writer = new StreamWriter(targetPath, false, new UTF8Encoding(true)))
            {
                writer.Write("{\"type\":\"FeatureCollection\",\"features\":[");
                bool first = true;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i]?.Trim();
                    if (string.IsNullOrEmpty(line))
                        continue;

                    if (!first) writer.Write(',');
                    writer.Write(line);
                    first = false;
                }
                writer.Write("]}");
            }

            resultPath = targetPath;
            return true;
        }

        private static string BuildFeatureJson(
            DateTime timeUtc,
            double latitude,
            double longitude,
            double speedKmh,
            double yieldCpha,
            double toolWidthM,
            double headingDeg,
            string crop,
            double scaleK,
            double delaySec)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            string timestamp = timeUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", ci);
            string safeCrop = JsonEscape(crop ?? string.Empty);

            return "{\"type\":\"Feature\",\"geometry\":{\"type\":\"Point\",\"coordinates\":[" +
                   longitude.ToString("F7", ci) + "," + latitude.ToString("F7", ci) +
                   "]},\"properties\":{\"time_utc\":\"" + timestamp +
                   "\",\"speed_kmh\":" + speedKmh.ToString("F2", ci) +
                   ",\"yield_cpha\":" + yieldCpha.ToString("F2", ci) +
                   ",\"tool_width_m\":" + toolWidthM.ToString("F2", ci) +
                   ",\"heading_deg\":" + headingDeg.ToString("F1", ci) +
                   ",\"crop\":\"" + safeCrop +
                   "\",\"k\":" + scaleK.ToString("F4", ci) +
                   ",\"delay_s\":" + delaySec.ToString("F1", ci) +
                   "}}";
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
