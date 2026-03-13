using System;

using System.Collections.Generic;

namespace AgOpenGPS
{
    public partial class CBoundary
    {
        public bool isHeadlandOn;

        public bool isToolInHeadland,
            isToolOuterPointsInHeadland, isSectionControlledByHeadland;

        public vec2? HeadlandNearestPoint { get; private set; } = null;
        public double? HeadlandDistance { get; private set; } = null;

        public bool HasAnyHeadland()
        {
            if (bndList == null || bndList.Count == 0) return false;

            for (int i = 0; i < bndList.Count; i++)
            {
                if (bndList[i].hdLine != null && bndList[i].hdLine.Count > 2)
                {
                    return true;
                }
            }

            return false;
        }

        public void SetHydPosition()
        {
            if (mf.vehicle.isHydLiftOn && mf.avgSpeed > 0.2 && !mf.isReverse)
            {
                if (isToolInHeadland)
                {
                    mf.p_239.pgn[mf.p_239.hydLift] = 2;
                    if (mf.sounds.isHydLiftChange != isToolInHeadland)
                    {
                        if (mf.sounds.isHydLiftSoundOn) mf.sounds.sndHydLiftUp.Play();
                        mf.sounds.isHydLiftChange = isToolInHeadland;
                    }
                }
                else
                {
                    mf.p_239.pgn[mf.p_239.hydLift] = 1;
                    if (mf.sounds.isHydLiftChange != isToolInHeadland)
                    {
                        if (mf.sounds.isHydLiftSoundOn) mf.sounds.sndHydLiftDn.Play();
                        mf.sounds.isHydLiftChange = isToolInHeadland;
                    }
                }
            }
        }

        public void WhereAreToolCorners()
        {
            if (HasAnyHeadland())
            {
                bool isLeftInWk, isRightInWk = true;

                for (int j = 0; j < mf.tool.numOfSections; j++)
                {
                    isLeftInWk = j == 0 ? IsPointInsideHeadArea(mf.section[j].leftPoint) : isRightInWk;
                    isRightInWk = IsPointInsideHeadArea(mf.section[j].rightPoint);

                    //save left side
                    if (j == 0)
                        mf.tool.isLeftSideInHeadland = !isLeftInWk;

                    //merge the two sides into in or out
                    mf.section[j].isInHeadlandArea = !isLeftInWk && !isRightInWk;
                }

                //save right side
                mf.tool.isRightSideInHeadland = !isRightInWk;

                // Outer boundary only: tool width based trigger (ignore inner hdLine zones)
                bool leftInOuterHd = bndList[0].hdLine != null && bndList[0].hdLine.Count > 2
                    && !bndList[0].hdLine.IsPointInPolygon(mf.section[0].leftPoint);
                bool rightInOuterHd = bndList[0].hdLine != null && bndList[0].hdLine.Count > 2
                    && !bndList[0].hdLine.IsPointInPolygon(mf.section[mf.tool.numOfSections - 1].rightPoint);
                isToolOuterPointsInHeadland = leftInOuterHd && rightInOuterHd;

                // Inner boundaries: pivot inside hdLine zone AND AB line crosses turnLine
                if (!isToolOuterPointsInHeadland)
                {
                    vec2 pivotPt = mf.toolPivotPos.ToVec2();
                    for (int i = 1; i < bndList.Count; i++)
                    {
                        if (bndList[i].hdLine == null || bndList[i].hdLine.Count < 3) continue;
                        if (!bndList[i].hdLine.IsPointInPolygon(pivotPt)) continue;
                        if (IsABLineCrossingTurnLine(i))
                        {
                            isToolOuterPointsInHeadland = true;
                            break;
                        }
                    }
                }
            }
        }

        public void WhereAreToolLookOnPoints()
        {
            if (HasAnyHeadland())
            {
                bool isLookRightIn = false;

                vec3 toolFix = mf.toolPivotPos;
                double sinAB = Math.Sin(toolFix.heading);
                double cosAB = Math.Cos(toolFix.heading);

                //generated box for finding closest point
                double pos = 0;
                double mOn = (mf.tool.lookAheadDistanceOnPixelsRight - mf.tool.lookAheadDistanceOnPixelsLeft) / mf.tool.rpWidth;

                for (int j = 0; j < mf.tool.numOfSections; j++)
                {
                    bool isLookLeftIn = j == 0 ? IsPointInsideHeadArea(new vec2(
                        mf.section[j].leftPoint.easting + (sinAB * mf.tool.lookAheadDistanceOnPixelsLeft * 0.1),
                        mf.section[j].leftPoint.northing + (cosAB * mf.tool.lookAheadDistanceOnPixelsLeft * 0.1))) : isLookRightIn;

                    pos += mf.section[j].rpSectionWidth;
                    double endHeight = (mf.tool.lookAheadDistanceOnPixelsLeft + (mOn * pos)) * 0.1;

                    isLookRightIn = IsPointInsideHeadArea(new vec2(
                        mf.section[j].rightPoint.easting + (sinAB * endHeight),
                        mf.section[j].rightPoint.northing + (cosAB * endHeight)));

                    mf.section[j].isLookOnInHeadland = !isLookLeftIn && !isLookRightIn;
                }
            }
        }

        public bool IsPointInsideHeadArea(vec2 pt)
        {
            if (bndList.Count == 0 || bndList[0].hdLine == null || bndList[0].hdLine.Count < 3) return false;

            //if inside outer boundary, then potentially add
            if (bndList[0].hdLine.IsPointInPolygon(pt))
            {
                for (int i = 1; i < bndList.Count; i++)
                {
                    if (bndList[i].hdLine != null && bndList[i].hdLine.Count > 2 && bndList[i].hdLine.IsPointInPolygon(pt))
                    {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }

        public bool IsPointInHydLiftWindow(vec3 pt, double beforeMeters, double afterMeters)
        {
            if (!isHeadlandOn || !HasAnyHeadland()) return false;

            if (beforeMeters < 0) beforeMeters = 0;
            if (afterMeters < 0) afterMeters = 0;

            vec2 ptVec2 = pt.ToVec2();
            double fwdX = Math.Sin(pt.heading);
            double fwdY = Math.Cos(pt.heading);

            for (int i = 0; i < bndList.Count; i++)
            {
                if (bndList[i].hdLine == null || bndList[i].hdLine.Count < 2) continue;

                double nearestDist = NearestPointOnPolyline(ptVec2, bndList[i].hdLine, out vec2 nearestPt);

                bool inHeadlandZone = (i == 0)
                    ? !bndList[i].hdLine.IsPointInPolygon(ptVec2)
                    : bndList[i].hdLine.IsPointInPolygon(ptVec2);

                // dot > 0: boundary is ahead (approaching), dot < 0: boundary is behind (just exited)
                double dot = fwdX * (nearestPt.easting - ptVec2.easting)
                           + fwdY * (nearestPt.northing - ptVec2.northing);

                bool nearBefore = dot > 0 && nearestDist <= beforeMeters;
                bool nearAfter  = !inHeadlandZone && dot < 0 && nearestDist <= afterMeters;

                if (nearBefore || inHeadlandZone || nearAfter)
                {
                    if (i > 0 && !IsABLineCrossingTurnLine(i)) continue;
                    return true;
                }
            }

            return false;
        }

        private bool IsABLineCrossingTurnLine(int bndIdx)
        {
            // If a UTurn maneuver is already in progress, keep the flag active
            if (mf.yt.isYouTurnTriggered) return true;

            var turnLine = bndList[bndIdx].turnLine;
            if (turnLine == null || turnLine.Count < 2) return false;

            if (!mf.ABLine.isABValid) return false;

            double aE = mf.ABLine.currentLinePtA.easting;
            double aN = mf.ABLine.currentLinePtA.northing;
            double bE = mf.ABLine.currentLinePtB.easting;
            double bN = mf.ABLine.currentLinePtB.northing;

            for (int i = 0; i < turnLine.Count - 1; i++)
            {
                if (SegmentsIntersect(
                    turnLine[i].easting, turnLine[i].northing,
                    turnLine[i + 1].easting, turnLine[i + 1].northing,
                    aE, aN, bE, bN))
                    return true;
            }
            return false;
        }

        private static bool SegmentsIntersect(double x1, double y1, double x2, double y2,
                                               double x3, double y3, double x4, double y4)
        {
            double d = (x2 - x1) * (y4 - y3) - (y2 - y1) * (x4 - x3);
            if (Math.Abs(d) < 1e-10) return false;
            double t = ((x3 - x1) * (y4 - y3) - (y3 - y1) * (x4 - x3)) / d;
            double u = ((x3 - x1) * (y2 - y1) - (y3 - y1) * (x2 - x1)) / d;
            return t >= 0.0 && t <= 1.0 && u >= 0.0 && u <= 1.0;
        }

        private static double NearestPointOnPolyline(vec2 p, List<vec3> line, out vec2 nearest)
        {
            nearest = new vec2();
            if (line == null || line.Count < 2) return double.MaxValue;

            double minDistSq = double.MaxValue;
            for (int i = 0; i < line.Count - 1; i++)
            {
                vec2 a = line[i].ToVec2();
                vec2 b = line[i + 1].ToVec2();
                vec2 candidate = ClosestPointOnSegment(p, a, b);
                double dSq = glm.DistanceSquared(p, candidate);
                if (dSq < minDistSq) { minDistSq = dSq; nearest = candidate; }
            }
            return Math.Sqrt(minDistSq);
        }

        private static vec2 ClosestPointOnSegment(vec2 p, vec2 a, vec2 b)
        {
            double vx = b.easting - a.easting, vy = b.northing - a.northing;
            double c1 = vx * (p.easting - a.easting) + vy * (p.northing - a.northing);
            if (c1 <= 0) return a;
            double c2 = vx * vx + vy * vy;
            if (c2 <= c1) return b;
            double t = c1 / c2;
            return new vec2(a.easting + t * vx, a.northing + t * vy);
        }


        public void CheckHeadlandProximity()
        {
            if (!isHeadlandOn || !HasAnyHeadland())
            {
                HeadlandNearestPoint = null;
                HeadlandDistance = null;
                return;
            }

            vec3 vehiclePos = mf.toolPivotPos;
            vec2? nearest = null;
            double minDistance = double.MaxValue;
            int nearestHdIndex = -1;

            for (int i = 0; i < bndList.Count; i++)
            {
                if (bndList[i].hdLine == null || bndList[i].hdLine.Count < 2) continue;

                vec2? hit = glm.RaycastToPolygon(vehiclePos, bndList[i].hdLine);
                if (!hit.HasValue) continue;

                double d = glm.Distance(vehiclePos.ToVec2(), hit.Value);
                if (d < minDistance)
                {
                    minDistance = d;
                    nearest = hit;
                    nearestHdIndex = i;
                }
            }

            if (!nearest.HasValue || nearestHdIndex == -1)
            {
                HeadlandNearestPoint = null;
                HeadlandDistance = null;
                return;
            }

            vec2 nearestVal = nearest.Value;
            double distance = minDistance;

            HeadlandNearestPoint = nearestVal;
            HeadlandDistance = distance;

            bool isInside = bndList[nearestHdIndex].hdLine.IsPointInPolygon(vehiclePos.ToVec2());

            double dx = nearestVal.easting - vehiclePos.easting;
            double dy = nearestVal.northing - vehiclePos.northing;
            double angleToPolygon = Math.Atan2(dx, dy);
            double headingDiff = glm.AngleDiff(vehiclePos.heading, angleToPolygon);
            bool headingOk = headingDiff < glm.toRadians(60); // eventueel verwijderen: zit al in GetClosestPointInFront

            // Warning Logic
            bool shouldPlay =
                (isInside && headingOk && distance < 20.0) ||
                (!isInside && headingOk && distance < 5.0);

            if (shouldPlay && mf.isHeadlandDistanceOn)
            {
                if (!mf.sounds.isBoundAlarming)
                {
                    mf.sounds.sndHeadland.Play();
                    mf.sounds.isBoundAlarming = true;
                }
            }
            else
            {
                mf.sounds.isBoundAlarming = false;
            }
        }

    }
}
