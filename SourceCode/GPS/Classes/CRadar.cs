using OpenTK.Graphics.OpenGL;
using System.Collections.Generic;

namespace AgOpenGPS
{
    public class CRadar
    {
        public class RadarObject
        {
            public double X; // вправо от трактора (м)
            public double Y; // вперёд (м)
            public double Speed;   // ← ДОБАВИТЬ (м/с)
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

                GL.PointSize(6);
                GL.Begin(PrimitiveType.Points);

                foreach (var o in objects)
                {
                    if (o.Speed > 0.3)
                        GL.Color3(1.0, 0.0, 0.0);   // движущийся — красный
                    else
                        GL.Color3(0.5, 0.5, 0.5);   // статичный — серый

                    GL.Vertex3(o.X, o.Y, 0.05);
                }

                GL.End();
                GL.PopAttrib();
            }
        }
    }
}