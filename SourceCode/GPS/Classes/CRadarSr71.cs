using System;
using System.Collections.Generic;

namespace AgOpenGPS
{
    /// <summary>
    /// Парсер радара SR71 / AFM711 (Protocol EN 1.5)
    /// NodeID = 1  → CAN ID 0x61A / 0x61B
    /// </summary>
    public class RadarSr71
    {
        public const uint ID_HEADER = 0x61A;
        public const uint ID_OBJECT = 0x61B;

        public class RadarObject
        {
            public int Id;
            public double X;   // lateral (m)
            public double Y;   // longitudinal (m)
            public double Vx;
            public double Vy;
            public int DynProp;
            public double Rcs;
        }

        public List<RadarObject> Objects = new List<RadarObject>();

        int expectedObjects = 0;
        int receivedObjects = 0;
        ushort measurementCounter = 0;

        public bool FrameComplete =>
                expectedObjects > 0 && receivedObjects == expectedObjects;

        /// <summary>
        /// Вызывать для каждого CAN кадра радара
        /// </summary>
        public void ProcessFrame(uint canId, byte[] data)
        {
            if (canId == ID_HEADER)
                ParseHeader(data);
            else if (canId == ID_OBJECT)
                ParseObject(data);
        }

        // ===== 0x61A =====
        void ParseHeader(byte[] d)
        {
            expectedObjects = d[0];
            measurementCounter = (ushort)(d[2] | (d[3] << 8));

            Objects.Clear();
            receivedObjects = 0;

            System.Diagnostics.Debug.WriteLine(
                $"RADAR HEADER: objects={expectedObjects}, measCnt={measurementCounter}");
        }

        // ===== 0x61B =====
        void ParseObject(byte[] d)
        {
            if (receivedObjects >= expectedObjects)
                return;

            RadarObject obj = new RadarObject();

            obj.Id = d[0];

            // ---- DistLong (m) ----
            int rawDistLong = (d[1] << 5) | (d[2] >> 3);
            obj.Y = rawDistLong * 0.1 - 500.0;

            // ---- DistLat (m) ----
            int rawDistLat = ((d[2] & 0x07) << 8) | d[3];
            obj.X = rawDistLat * 0.1 - 102.3;

            // ---- VrelLong (m/s) ----
            int rawVLong = (d[4] << 2) | (d[5] >> 6);
            obj.Vy = rawVLong * 0.25 - 128.0;

            // ---- VrelLat (m/s) ----
            int rawVLat = ((d[5] & 0x3F) << 3) | (d[6] >> 5);
            obj.Vx = rawVLat * 0.25 - 64.0;

            // ---- Dynamic property ----
            obj.DynProp = d[6] & 0x07;

            // ---- RCS ----
            obj.Rcs = d[7] * 0.5 - 64.0;

            Objects.Add(obj);
            receivedObjects++;

            System.Diagnostics.Debug.WriteLine(
                $"OBJ {obj.Id}: X={obj.X:F1}m Y={obj.Y:F1}m Vx={obj.Vx:F1} Vy={obj.Vy:F1}");
        }
        /// <summary>
        /// Преобразовать объекты радара в формат CRadar
        /// Вызывать 1 раз после получения всех объектов кадра
        /// </summary>
        public List<CRadar.RadarObject> GetCRadarObjects()
        {
            List<CRadar.RadarObject> list = new List<CRadar.RadarObject>();

            foreach (RadarObject o in Objects)
            {
                CRadar.RadarObject ro = new CRadar.RadarObject();
                ro.X = o.X;   // lateral → вправо
                ro.Y = o.Y;   // longitudinal → вперёд

                list.Add(ro);
            }

            return list;
        }

    }
}
