using System;
using System.Collections.Generic;
namespace AgOpenGPS
{
    /// <summary>
    /// Парсер радара SR71 / AFM711 (Protocol EN 1.5)
    /// NodeID = 1 → CAN ID 0x61A / 0x61B
    /// </summary>
    public class RadarSr71
    {
        public const uint ID_HEADER = 0x61A;
        public const uint ID_OBJECT = 0x61B;
        // ===== RAW RADAR OBJECT =====
        public class RadarObject
        {
            public int Id;
            public double X; // lateral (m)
            public double Y; // longitudinal (m)
            public double Vx;
            public double Vy;
            public int DynProp;
            public double Rcs;
        }
        // ===== THREAD SAFETY =====
        private readonly object locker = new object();
        // Буфер кадра (заполняется CAN-потоком)
        private readonly List<RadarObject> frameObjects = new List<RadarObject>();
        // Стабильный буфер (читается UI)
        private readonly List<RadarObject> stableObjects = new List<RadarObject>();
        private int expectedObjects = 0;
        private int receivedObjects = 0;
        private ushort measurementCounter = 0;
        // ===== SETTINGS =====
        public double RadarOffsetY = 0.0; // смещение радара вперед
        public double ToolHalfWidth = 0.0;
        public double MaxDistanceY = 30.0;
        public double SteerAngleRad = 0.0;
        // ===== ROTATION =====
        private static void Rotate(double x, double y, double angleRad,
out double xr, out double yr)
        {
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);
            xr = x * cos + y * sin;
            yr = y * cos - x * sin;
        }
        // ===== ENTRY POINT =====
        public void ProcessFrame(uint canId, byte[] data)
        {
            if (canId == ID_HEADER)
                ParseHeader(data);
            else if (canId == ID_OBJECT)
                ParseObject(data);
        }
        // ===== HEADER =====
        private void ParseHeader(byte[] d)
        {
            lock (locker)
            {
                expectedObjects = d[0];
                measurementCounter = (ushort)(d[2] | (d[3] << 8));
                frameObjects.Clear();
                receivedObjects = 0;
            }
        }
        // ===== OBJECT =====
        private void ParseObject(byte[] d)
        {
            lock (locker)
            {
                if (receivedObjects >= expectedObjects)
                    return;
                RadarObject obj = new RadarObject
                {
                    Id = d[0]
                };
                int rawDistLong = (d[1] << 5) | (d[2] >> 3);
                obj.Y = rawDistLong * 0.1 - 500.0;
                int rawDistLat = ((d[2] & 0x07) << 8) | d[3];
                obj.X = rawDistLat * 0.1 - 102.3;
                int rawVLong = (d[4] << 2) | (d[5] >> 6);
                obj.Vy = rawVLong * 0.25 - 128.0;
                int rawVLat = ((d[5] & 0x3F) << 3) | (d[6] >> 5);
                obj.Vx = rawVLat * 0.25 - 64.0;
                obj.DynProp = d[6] & 0x07;
                obj.Rcs = d[7] * 0.5 - 64.0;
                frameObjects.Add(obj);
                receivedObjects++;
            }
        }
        // ===== FRAME COMPLETE =====
        public bool FrameComplete
        {
            get
            {
                lock (locker)
                    return receivedObjects >= expectedObjects;
            }
        }
        // ===== COMMIT FRAME =====
        public void CommitFrame()
        {
            lock (locker)
            {
                stableObjects.Clear();
                stableObjects.AddRange(frameObjects);
                expectedObjects = 0;
                receivedObjects = 0;
            }
        }
        // ===== STANDARD MODE =====
        public List<CRadar.RadarObject> GetCRadarObjects()
        {
            List<RadarObject> snapshot;
            lock (locker)
                snapshot = new List<RadarObject>(stableObjects);
            List<CRadar.RadarObject> list = new List<CRadar.RadarObject>();
            foreach (var o in snapshot)
            {
                double x = o.X;
                double y = o.Y + RadarOffsetY;
                Rotate(x, y, -SteerAngleRad, out double xr, out double yr);
                if (Math.Abs(xr) > ToolHalfWidth)
                    continue;
                Rotate(xr, yr, SteerAngleRad, out double xf, out double yf);
                if (yf < 0 || yf > MaxDistanceY)
                    continue;
                double speed = Math.Sqrt(o.Vx * o.Vx + o.Vy * o.Vy);

                RadarClass cls;

                if (o.Rcs < -20)
                    cls = RadarClass.Unknown;
                else if (o.Rcs < -5)
                    cls = RadarClass.HumanLike;
                else if (o.Rcs < 15)
                    cls = RadarClass.VehicleLike;
                else
                    cls = RadarClass.LargeStatic;

                list.Add(new CRadar.RadarObject
                {
                    X = xf,
                    Y = yf,
                    Speed = speed,
                    Class = cls
                });
            }
            return list;
        }
        // ===== YOUTURN MODE =====
        public List<CRadar.RadarObject> GetObjectsOnYouTurnPath(
    List<vec3> ytLocalPath,
    double halfWidth)
        {
            List<RadarObject> snapshot;
            lock (locker)
                snapshot = new List<RadarObject>(stableObjects);

            List<CRadar.RadarObject> dangerous = new List<CRadar.RadarObject>();

            if (ytLocalPath == null || ytLocalPath.Count < 2)
                return dangerous;

            foreach (var obj in snapshot)
            {
                // === ПРИВОДИМ ОБЪЕКТ В ЛОКАЛЬНУЮ СИСТЕМУ ТРАКТОРА ===
                RadarToVehicleLocal(obj.X, obj.Y, out double ox, out double oy);

                // отбрасываем всё, что позади
                if (oy < 0 || oy > MaxDistanceY)
                    continue;

                // проверяем попадание в коридор YouTurn
                for (int i = 0; i < Math.Min(ytLocalPath.Count - 1, 50); i++)
                {
                    double dist = DistToSegment(
                        ox, oy,
                        ytLocalPath[i].easting, ytLocalPath[i].northing,
                        ytLocalPath[i + 1].easting, ytLocalPath[i + 1].northing);

                    if (dist < halfWidth)
                    {
                        double speed = Math.Abs(obj.Vy);

                        dangerous.Add(new CRadar.RadarObject
                        {
                            X = ox,
                            Y = oy,
                            Speed = speed,
                            Class = RadarClass.VehicleLike
                        });
                        break;
                    }
                }
            }

            return dangerous;
        }

        private void RadarToVehicleLocal(
    double rx, double ry,
    out double lx, out double ly)
        {
            // смещение радара вперёд
            double y = ry + RadarOffsetY;
            double x = rx;

            // поворот в систему машины
            Rotate(x, y, -SteerAngleRad, out lx, out ly);
        }
        // ===== DISTANCE =====
        private static double DistToSegment(
double px, double py,
double x1, double y1,
double x2, double y2)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            if (dx == 0 && dy == 0)
                return Math.Sqrt((px - x1) * (px - x1) +
                (py - y1) * (py - y1));
            double t = ((px - x1) * dx + (py - y1) * dy) /
            (dx * dx + dy * dy);
            t = Math.Max(0, Math.Min(1, t));
            double cx = x1 + t * dx;
            double cy = y1 + t * dy;
            return Math.Sqrt((px - cx) * (px - cx) +
            (py - cy) * (py - cy));
        }
    }
}