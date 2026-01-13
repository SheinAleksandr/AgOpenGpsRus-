//Please, if you use this, share the improvements

using AgOpenGPS.Core.Drawing;
using AgOpenGPS.Core.DrawLib;
using AgOpenGPS.Core.Models;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
namespace AgOpenGPS
{
    public class CVehicle
    {
        private readonly FormGPS mf;
        public int deadZoneHeading, deadZoneDelay;
        public int deadZoneDelayCounter;
        public bool isInDeadZone;
        //min vehicle speed allowed before turning shit off
        public double slowSpeedCutoff = 0;
        //autosteer values
        public double goalPointLookAheadHold, goalPointLookAheadMult, goalPointAcquireFactor, uturnCompensation;
        public double stanleyDistanceErrorGain, stanleyHeadingErrorGain;
        public double maxSteerAngle, maxSteerSpeed, minSteerSpeed;
        public double maxAngularVelocity;
        public double hydLiftLookAheadTime;
        public double hydLiftLookAheadDistanceLeft, hydLiftLookAheadDistanceRight;
        public bool isHydLiftOn;
        public double stanleyIntegralGainAB, purePursuitIntegralGain;
        //flag for free drive window to control autosteer
        public bool isInFreeDriveMode;
        //the trackbar angle for free drive
        public double driveFreeSteerAngle = 0;
        public double modeXTE, modeActualXTE = 0, modeActualHeadingError = 0;
        public int modeTime = 0;
        public double functionSpeedLimit;
        public CVehicle(FormGPS _f)
        {
            //constructor
            mf = _f;
            VehicleConfig = new VehicleConfig();
            VehicleConfig.AntennaHeight = Properties.Settings.Default.setVehicle_antennaHeight;
            VehicleConfig.AntennaPivot = Properties.Settings.Default.setVehicle_antennaPivot;
            VehicleConfig.AntennaOffset = Properties.Settings.Default.setVehicle_antennaOffset;
            VehicleConfig.Wheelbase = Properties.Settings.Default.setVehicle_wheelbase;
            slowSpeedCutoff = Properties.Settings.Default.setVehicle_slowSpeedCutoff;
            goalPointLookAheadHold = Properties.Settings.Default.setVehicle_goalPointLookAheadHold;
            goalPointLookAheadMult = Properties.Settings.Default.setVehicle_goalPointLookAheadMult;
            goalPointAcquireFactor = Properties.Settings.Default.setVehicle_goalPointAcquireFactor;
            stanleyDistanceErrorGain = Properties.Settings.Default.stanleyDistanceErrorGain;
            stanleyHeadingErrorGain = Properties.Settings.Default.stanleyHeadingErrorGain;
            maxAngularVelocity = Properties.Settings.Default.setVehicle_maxAngularVelocity;
            maxSteerAngle = Properties.Settings.Default.setVehicle_maxSteerAngle;
            isHydLiftOn = false;
            VehicleConfig.TrackWidth = Properties.Settings.Default.setVehicle_trackWidth;
            stanleyIntegralGainAB = Properties.Settings.Default.stanleyIntegralGainAB;
            purePursuitIntegralGain = Properties.Settings.Default.purePursuitIntegralGainAB;
            VehicleConfig.Type = (VehicleType)Properties.Settings.Default.setVehicle_vehicleType;
            hydLiftLookAheadTime = Properties.Settings.Default.setVehicle_hydraulicLiftLookAhead;
            deadZoneHeading = Properties.Settings.Default.setAS_deadZoneHeading;
            deadZoneDelay = Properties.Settings.Default.setAS_deadZoneDelay;
            isInFreeDriveMode = false;
            //how far from line before it becomes Hold
            modeXTE = 0.2;
            //how long before hold is activated
            modeTime = 1;
            functionSpeedLimit = Properties.Settings.Default.setAS_functionSpeedLimit;
            maxSteerSpeed = Properties.Settings.Default.setAS_maxSteerSpeed;
            minSteerSpeed = Properties.Settings.Default.setAS_minSteerSpeed;
            uturnCompensation = Properties.Settings.Default.setAS_uTurnCompensation;
        }
        public int modeTimeCounter = 0;
        public double goalDistance = 0;
        public VehicleConfig VehicleConfig { get; }
        public double UpdateGoalPointDistance()
        {
            double xTE = Math.Abs(modeActualXTE);
            double goalPointDistance = mf.avgSpeed * 0.05 * goalPointLookAheadMult;
            double LoekiAheadHold = goalPointLookAheadHold;
            double LoekiAheadAcquire = goalPointLookAheadHold * goalPointAcquireFactor;
            if (xTE <= 0.1)
            {
                goalPointDistance *= LoekiAheadHold;
                goalPointDistance += LoekiAheadHold;
            }
            else if (xTE > 0.1 && xTE < 0.4)
            {
                xTE -= 0.1;
                LoekiAheadHold = (1 - (xTE / 0.3)) * (LoekiAheadHold - LoekiAheadAcquire);
                LoekiAheadHold += LoekiAheadAcquire;
                goalPointDistance *= LoekiAheadHold;
                goalPointDistance += LoekiAheadHold;
            }
            else
            {
                goalPointDistance *= LoekiAheadAcquire;
                goalPointDistance += LoekiAheadAcquire;
            }
            if (goalPointDistance < 2) goalPointDistance = 2;
            goalDistance = goalPointDistance;
            return goalPointDistance;
        }
        public void DrawVehicle()
        {
            GL.Rotate(glm.toDegrees(-mf.fixHeading), 0.0, 0.0, 1.0);
            // === update radar steer angle ===
            if (mf.usbCan != null)
            {
                double steerDeg =
                mf.timerSim.Enabled ? mf.sim.steerAngle : mf.mc.actualSteerAngleDegrees;
                mf.usbCan.radar.SteerAngleRad = steerDeg * Math.PI / 180.0;
            }
            //mf.font.DrawText3D(0, 0, "&TGF");
            if (mf.isFirstHeadingSet && !mf.tool.isToolFrontFixed)
            {
                // Draw the rigid hitch
                double hitchLengthFromPivot = mf.tool.GetHitchLengthFromVehiclePivot();
                double hitchHeading = mf.tool.GetHitchHeadingFromVehiclePivot(hitchLengthFromPivot);
                double hitchAngleOffset = hitchHeading - mf.fixHeading;
                double sinOffset = Math.Sin(hitchAngleOffset);
                double cosOffset = Math.Cos(hitchAngleOffset);
                XyCoord TransformVertex(double lateral, double longitudinal)
                {
                    double x = lateral * cosOffset + longitudinal * sinOffset;
                    double y = longitudinal * cosOffset - lateral * sinOffset;
                    return new XyCoord(x, y);
                }
                XyCoord[] vertices;
                if (!mf.tool.isToolRearFixed)
                {
                    vertices = new XyCoord[]
                    {
TransformVertex(0, hitchLengthFromPivot), TransformVertex(0, 0)
                    };
                }
                else
                {
                    vertices = new XyCoord[]
                    {
TransformVertex(-0.35, hitchLengthFromPivot), TransformVertex(-0.35, 0),
TransformVertex( 0.35, hitchLengthFromPivot), TransformVertex( 0.35, 0)
                    };
                }
                LineStyle backgroundLineStyle = new LineStyle(4, Colors.Black);
                LineStyle foregroundLineStyle = new LineStyle(1, Colors.HitchRigidColor);
                LineStyle[] layerStyles = { backgroundLineStyle, foregroundLineStyle };
                GLW.DrawLinesPrimitiveLayered(layerStyles, vertices);
            }
            //draw the vehicle Body
            if (!mf.isFirstHeadingSet && mf.headingFromSource != "Dual")
            {
                GL.Color4(1, 1, 1, 0.75);
                mf.ScreenTextures.QuestionMark.Draw(new XyCoord(1.0, 5.0), new XyCoord(5.0, 1.0));
            }
            //3 vehicle types tractor=0 harvestor=1 Articulated=2
            ColorRgba vehicleColor = new ColorRgba(VehicleConfig.Color, (float)VehicleConfig.Opacity);
            if (VehicleConfig.IsImage)
            {
                if (VehicleConfig.Type == VehicleType.Tractor)
                {
                    //vehicle body
                    GLW.SetColor(vehicleColor);
                    AckermannAngles(
                    -(mf.timerSim.Enabled ? mf.sim.steerangleAve : mf.mc.actualSteerAngleDegrees),
                    out double leftAckermann,
                    out double rightAckermann);
                    XyCoord tractorCenter = new XyCoord(0.0, 0.5 * VehicleConfig.Wheelbase);
                    mf.VehicleTextures.Tractor.DrawCentered(
                    tractorCenter,
                    new XyDelta(VehicleConfig.TrackWidth, -1.0 * VehicleConfig.Wheelbase));
                    //right wheel
                    GL.PushMatrix();
                    GL.Translate(0.5 * VehicleConfig.TrackWidth, VehicleConfig.Wheelbase, 0);
                    GL.Rotate(rightAckermann, 0, 0, 1);
                    XyDelta frontWheelDelta = new XyDelta(0.5 * VehicleConfig.TrackWidth, -0.75 * VehicleConfig.Wheelbase);
                    mf.VehicleTextures.FrontWheel.DrawCenteredAroundOrigin(frontWheelDelta);
                    GL.PopMatrix();
                    //Left Wheel
                    GL.PushMatrix();
                    GL.Translate(-VehicleConfig.TrackWidth * 0.5, VehicleConfig.Wheelbase, 0);
                    GL.Rotate(leftAckermann, 0, 0, 1);
                    mf.VehicleTextures.FrontWheel.DrawCenteredAroundOrigin(frontWheelDelta);
                    GL.PopMatrix();
                    //disable, straight color
                }
                else if (VehicleConfig.Type == VehicleType.Harvester)
                {
                    //vehicle body
                    AckermannAngles(
mf.timerSim.Enabled ? mf.sim.steerAngle : mf.mc.actualSteerAngleDegrees,
out double leftAckermannAngle,
out double rightAckermannAngle);
                    ColorRgba harvesterWheelColor = new ColorRgba(Colors.HarvesterWheelColor, (float)VehicleConfig.Opacity);
                    GLW.SetColor(harvesterWheelColor);
                    //right wheel
                    GL.PushMatrix();
                    GL.Translate(VehicleConfig.TrackWidth * 0.5, -VehicleConfig.Wheelbase, 0);
                    GL.Rotate(rightAckermannAngle, 0, 0, 1);
                    XyDelta forntWheelDelta = new XyDelta(0.25 * VehicleConfig.TrackWidth, 0.5 * VehicleConfig.Wheelbase);
                    mf.VehicleTextures.FrontWheel.DrawCenteredAroundOrigin(forntWheelDelta);
                    GL.PopMatrix();
                    //Left Wheel
                    GL.PushMatrix();
                    GL.Translate(-VehicleConfig.TrackWidth * 0.5, -VehicleConfig.Wheelbase, 0);
                    GL.Rotate(leftAckermannAngle, 0, 0, 1);
                    mf.VehicleTextures.FrontWheel.DrawCenteredAroundOrigin(forntWheelDelta);
                    GL.PopMatrix();
                    GLW.SetColor(vehicleColor);
                    mf.VehicleTextures.Harvester.DrawCenteredAroundOrigin(
                    new XyDelta(VehicleConfig.TrackWidth, -1.5 * VehicleConfig.Wheelbase));
                    //disable, straight color
                }
                else if (VehicleConfig.Type == VehicleType.Articulated)
                {
                    double modelSteerAngle = 0.5 * (mf.timerSim.Enabled ? mf.sim.steerAngle : mf.mc.actualSteerAngleDegrees);
                    GLW.SetColor(vehicleColor);
                    XyDelta articulated = new XyDelta(VehicleConfig.TrackWidth, -0.65 * VehicleConfig.Wheelbase);
                    GL.PushMatrix();
                    GL.Translate(0, -VehicleConfig.Wheelbase * 0.5, 0);
                    GL.Rotate(modelSteerAngle, 0, 0, 1);
                    mf.VehicleTextures.ArticulatedRear.DrawCenteredAroundOrigin(articulated);
                    GL.PopMatrix();
                    GL.PushMatrix();
                    GL.Translate(0, VehicleConfig.Wheelbase * 0.5, 0);
                    GL.Rotate(-modelSteerAngle, 0, 0, 1);
                    mf.VehicleTextures.ArticulatedFront.DrawCenteredAroundOrigin(articulated);
                    GL.PopMatrix();
                }
            }
            else
            {
                GL.Color4(1.2, 1.20, 0.0, VehicleConfig.Opacity);
                GL.Begin(PrimitiveType.TriangleFan);
                GL.Vertex3(0, VehicleConfig.AntennaPivot, -0.0);
                GL.Vertex3(1.0, -0, 0.0);
                GL.Color4(0.0, 1.20, 1.22, VehicleConfig.Opacity);
                GL.Vertex3(0, VehicleConfig.Wheelbase, 0.0);
                GL.Color4(1.220, 0.0, 1.2, VehicleConfig.Opacity);
                GL.Vertex3(-1.0, -0, 0.0);
                GL.Vertex3(1.0, -0, 0.0);
                GL.End();
                GL.LineWidth(3);
                GL.Color3(0.12, 0.12, 0.12);
                GL.Begin(PrimitiveType.LineLoop);
                {
                    GL.Vertex3(-1.0, 0, 0);
                    GL.Vertex3(1.0, 0, 0);
                    GL.Vertex3(0, VehicleConfig.Wheelbase, 0);
                }
                GL.End();
            }
            if (mf.camera.camSetDistance > -75 && mf.isFirstHeadingSet)
            {
                //draw the bright antenna dot
                PointStyle antennaBackgroundStyle = new PointStyle(16, Colors.Black);
                PointStyle antennaForegroundStyle = new PointStyle(10, Colors.AntennaColor);
                PointStyle[] layerStyles = { antennaBackgroundStyle, antennaForegroundStyle };
                GLW.DrawPointLayered(layerStyles, -VehicleConfig.AntennaOffset, VehicleConfig.AntennaPivot, 0.1);
            }
            if (mf.bnd.isBndBeingMade && mf.bnd.isDrawAtPivot)
            {
                if (mf.bnd.isDrawRightSide)
                {
                    GL.LineWidth(2);
                    GL.Color3(0.0, 1.270, 0.0);
                    GL.Begin(PrimitiveType.LineStrip);
                    {
                        GL.Vertex3(0.0, 0, 0);
                        GL.Color3(1.270, 1.220, 0.20);
                        GL.Vertex3(mf.bnd.createBndOffset, 0, 0);
                        GL.Vertex3(mf.bnd.createBndOffset * 0.75, 0.25, 0);
                    }
                    GL.End();
                }
                //draw on left side
                else
                {
                    GL.LineWidth(2);
                    GL.Color3(0.0, 1.270, 0.0);
                    GL.Begin(PrimitiveType.LineStrip);
                    {
                        GL.Vertex3(0.0, 0, 0);
                        GL.Color3(1.270, 1.220, 0.20);
                        GL.Vertex3(-mf.bnd.createBndOffset, 0, 0);
                        GL.Vertex3(-mf.bnd.createBndOffset * 0.75, 0.25, 0);
                    }
                    GL.End();
                }
            }
            //Svenn Arrow
            if (mf.isSvennArrowOn && mf.camera.camSetDistance > -1000)
            {
                //double offs = mf.curve.distanceFromCurrentLinePivot * 0.3;
                double svennDist = mf.camera.camSetDistance * -0.07;
                double svennWidth = svennDist * 0.22;
                LineStyle svenArrowLineStyle = new LineStyle(mf.ABLine.lineWidth, Colors.SvenArrowColor);
                GLW.SetLineStyle(svenArrowLineStyle);
                XyCoord[] vertices = {
new XyCoord(svennWidth, VehicleConfig.Wheelbase + svennDist),
new XyCoord(0, VehicleConfig.Wheelbase + svennWidth + 0.5 + svennDist),
new XyCoord(-svennWidth, VehicleConfig.Wheelbase + svennDist)
};
                GLW.DrawLineStripPrimitive(vertices);
            }
            // ===== ОБНОВЛЕННЫЙ БЛОК РАДАРА С ОТСЛЕЖИВАНИЕМ ТРАЕКТОРИИ =====
            if (mf.usbCan != null)
            {
                // 1. Обновляем текущий угол для расчетов
                var radar = mf.usbCan.radar;
                double steerDeg = mf.timerSim.Enabled ? mf.sim.steerAngle : mf.mc.actualSteerAngleDegrees;
                radar.SteerAngleRad = steerDeg * Math.PI / 180.0;

                radar.ToolHalfWidth = mf.tool.width * 0.5 + 0.5;

                // ===== ЗОНА КОНТРОЛЯ РАДАРА =====

                // ШИРИНА ИЗ НАСТРОЕК AOG
                double toolWidth = mf.tool.width;

                // Актуальная ширина орудия
                double halfWidth = toolWidth * 0.5;

                // Запас безопасности ±0.5 м
                halfWidth += 0.5;

                // Дальность контроля радара
                double maxDistance = radar.MaxDistanceY;

                GL.PushMatrix();

                // смещение от центра поворота (pivot axle) к радару
                double radarY =
                    Properties.Settings.Default.setVehicle_antennaOffset
                  + Properties.Settings.Default.setVehicle_radarOffsetY;

                GL.Translate(0, radarY, 0);

                // поворот зоны по рулю
                GL.Rotate(-steerDeg, 0, 0, 1);

                // --- Полупрозрачная заливка ---
                GL.Color4(0.0, 0.5, 1.0, 0.15);
                GL.Begin(PrimitiveType.Quads);
                {
                    GL.Vertex3(-halfWidth, 0, 0.05);
                    GL.Vertex3(-halfWidth, maxDistance, 0.05);
                    GL.Vertex3(halfWidth, maxDistance, 0.05);
                    GL.Vertex3(halfWidth, 0, 0.05);
                }
                GL.End();

                // --- Контур зоны ---
                GL.LineWidth(1);
                GL.Color3(0.0, 0.3, 0.8);
                GL.Begin(PrimitiveType.LineLoop);
                {
                    GL.Vertex3(-halfWidth, 0, 0.06);
                    GL.Vertex3(-halfWidth, maxDistance, 0.06);
                    GL.Vertex3(halfWidth, maxDistance, 0.06);
                    GL.Vertex3(halfWidth, 0, 0.06);
                }
                GL.End();
                GL.PopMatrix();

                // 1. Обычная зона — ВСЕГДА
                var stdObjects = radar.GetCRadarObjects();

                // ===== ГЛОБАЛЬНЫЙ КОНТРОЛЬ ДВИЖУЩИХСЯ ОБЪЕКТОВ =====
                var movingObjects = new List<CRadar.RadarObject>();

                foreach (var obj in stdObjects)
                {
                    if (obj.Speed > 0.3) // порог 0.3 м/с
                        movingObjects.Add(obj);
                }

                // 2. YouTurn зона — ТОЛЬКО если есть
                var ytObjects = new List<CRadar.RadarObject>();
                bool hasYouTurnObjects = false;
                if (mf.yt.isYouTurnTriggered && mf.yt.ytList.Count > 1)
                {
                    // 1️⃣ найти где мы сейчас на траектории
                    int startIdx = FindClosestIndex(
                        mf.yt.ytList,
                        mf.pivotAxlePos.easting,
                        mf.pivotAxlePos.northing);

                    // 2️⃣ взять ТОЛЬКО оставшийся путь
                    var remainingPath = mf.yt.ytList.GetRange(
                        startIdx,
                        mf.yt.ytList.Count - startIdx);

                    // 3️⃣ перевести его в локальные координаты
                    var localYtList = ConvertToLocal(
                        remainingPath,
                        mf.pivotAxlePos.easting,
                        mf.pivotAxlePos.northing,
                        mf.fixHeading);

                    // 4️⃣ фильтрация объектов по траектории
                    ytObjects = radar.GetObjectsOnYouTurnPath(
                        localYtList,
                        mf.tool.width * 0.5);

                    hasYouTurnObjects = ytObjects.Count > 0;

                    // 5️⃣ коридор ВСЕГДА рисуем от текущей позиции
                    DrawYouTurnCorridor(
                        localYtList,
                        mf.tool.width * 0.5);
                }
                // 3. ПРИОРИТЕТ
                List<CRadar.RadarObject> processedObjects;

                if (hasYouTurnObjects)
                {
                    processedObjects = ytObjects;
                }
                else
                {
                    processedObjects = stdObjects;
                }

                // Отрисовка самих объектов (красных точек)
                mf.usbCan.cradar.Update(processedObjects);
                mf.usbCan.cradar.Draw();
            }
            GL.LineWidth(1);
        } // Конец метода DrawVehicle

        private int FindClosestIndex(
    List<vec3> path,
    double x,
    double y)
        {
            int best = 0;
            double bestDist = double.MaxValue;

            for (int i = 0; i < path.Count; i++)
            {
                double dx = path[i].easting - x;
                double dy = path[i].northing - y;
                double d = dx * dx + dy * dy;

                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            return best;
        }
        private List<vec3> ConvertToLocal(List<vec3> worldList, double fixE, double fixN, double heading)
        {
            List<vec3> localList = new List<vec3>();
            double cosH = Math.Cos(-heading);
            double sinH = Math.Sin(-heading);
            // Анализируем путь на ближайшие 10-15 метров (примерно 50 точек)
            int limit = Math.Min(worldList.Count, 50);
            for (int i = 0; i < limit; i++)
            {
                double dx = worldList[i].easting - fixE;
                double dy = worldList[i].northing - fixN;
                // Поворот в локальную систему координат трактора
                double lx = dx * cosH + dy * sinH;
                double ly = dy * cosH - dx * sinH;
                localList.Add(new vec3(lx, ly, 0));
            }
            return localList;
        }
        private (double x, double y) GetNormal(vec3 p1, vec3 p2)
        {
            double dx = p2.easting - p1.easting;
            double dy = p2.northing - p1.northing;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001)
                return (0, 0);
            // Левая нормаль относительно направления движения
            return (-dy / len, dx / len);
        }
        private void DrawYouTurnCorridor(List<vec3> path, double halfWidth)
        {
            if (path == null || path.Count < 2)
                return;
            GL.LineWidth(2);
            GL.Color3(0.0, 1.0, 0.0);
            // ===== LEFT BORDER =====
            GL.Begin(PrimitiveType.LineStrip);
            for (int i = 0; i < path.Count - 1; i++)
            {
                var p = path[i];
                var n = GetNormal(path[i], path[i + 1]);
                GL.Vertex3(
                p.easting - n.x * halfWidth,
                p.northing - n.y * halfWidth,
                0);
            }
            GL.End();
            // ===== RIGHT BORDER =====
            GL.Begin(PrimitiveType.LineStrip);
            for (int i = 0; i < path.Count - 1; i++)
            {
                var p = path[i];
                var n = GetNormal(path[i], path[i + 1]);
                GL.Vertex3(
                p.easting + n.x * halfWidth,
                p.northing + n.y * halfWidth,
                0);
            }
            GL.End();
        }
        private void AckermannAngles(double wheelAngle, out double leftAckermannAngle, out double rightAckermannAngle)
        {
            leftAckermannAngle = wheelAngle;
            rightAckermannAngle = wheelAngle;
            if (wheelAngle > 0.0)
            {
                leftAckermannAngle *= 1.25;
            }
            else
            {
                rightAckermannAngle *= 1.25;
            }
        }
    }
}
