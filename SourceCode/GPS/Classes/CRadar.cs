using OpenTK.Graphics.OpenGL;
using System.Collections.Generic;

namespace AgOpenGPS
{
    public enum RadarClass
    {
        Unknown,
        Small,
        HumanLike,
        VehicleLike,
        LargeStatic
    }

    public class CRadar
    {
        public class RadarObject
        {
            public double X;       // вправо от трактора (м)
            public double Y;       // вперёд (м)
            public double Speed;   // м/с
            public RadarClass Class;
        }

        private readonly List<RadarObject> objects = new List<RadarObject>();

        public void Update(List<RadarObject> newObjects)
        {
            lock (objects)
            {
                objects.Clear();
                objects.AddRange(newObjects);
            }
        }

        public void Draw()
        {
            lock (objects)
            {
                GL.PushAttrib(AttribMask.PointBit | AttribMask.CurrentBit);

                // ===== UNKNOWN / SMALL =====
                GL.PointSize(3);
                GL.Begin(PrimitiveType.Points);
                foreach (var o in objects)
                {
                    if (o.Class == RadarClass.Unknown || o.Class == RadarClass.Small)
                    {
                        SetColor(o);
                        GL.Vertex3(o.X, o.Y, 0.05);
                    }
                }
                GL.End();

                // ===== HUMAN =====
                GL.PointSize(6);
                GL.Begin(PrimitiveType.Points);
                foreach (var o in objects)
                {
                    if (o.Class == RadarClass.HumanLike)
                    {
                        SetColor(o);
                        GL.Vertex3(o.X, o.Y, 0.05);
                    }
                }
                GL.End();

                // ===== VEHICLE =====
                GL.PointSize(9);
                GL.Begin(PrimitiveType.Points);
                foreach (var o in objects)
                {
                    if (o.Class == RadarClass.VehicleLike)
                    {
                        SetColor(o);
                        GL.Vertex3(o.X, o.Y, 0.05);
                    }
                }
                GL.End();

                // ===== LARGE STATIC =====
                GL.PointSize(5);
                GL.Begin(PrimitiveType.Points);
                foreach (var o in objects)
                {
                    if (o.Class == RadarClass.LargeStatic)
                    {
                        SetColor(o);
                        GL.Vertex3(o.X, o.Y, 0.05);
                    }
                }
                GL.End();

                GL.PopAttrib();
            }
        }

        public void Clear()
        {
            lock (objects)
            {
                objects.Clear();
            }
        }

        // ===== ЦВЕТ ПО ДВИЖЕНИЮ =====
        private static void SetColor(RadarObject o)
        {
            if (o.Speed < -0.3)
                GL.Color3(1.0, 0.0, 0.0);   // приближается — красный
            else if (o.Speed > 0.3)
                GL.Color3(0.0, 1.0, 0.0);   // удаляется — зелёный
            else
                GL.Color3(0.5, 0.5, 0.5);   // статичный — серый
        }
    }
}
