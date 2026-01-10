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
                GL.PointSize(6);
                GL.Begin(PrimitiveType.Points);

                foreach (var o in objects)
                {
                    GL.Color3(1.0, 0.0, 0.0); // красные точки
                    GL.Vertex3(o.X, o.Y, 0);
                }

                GL.End();
            }
        }
    }
}