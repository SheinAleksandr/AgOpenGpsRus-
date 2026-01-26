using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AgOpenGPS
{
    public unsafe class UsbCanZlg : IDisposable
    {
        // ===== ZLG constants =====
        private const uint DEVICE_TYPE = 4;   // VCI_USBCAN1
        private const uint DEVICE_INDEX = 0;
        private const uint CAN_INDEX = 0;

        private const int RX_BUFFER_SIZE = 100;
        private const int RX_WAIT_TIME_MS = 100;
        private const int THREAD_SLEEP_MS = 5;
        private const int THREAD_JOIN_TIMEOUT_MS = 500;

        // ===== DLL IMPORT =====
        [DllImport("usbcan.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint VCI_OpenDevice(uint type, uint index, uint reserved);

        [DllImport("usbcan.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint VCI_CloseDevice(uint type, uint index);

        [DllImport("usbcan.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint VCI_InitCAN(uint type, uint index, uint canInd, ref VCI_INIT_CONFIG config);

        [DllImport("usbcan.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint VCI_StartCAN(uint type, uint index, uint canInd);

        [DllImport("usbcan.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint VCI_ResetCAN(uint type, uint index, uint canInd);

        [DllImport("usbcan.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int VCI_Receive(
            uint type,
            uint index,
            uint canInd,
            IntPtr pReceive,
            uint len,
            int waitTime);

        [DllImport("usbcan.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint VCI_ClearBuffer(uint type, uint index, uint canInd);

        // ===== STRUCTS =====
        [StructLayout(LayoutKind.Sequential)]
        private struct VCI_INIT_CONFIG
        {
            public uint AccCode;
            public uint AccMask;
            public uint Reserved;
            public byte Filter;
            public byte Timing0;
            public byte Timing1;
            public byte Mode;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct VCI_CAN_OBJ
        {
            public uint ID;
            public uint TimeStamp;
            public byte TimeFlag;
            public byte SendType;
            public byte RemoteFlag;
            public byte ExternFlag;
            public byte DataLen;
            public fixed byte Data[8];
            public fixed byte Reserved[3];
        }

        // ===== FIELDS =====
        private Thread rxThread;
        private volatile bool running;
        private volatile bool isDeviceOpen;

        private IntPtr rxBuffer = IntPtr.Zero;
        private readonly int objSize;

        private readonly object disposeLock = new object();
        private bool disposed = false;

        // ===== RADAR =====
        public readonly RadarSr71 radar = new RadarSr71();
        public readonly CRadar cradar = new CRadar();

        // ===== STATISTICS =====
        private long totalFramesReceived = 0;
        private long totalParseErrors = 0;
        private long lastStatsLogTime = 0;
        private const long STATS_LOG_INTERVAL_MS = 30000; // 30 секунд
        private long lastRadarFrameTime = 0;
        private long lastReconnectAttemptTime = 0;
        private const long RADAR_STALE_CLEAR_MS = 700;
        private const long RADAR_RECONNECT_MS = 3000;
        private const long RADAR_RECONNECT_COOLDOWN_MS = 2000;

        // ===== CONSTRUCTOR =====
        public UsbCanZlg()
        {
            objSize = sizeof(VCI_CAN_OBJ);
        }

        // ===== START =====
        public bool Start()
        {
            lock (disposeLock)
            {
                if (disposed)
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: Cannot start disposed UsbCanZlg");
                    return false;
                }

                if (running)
                {
                    System.Diagnostics.Debug.WriteLine("WARNING: UsbCanZlg already running");
                    return true;
                }

                try
                {
                    // Load radar settings
                    radar.RadarOffsetY = Properties.Settings.Default.setVehicle_radarOffsetY;

                    // Open device
                    if (VCI_OpenDevice(DEVICE_TYPE, DEVICE_INDEX, 0) == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("ERROR: Failed to open ZLG CAN device");
                        return false;
                    }
                    isDeviceOpen = true;

                    if (!InitializeCan())
                    {
                        CloseDevice();
                        return false;
                    }

                    // Allocate receive buffer
                    rxBuffer = Marshal.AllocHGlobal(objSize * RX_BUFFER_SIZE);

                    // Start receive thread
                    running = true;
                    rxThread = new Thread(ReceiveLoop)
                    {
                        IsBackground = true,
                        Name = "ZLG_CAN_RX",
                        Priority = ThreadPriority.AboveNormal
                    };
                    rxThread.Start();

                    lastRadarFrameTime = NowMs();
                    System.Diagnostics.Debug.WriteLine("ZLG USB-CAN STARTED successfully");
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR: Exception during ZLG start: {ex.Message}");
                    Cleanup();
                    return false;
                }
            }
        }

        // ===== STOP =====
        public void Stop()
        {
            lock (disposeLock)
            {
                if (!running && !isDeviceOpen)
                {
                    return; // Already stopped
                }

                running = false;

                // Wait for thread to finish
                if (rxThread != null && rxThread.IsAlive)
                {
                    if (!rxThread.Join(THREAD_JOIN_TIMEOUT_MS))
                    {
                        System.Diagnostics.Debug.WriteLine("WARNING: CAN receive thread did not stop gracefully");
                        try
                        {
                            rxThread.Abort();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"ERROR: Failed to abort thread: {ex.Message}");
                        }
                    }
                    rxThread = null;
                }

                Cleanup();

                System.Diagnostics.Debug.WriteLine("ZLG USB-CAN STOPPED");
                LogStatistics();
            }
        }

        // ===== CLEANUP =====
        private void Cleanup()
        {
            // Free buffer
            if (rxBuffer != IntPtr.Zero)
            {
                try
                {
                    Marshal.FreeHGlobal(rxBuffer);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR: Failed to free buffer: {ex.Message}");
                }
                rxBuffer = IntPtr.Zero;
            }

            // Close device
            CloseDevice();
        }

        // ===== CLOSE DEVICE =====
        private void CloseDevice()
        {
            if (isDeviceOpen)
            {
                try
                {
                    VCI_ResetCAN(DEVICE_TYPE, DEVICE_INDEX, CAN_INDEX);
                    VCI_CloseDevice(DEVICE_TYPE, DEVICE_INDEX);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR: Failed to close device: {ex.Message}");
                }
                isDeviceOpen = false;
            }
        }

        // ===== RECEIVE LOOP =====
        private void ReceiveLoop()
        {
            System.Diagnostics.Debug.WriteLine("CAN receive thread started");

            try
            {
                while (running)
                {
                    if (rxBuffer == IntPtr.Zero)
                    {
                        System.Diagnostics.Debug.WriteLine("ERROR: RX buffer is null");
                        break;
                    }

                    int count = 0;

                    try
                    {
                        count = VCI_Receive(
                            DEVICE_TYPE,
                            DEVICE_INDEX,
                            CAN_INDEX,
                            rxBuffer,
                            RX_BUFFER_SIZE,
                            RX_WAIT_TIME_MS);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"ERROR: VCI_Receive exception: {ex.Message}");
                        Thread.Sleep(100); // Back off on error
                        continue;
                    }

                    if (count > 0)
                    {
                        ProcessReceivedFrames(count);
                    }
                    else
                    {
                        HandleInactivity();
                    }

                    Thread.Sleep(THREAD_SLEEP_MS);
                }
            }
            catch (ThreadAbortException)
            {
                System.Diagnostics.Debug.WriteLine("CAN receive thread aborted");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FATAL: USB-CAN RX thread error: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("CAN receive thread exiting");
            }
        }

        // ===== PROCESS RECEIVED FRAMES =====
        private void ProcessReceivedFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                try
                {
                    VCI_CAN_OBJ* frame = (VCI_CAN_OBJ*)((byte*)rxBuffer + i * objSize);

                    // Validate frame
                    if (frame->DataLen == 0 || frame->DataLen > 8)
                    {
                        continue;
                    }

                    // Filter: only radar messages
                    if (frame->ID != RadarSr71.ID_HEADER &&
                        frame->ID != RadarSr71.ID_OBJECT)
                    {
                        continue;
                    }

                    // Copy data to managed array
                    byte[] data = new byte[frame->DataLen];
                    for (int j = 0; j < frame->DataLen; j++)
                    {
                        data[j] = frame->Data[j];
                    }

                    // Process radar frame
                    try
                    {
                        radar.ProcessFrame(frame->ID, data);
                        lastRadarFrameTime = NowMs();
                        totalFramesReceived++;
                    }
                    catch (Exception ex)
                    {
                        totalParseErrors++;
                        System.Diagnostics.Debug.WriteLine($"ERROR: Radar parse error: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR: Frame processing error: {ex.Message}");
                }
            }

            // Commit complete radar frame
            if (radar.FrameComplete)
            {
                try
                {
                    radar.CommitFrame();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR: Frame commit error: {ex.Message}");
                }
            }

            // Periodic statistics
            LogStatisticsPeriodically();
        }

        private void HandleInactivity()
        {
            long now = NowMs();
            long sinceLast = now - lastRadarFrameTime;

            if (sinceLast > RADAR_STALE_CLEAR_MS)
            {
                radar.Clear();
                cradar.Clear();
            }

            if (sinceLast > RADAR_RECONNECT_MS && now - lastReconnectAttemptTime > RADAR_RECONNECT_COOLDOWN_MS)
            {
                lastReconnectAttemptTime = now;
                TryReconnectCan();
            }
        }

        private bool InitializeCan()
        {
            // Configure CAN: 500 kbit/s
            VCI_INIT_CONFIG cfg = new VCI_INIT_CONFIG
            {
                AccCode = 0,
                AccMask = 0xFFFFFFFF,
                Filter = 1,
                Mode = 0,
                Timing0 = 0x00,  // 500 kbit/s
                Timing1 = 0x1C
            };

            if (VCI_InitCAN(DEVICE_TYPE, DEVICE_INDEX, CAN_INDEX, ref cfg) == 0)
            {
                System.Diagnostics.Debug.WriteLine("ERROR: Failed to initialize CAN");
                return false;
            }

            // Clear buffer
            VCI_ClearBuffer(DEVICE_TYPE, DEVICE_INDEX, CAN_INDEX);

            // Start CAN
            if (VCI_StartCAN(DEVICE_TYPE, DEVICE_INDEX, CAN_INDEX) == 0)
            {
                System.Diagnostics.Debug.WriteLine("ERROR: Failed to start CAN");
                return false;
            }

            return true;
        }

        private void TryReconnectCan()
        {
            try
            {
                if (isDeviceOpen)
                {
                    try
                    {
                        VCI_ResetCAN(DEVICE_TYPE, DEVICE_INDEX, CAN_INDEX);
                    }
                    catch { }
                    try
                    {
                        VCI_CloseDevice(DEVICE_TYPE, DEVICE_INDEX);
                    }
                    catch { }
                    isDeviceOpen = false;
                }

                if (VCI_OpenDevice(DEVICE_TYPE, DEVICE_INDEX, 0) == 0)
                {
                    System.Diagnostics.Debug.WriteLine("WARNING: CAN reopen failed (device not present)");
                    return;
                }

                isDeviceOpen = true;

                if (!InitializeCan())
                {
                    System.Diagnostics.Debug.WriteLine("WARNING: CAN reconnect failed");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("INFO: CAN reinitialized after inactivity");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR: CAN reconnect exception: {ex.Message}");
            }
        }

        private static long NowMs()
        {
            return DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        }

        // ===== STATISTICS =====
        private void LogStatisticsPeriodically()
        {
            long now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

            if (now - lastStatsLogTime > STATS_LOG_INTERVAL_MS)
            {
                LogStatistics();
                lastStatsLogTime = now;
            }
        }

        private void LogStatistics()
        {
            if (totalFramesReceived > 0)
            {
                double errorRate = (double)totalParseErrors / totalFramesReceived * 100;
                System.Diagnostics.Debug.WriteLine(
                    $"CAN Stats: Frames={totalFramesReceived}, Errors={totalParseErrors} ({errorRate:F2}%)");
            }
        }

        // ===== PUBLIC STATUS =====
        public bool IsRunning => running;

        public long TotalFramesReceived => totalFramesReceived;

        public long TotalParseErrors => totalParseErrors;

        // ===== IDISPOSABLE =====
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            lock (disposeLock)
            {
                if (disposed)
                {
                    return;
                }

                if (disposing)
                {
                    Stop();
                }

                disposed = true;
            }
        }

        ~UsbCanZlg()
        {
            Dispose(false);
        }
    }
}
