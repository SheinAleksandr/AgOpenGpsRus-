using OpenTK.Graphics.OpenGL;
using System.Collections.Generic;

namespace AgOpenGPS
{
    public class CRadar
    {
        public class RadarObject
        {
            public double X;       // вправо от трактора (м)
            public double Y;       // вперёд (м)
            public double Speed;   // м/с
            public double Rcs;     // dBsm
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

                // draw red (RCS > 18) larger
                GL.PointSize(15);
                GL.Begin(PrimitiveType.Points);
                foreach (var o in objects)
                {
                    if (o.Rcs <= 18.0)
                        continue;
                    SetColor(o);
                    GL.Vertex3(o.X, o.Y, 0.05);
                }
                GL.End();

                // draw остальных
                GL.PointSize(10);
                GL.Begin(PrimitiveType.Points);
                foreach (var o in objects)
                {
                    if (o.Rcs > 18.0)
                        continue;
                    SetColor(o);
                    GL.Vertex3(o.X, o.Y, 0.05);
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

        // ===== ЦВЕТ ПО ОТРАЖЕНИЮ =====
        private static void SetColor(RadarObject o)
        {
            if (o.Rcs > 18.0)
                GL.Color3(1.0, 0.0, 0.0);   // > 18 — красный
            else if (o.Rcs >= 15.0)
                GL.Color3(1.0, 1.0, 0.0);   // 15..18 — жёлтый
            else
                GL.Color3(0.5, 0.5, 0.5);   // < 15 — серый
        }
    }
}
