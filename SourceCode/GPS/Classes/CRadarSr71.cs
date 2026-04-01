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
        public byte SensorId = 1;
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
        private int expectedObjects = 0;
        private int receivedObjects = 0;
        private ushort measurementCounter = 0;
        // ===== TRACKING =====
        private const int AppearFrames = 2;
        private const int HoldFrames = 3;
        private int frameId = 0;
        private readonly Dictionary<int, TrackState> tracks = new Dictionary<int, TrackState>();

        private class TrackState
        {
            public RadarObject Obj;
            public int SeenStreak;
            public bool Confirmed;
            public int LastSeenFrame;
        }
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
                    return expectedObjects > 0 && receivedObjects >= expectedObjects;
            }
        }
        // ===== COMMIT FRAME =====
        public void CommitFrame()
        {
            lock (locker)
            {
                expectedObjects = 0;
                receivedObjects = 0;
                UpdateTracks(frameObjects);
            }
        }

        public void Clear()
        {
            lock (locker)
            {
                frameObjects.Clear();
                expectedObjects = 0;
                receivedObjects = 0;
                measurementCounter = 0;
                tracks.Clear();
                frameId = 0;
            }
        }

        private static double ComputeTargetSpeed()
        {
            return 0.0;
        }
        // ===== TRACKING =====
        private void UpdateTracks(List<RadarObject> frameObjs)
        {
            frameId++;
            HashSet<int> seen = new HashSet<int>();

            foreach (var obj in frameObjs)
            {
                seen.Add(obj.Id);
                TrackState state;
                if (!tracks.TryGetValue(obj.Id, out state))
                {
                    state = new TrackState { SeenStreak = 1, Confirmed = false };
                    state.LastSeenFrame = frameId - 1; // чтобы условие ниже дало streak++
                }

                if (state.LastSeenFrame == frameId - 1)
                    state.SeenStreak++;
                else
                    state.SeenStreak = 1;

                state.Obj = obj;
                state.LastSeenFrame = frameId;

                if (state.SeenStreak >= AppearFrames)
                    state.Confirmed = true;

                tracks[obj.Id] = state;
            }

            List<int> toRemove = null;
            foreach (var kvp in tracks)
            {
                if (seen.Contains(kvp.Key))
                    continue;

                TrackState state = kvp.Value;
                if (!state.Confirmed)
                {
                    if (toRemove == null) toRemove = new List<int>();
                    toRemove.Add(kvp.Key);
                    continue;
                }

                if (frameId - state.LastSeenFrame > HoldFrames)
                {
                    if (toRemove == null) toRemove = new List<int>();
                    toRemove.Add(kvp.Key);
                }
            }

            if (toRemove != null)
            {
                foreach (int id in toRemove)
                    tracks.Remove(id);
            }
        }

        private List<RadarObject> GetFilteredSnapshot()
        {
            lock (locker)
            {
                List<RadarObject> list = new List<RadarObject>();
                foreach (var kvp in tracks)
                {
                    if (kvp.Value.Confirmed)
                        list.Add(kvp.Value.Obj);
                }
                return list;
            }
        }
        // ===== STANDARD MODE =====
        public List<CRadar.RadarObject> GetCRadarObjects()
        {
            List<RadarObject> snapshot = GetFilteredSnapshot();
            List<CRadar.RadarObject> list = new List<CRadar.RadarObject>();
            foreach (var o in snapshot)
            {
                double x = o.X;
                double y = o.Y + RadarOffsetY;
                Rotate(x, y, -SteerAngleRad, out double xr, out double yr);
                if (Math.Abs(xr) > ToolHalfWidth)
                    continue;
                if (yr < 0 || yr > MaxDistanceY)
                    continue;
                double speed = ComputeTargetSpeed();
                list.Add(new CRadar.RadarObject
                {
                    X = xr,
                    Y = yr,
                    Speed = speed,
                    Rcs = o.Rcs
                });
            }
            return list;
        }
        // ===== YOUTURN MODE =====
        public List<CRadar.RadarObject> GetObjectsOnYouTurnPath(
    List<vec3> ytLocalPath,
    double halfWidth)
        {
            List<RadarObject> snapshot = GetFilteredSnapshot();

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
                        double speed = ComputeTargetSpeed();

                        dangerous.Add(new CRadar.RadarObject
                        {
                            X = ox,
                            Y = oy,
                            Speed = speed,
                            Rcs = obj.Rcs
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
            double y = ry + RadarOffsetY;
            double x = rx;
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

        public List<CRadar.RadarObject> GetAllRadarObjects()
        {
            List<RadarObject> snapshot = GetFilteredSnapshot();

            List<CRadar.RadarObject> list = new List<CRadar.RadarObject>();
            foreach (var o in snapshot)
            {
                double x = o.X;
                double y = o.Y + RadarOffsetY;
                Rotate(x, y, -SteerAngleRad, out double xr, out double yr);

                if (yr < 0 || yr > MaxDistanceY)
                    continue;

                double speed = ComputeTargetSpeed();

                list.Add(new CRadar.RadarObject
                {
                    X = xr,
                    Y = yr,
                    Speed = speed,
                    Rcs = o.Rcs
                });
            }

            return list;
        }
    }
}
