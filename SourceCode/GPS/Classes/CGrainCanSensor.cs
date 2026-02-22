using System;
using System.Collections.Generic;

namespace AgOpenGPS
{
    /// <summary>
    /// Parser for optical grain sensor CAN frame from STM32 sketch.
    /// CAN ID: 0x18FF60A1 (extended, 29-bit), DLC=8.
    /// </summary>
    public class GrainCanSensor
    {
        public const uint ID_DATA = 0x18FF60A1;

        public float FrequencyHz { get; private set; }
        public byte Fill255 { get; private set; }
        public byte Flags { get; private set; }
        public float TonMs { get; private set; }
        public float PeriodMs { get; private set; }
        public DateTime LastUpdateUtc { get; private set; } = DateTime.MinValue;

        private bool _yieldInit;
        private double _yieldEma;
        private const double YieldEmaAlphaSlow = 0.25;
        private const double YieldEmaAlphaFast = 0.65;

        private readonly Queue<FlowSample> _flowSamples = new Queue<FlowSample>(64);

        private struct FlowSample
        {
            public DateTime TimeUtc;
            public double FlowProxy;
        }

        public bool HasRecentData(int timeoutMs = 500)
        {
            if (LastUpdateUtc == DateTime.MinValue) return false;
            return (DateTime.UtcNow - LastUpdateUtc).TotalMilliseconds <= timeoutMs;
        }

        public bool ProcessFrame(uint canId, bool isExtended, byte[] data)
        {
            if (!isExtended || canId != ID_DATA || data == null || data.Length < 8)
                return false;

            ushort freqX10 = (ushort)(data[0] | (data[1] << 8));
            FrequencyHz = freqX10 * 0.1f;

            Fill255 = data[2];
            Flags = data[3];

            ushort tonMs10 = (ushort)(data[4] | (data[5] << 8));
            ushort perMs10 = (ushort)(data[6] | (data[7] << 8));
            TonMs = tonMs10 * 0.1f;
            PeriodMs = perMs10 * 0.1f;

            LastUpdateUtc = DateTime.UtcNow;

            // Cache raw flow on CAN cadence; calculations then use 1-second moving average.
            double flowProxy = Math.Max(0.0, FrequencyHz) * (Fill255 / 255.0);
            _flowSamples.Enqueue(new FlowSample
            {
                TimeUtc = LastUpdateUtc,
                FlowProxy = flowProxy
            });
            TrimFlowSamples(3.0);

            return true;
        }

        private void TrimFlowSamples(double keepSeconds)
        {
            DateTime minTime = DateTime.UtcNow - TimeSpan.FromSeconds(Math.Max(0.1, keepSeconds));
            while (_flowSamples.Count > 0 && _flowSamples.Peek().TimeUtc < minTime)
            {
                _flowSamples.Dequeue();
            }
        }

        /// <summary>
        /// Returns sensor flow proxy (no speed/area compensation).
        /// </summary>
        public bool TryGetFlowProxy(out double flowProxy)
        {
            flowProxy = 0;

            if (!HasRecentData())
                return false;

            flowProxy = Math.Max(0.0, FrequencyHz) * (Fill255 / 255.0);
            return true;
        }

        /// <summary>
        /// Returns average sensor flow proxy for recent window, seconds.
        /// </summary>
        public bool TryGetFlowProxyAvg(double windowSec, out double avgFlowProxy)
        {
            avgFlowProxy = 0;
            if (!HasRecentData())
                return false;

            double win = Math.Max(0.1, windowSec);
            TrimFlowSamples(Math.Max(3.0, win + 0.5));
            if (_flowSamples.Count == 0)
                return false;

            DateTime minTime = DateTime.UtcNow - TimeSpan.FromSeconds(win);
            double sum = 0;
            int n = 0;
            foreach (FlowSample s in _flowSamples)
            {
                if (s.TimeUtc >= minTime)
                {
                    sum += s.FlowProxy;
                    n++;
                }
            }

            if (n <= 0)
                return false;

            avgFlowProxy = sum / n;
            return true;
        }

        /// <summary>
        /// Returns flow/area proxy before normalization.
        /// Inputs: speed in km/h, tool width in meters.
        /// </summary>
        public bool TryGetYieldProxy(
            double speedKmh,
            double toolWidthM,
            out double yieldProxy,
            double emptyFlowBaseline = 0.0)
        {
            yieldProxy = 0;

            // Use 1-second averaging to stabilize status and map coloring.
            if (!TryGetFlowProxyAvg(1.0, out double flowProxy))
                return false;

            double speedMps = Math.Abs(speedKmh) / 3.6;
            if (speedMps < 0.2 || toolWidthM <= 0.1)
                return false;

            // Empty-conveyor baseline is calibrated on standing machine in flow units.
            double effectiveFlow = Math.Max(0.0, flowProxy - Math.Max(0.0, emptyFlowBaseline));
            double areaRate = speedMps * toolWidthM;
            if (areaRate <= 0.0001)
                return false;

            yieldProxy = effectiveFlow / areaRate;
            return true;
        }

        /// <summary>
        /// Returns instantaneous yield estimate in kg/m^2.
        /// K converts flow proxy to mass flow (kg/s).
        /// </summary>
        public bool TryGetYieldKgM2(
            double speedKmh,
            double toolWidthM,
            out double yieldKgM2,
            double emptyFlowBaseline,
            double scaleK)
        {
            yieldKgM2 = 0;

            if (!TryGetYieldProxy(speedKmh, toolWidthM, out double proxyPerArea, emptyFlowBaseline))
                return false;

            double k = Math.Max(0.0, scaleK);
            yieldKgM2 = k * proxyPerArea;
            return true;
        }

        /// <summary>
        /// Returns instantaneous yield estimate in centners per hectare (ц/га).
        /// 1 kg/m^2 = 100 ц/га.
        /// </summary>
        public bool TryGetYieldCentnerPerHa(
            double speedKmh,
            double toolWidthM,
            out double yieldCha,
            double emptyFlowBaseline,
            double scaleK)
        {
            yieldCha = 0;
            if (!TryGetYieldKgM2(speedKmh, toolWidthM, out double kgm2, emptyFlowBaseline, scaleK))
                return false;

            yieldCha = kgm2 * 100.0;
            return true;
        }

        /// <summary>
        /// Returns normalized yield index 0..255 using fixed scale in centners per hectare.
        /// Inputs: speed in km/h, tool width in meters, emptyBaseline in proxy units.
        /// </summary>
        public bool TryGetYieldIndex255(
            double speedKmh,
            double toolWidthM,
            out byte yield255,
            double emptyBaseline,
            double scaleK = 1.0,
            double minCha = 0.0,
            double maxCha = 35.0)
        {
            yield255 = 0;

            if (!TryGetYieldCentnerPerHa(speedKmh, toolWidthM, out double yieldCha, emptyBaseline, scaleK))
                return false;

            // Smooth and normalize on fixed user scale for stable coloring.
            if (!_yieldInit)
            {
                _yieldEma = yieldCha;
                _yieldInit = true;
            }
            else
            {
                double delta = Math.Abs(yieldCha - _yieldEma);
                double refVal = Math.Abs(_yieldEma) + 1e-6;
                bool fastStep = (delta / refVal) > 0.12;
                double alpha = fastStep ? YieldEmaAlphaFast : YieldEmaAlphaSlow;
                _yieldEma = _yieldEma * (1.0 - alpha) + yieldCha * alpha;
            }

            double lo = Math.Min(minCha, maxCha);
            double hi = Math.Max(minCha, maxCha);
            double span = hi - lo;
            if (span < 1e-6)
            {
                yield255 = 0;
                return true;
            }

            double norm = (_yieldEma - lo) / span;
            if (norm < 0) norm = 0;
            if (norm > 1) norm = 1;

            yield255 = (byte)(norm * 255.0 + 0.5);
            return true;
        }
    }
}
