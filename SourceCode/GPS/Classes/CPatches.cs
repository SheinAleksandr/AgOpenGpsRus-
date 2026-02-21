//Please, if you use this, share the improvements

using System;
using System.Collections.Generic;

namespace AgOpenGPS
{
    public class CPatches
    {
        //copy of the mainform address
        private readonly FormGPS mf;

        //list of patch data individual triangles
        public List<vec3> triangleList = new List<vec3>();

        //list of the list of patch data individual triangles for that entire section activity
        public List<List<vec3>> patchList = new List<List<vec3>>();

        //mapping
        public bool isDrawing = false;

        //points in world space that start and end of section are in
        public vec2 leftPoint, rightPoint;

        public int numTriangles = 0;
        public int currentStartSectionNum, currentEndSectionNum;
        public int newStartSectionNum, newEndSectionNum;
        private readonly Queue<DelayedSectionSample> _sectionDelayQueue = new Queue<DelayedSectionSample>(256);
        private DelayedSectionSample _lastDelayedSample;
        private bool _hasLastDelayedSample;

        //simple constructor, position is set in GPSWinForm_Load in FormGPS when creating new object
        public CPatches(FormGPS _f)
        {
            //constructor
            mf = _f;
            patchList.Capacity = 2048;
            //triangleList.Capacity =
        }

        private struct DelayedSectionSample
        {
            public DateTime TimeUtc;
            public vec2 Left;
            public vec2 Right;
        }

        private void UpdateSectionPointsWithDelay()
        {
            vec2 currentLeft = mf.section[currentStartSectionNum].leftPoint;
            vec2 currentRight = mf.section[currentEndSectionNum].rightPoint;
            double delaySec = Math.Max(0.0, Properties.Settings.Default.setYieldMap_transportDelaySec);

            if (delaySec <= 0.001)
            {
                leftPoint = currentLeft;
                rightPoint = currentRight;
                _sectionDelayQueue.Clear();
                _hasLastDelayedSample = false;
                return;
            }

            DateTime nowUtc = DateTime.UtcNow;
            _sectionDelayQueue.Enqueue(new DelayedSectionSample
            {
                TimeUtc = nowUtc,
                Left = currentLeft,
                Right = currentRight
            });

            DateTime targetUtc = nowUtc - TimeSpan.FromSeconds(delaySec);
            while (_sectionDelayQueue.Count > 0 && _sectionDelayQueue.Peek().TimeUtc <= targetUtc)
            {
                _lastDelayedSample = _sectionDelayQueue.Dequeue();
                _hasLastDelayedSample = true;
            }

            if (_hasLastDelayedSample)
            {
                leftPoint = _lastDelayedSample.Left;
                rightPoint = _lastDelayedSample.Right;
            }
            else
            {
                // Startup period before enough history is collected.
                leftPoint = currentLeft;
                rightPoint = currentRight;
            }

            while (_sectionDelayQueue.Count > 600)
            {
                _sectionDelayQueue.Dequeue();
            }
        }

        private vec3 GetPatchColor(int j)
        {
            // For combine mode: color by normalized yield index with speed compensation.
            if (mf.usbCan != null
                && mf.usbCan.grainSensor.TryGetYieldIndex255(
                    mf.avgSpeed,
                    mf.tool.width,
                    out byte yieldIndex,
                    Properties.Settings.Default.setYieldMap_emptyBaseline,
                    Properties.Settings.Default.setYieldMap_scaleK,
                    Properties.Settings.Default.setYieldMap_colorMinCha,
                    Properties.Settings.Default.setYieldMap_colorMaxCha))
            {
                // Two-segment palette: red -> yellow -> green.
                return GetYieldPaletteColor(yieldIndex);
            }

            if (!mf.tool.isMultiColoredSections)
            {
                return new vec3(mf.sectionColorDay.R, mf.sectionColorDay.G, mf.sectionColorDay.B);
            }

            if (mf.tool.isSectionsNotZones)
            {
                return new vec3(mf.tool.secColors[j].R, mf.tool.secColors[j].G, mf.tool.secColors[j].B);
            }

            return new vec3(mf.sectionColorDay.R, mf.sectionColorDay.G, mf.sectionColorDay.B);
        }

        private static vec3 GetYieldPaletteColor(byte idx)
        {
            int v = idx;
            if (v <= 127)
            {
                // 0..127: red -> yellow (R=255, G 0..255)
                int g = v * 2;
                return new vec3(255, g, 0);
            }
            else
            {
                // 128..255: yellow -> green (G=255, R 255..0)
                int r = 255 - ((v - 128) * 2);
                if (r < 0) r = 0;
                return new vec3(r, 255, 0);
            }
        }

        private static bool HasSignificantColorChange(vec3 oldColor, vec3 newColor)
        {
            int dr = Math.Abs((int)oldColor.easting - (int)newColor.easting);
            int dg = Math.Abs((int)oldColor.northing - (int)newColor.northing);
            int db = Math.Abs((int)oldColor.heading - (int)newColor.heading);
            return (dr + dg + db) >= 4;
        }

        private void SaveOrDiscardCurrentChunk()
        {
            if (triangleList.Count > 4)
            {
                mf.patchSaveList.Add(triangleList);
            }
            else
            {
                triangleList.Clear();
                if (patchList.Count > 0) patchList.RemoveAt(patchList.Count - 1);
            }
        }

        private void StartChunkWithSeed(vec3 color, vec3 seedLeft, vec3 seedRight)
        {
            triangleList = new List<vec3>(64);
            patchList.Add(triangleList);
            triangleList.Add(color);
            triangleList.Add(new vec3(seedLeft.easting, seedLeft.northing, 0));
            triangleList.Add(new vec3(seedRight.easting, seedRight.northing, 0));
            numTriangles = 0;
        }

        public void TurnMappingOn(int j)
        {
            numTriangles = 0;

            //do not tally square meters on inital point, that would be silly
            if (!isDrawing)
            {
                //set the section bool to on
                isDrawing = true;

                //starting a new patch chunk so create a new triangle list
                triangleList = new List<vec3>(64);

                patchList.Add(triangleList);
                triangleList.Add(GetPatchColor(j));

                UpdateSectionPointsWithDelay();

                //left side of triangle
                triangleList.Add(new vec3(leftPoint.easting, leftPoint.northing, 0));

                //Right side of triangle
                triangleList.Add(new vec3(rightPoint.easting, rightPoint.northing, 0));

                mf.patchCounter++;
            }
        }

        public void TurnMappingOff()
        {
            AddMappingPoint(0);

            isDrawing = false;
            numTriangles = 0;
            SaveOrDiscardCurrentChunk();
            _sectionDelayQueue.Clear();
            _hasLastDelayedSample = false;
        }

        //every time a new fix, a new patch point from last point to this point
        //only need prev point on the first points of triangle strip that makes a box (2 triangles)

        public void AddMappingPoint(int j)
        {
            UpdateSectionPointsWithDelay();

            // Keep local color history: split into a new chunk when color changed enough.
            if (triangleList.Count > 0)
            {
                vec3 newColor = GetPatchColor(j);
                if (triangleList.Count > 4 && HasSignificantColorChange(triangleList[0], newColor))
                {
                    vec3 seedLeft = triangleList[triangleList.Count - 2];
                    vec3 seedRight = triangleList[triangleList.Count - 1];
                    SaveOrDiscardCurrentChunk();
                    StartChunkWithSeed(newColor, seedLeft, seedRight);
                }
                else if (triangleList.Count <= 4)
                {
                    // Before real geometry appears, just update the header color.
                    triangleList[0] = newColor;
                }
            }

            //add two triangles for next step.
            //left side

            //add the point to List
            triangleList.Add(new vec3(leftPoint.easting, leftPoint.northing, 0));

            //Right side
            triangleList.Add(new vec3(rightPoint.easting, rightPoint.northing, 0));

            //countExit the triangle pairs
            numTriangles++;

            //quick countExit
            int c = triangleList.Count - 1;

            //when closing a job the triangle patches all are emptied but the section delay keeps going.
            //Prevented by quick check. 4 points plus colour
            //if (c >= 5)
            {
                //calculate area of these 2 new triangles - AbsoluteValue of (Ax(By-Cy) + Bx(Cy-Ay) + Cx(Ay-By)/2)
                {
                    double temp = Math.Abs((triangleList[c].easting * (triangleList[c - 1].northing - triangleList[c - 2].northing))
                              + (triangleList[c - 1].easting * (triangleList[c - 2].northing - triangleList[c].northing))
                                  + (triangleList[c - 2].easting * (triangleList[c].northing - triangleList[c - 1].northing)));

                    temp += Math.Abs((triangleList[c - 1].easting * (triangleList[c - 2].northing - triangleList[c - 3].northing))
                              + (triangleList[c - 2].easting * (triangleList[c - 3].northing - triangleList[c - 1].northing))
                                  + (triangleList[c - 3].easting * (triangleList[c - 1].northing - triangleList[c - 2].northing)));

                    temp *= 0.5;
                    mf.fd.workedAreaTotal += temp;
                    mf.fd.workedAreaTotalUser += temp;
                }
            }

            if (numTriangles > 61)
            {
                //save the cutoff patch to be saved later
                mf.patchSaveList.Add(triangleList);

                StartChunkWithSeed(
                    GetPatchColor(j),
                    new vec3(leftPoint.easting, leftPoint.northing, 0),
                    new vec3(rightPoint.easting, rightPoint.northing, 0));
            }
        }
    }
}
