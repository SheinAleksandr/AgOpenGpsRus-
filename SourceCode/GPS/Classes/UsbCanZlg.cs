using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AgOpenGPS
{
    public class UsbCanZlg
    {
        // ===== ZLG constants =====
        const uint DEVICE_TYPE = 4;   // VCI_USBCAN1
        const uint DEVICE_INDEX = 0;
        const uint CAN_INDEX = 0;

        // ===== DLL IMPORT =====
        [DllImport("usbcan.dll")]
        static extern uint VCI_OpenDevice(uint type, uint index, uint reserved);

        [DllImport("usbcan.dll")]
        static extern uint VCI_CloseDevice(uint type, uint index);

        [DllImport("usbcan.dll")]
        static extern uint VCI_InitCAN(uint type, uint index, uint canInd, ref VCI_INIT_CONFIG config);

        [DllImport("usbcan.dll")]
        static extern uint VCI_StartCAN(uint type, uint index, uint canInd);

        [DllImport("usbcan.dll")]
        static extern uint VCI_ResetCAN(uint type, uint index, uint canInd);

        [DllImport("usbcan.dll")]
        static extern uint VCI_ClearBuffer(uint type, uint index, uint canInd);

        [DllImport("usbcan.dll")]
        static extern int VCI_Receive(
            uint type,
            uint index,
            uint canInd,
            IntPtr pReceive,
            uint len,
            int waitTime);

        // ===== STRUCTS =====
        [StructLayout(LayoutKind.Sequential)]
        struct VCI_INIT_CONFIG
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
        struct VCI_CAN_OBJ
        {
            public uint ID;
            public uint TimeStamp;
            public byte TimeFlag;
            public byte SendType;
            public byte RemoteFlag;
            public byte ExternFlag;
            public byte DataLen;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] Data;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public byte[] Reserved;
        }

        // ===== FIELDS =====
        Thread rxThread;
        volatile bool running;
        volatile bool canOnline;
        DateTime lastReconnectAttemptUtc = DateTime.MinValue;
        DateTime lastFrameUtc = DateTime.MinValue;
        bool hadFramesOnCurrentSession;
        const int ReconnectIntervalMs = 1000;
        const int InactivityReconnectMs = 2500;
        readonly object canLock = new object();

        IntPtr rxBuffer;
        int objSize;

        public GrainCanSensor grainSensor = new GrainCanSensor(); // оптический датчик

        bool TryOpenAndStartCan()
        {
            lock (canLock)
            {
                if (canOnline) return true;

                if (VCI_OpenDevice(DEVICE_TYPE, DEVICE_INDEX, 0) == 0)
                    return false;

                VCI_INIT_CONFIG cfg = new VCI_INIT_CONFIG
                {
                    AccCode = 0,
                    AccMask = 0xFFFFFFFF,
                    Filter = 1,
                    Mode = 0,

                    // 500 kbit/s for ZLG
                    Timing0 = 0x00,
                    Timing1 = 0x1C
                };

                if (VCI_InitCAN(DEVICE_TYPE, DEVICE_INDEX, CAN_INDEX, ref cfg) == 0)
                {
                    VCI_CloseDevice(DEVICE_TYPE, DEVICE_INDEX);
                    return false;
                }

                if (VCI_StartCAN(DEVICE_TYPE, DEVICE_INDEX, CAN_INDEX) == 0)
                {
                    VCI_CloseDevice(DEVICE_TYPE, DEVICE_INDEX);
                    return false;
                }

                VCI_ClearBuffer(DEVICE_TYPE, DEVICE_INDEX, CAN_INDEX);
                canOnline = true;
                lastFrameUtc = DateTime.MinValue;
                hadFramesOnCurrentSession = false;
                return true;
            }
        }

        void CloseCan()
        {
            lock (canLock)
            {
                if (!canOnline)
                    return;

                canOnline = false;
                try { VCI_ResetCAN(DEVICE_TYPE, DEVICE_INDEX, CAN_INDEX); } catch { }
                try { VCI_CloseDevice(DEVICE_TYPE, DEVICE_INDEX); } catch { }
            }
        }

        // ===== START =====
        public bool Start()
        {
            objSize = Marshal.SizeOf<VCI_CAN_OBJ>();
            rxBuffer = Marshal.AllocHGlobal(objSize * 100);

            bool startedNow = TryOpenAndStartCan();
            if (!startedNow)
            {
                System.Diagnostics.Debug.WriteLine("ZLG USB-CAN NOT READY, waiting for reconnect...");
            }

            running = true;
            rxThread = new Thread(ReceiveLoop);
            rxThread.IsBackground = true;
            rxThread.Start();

            System.Diagnostics.Debug.WriteLine("ZLG USB-CAN STARTED");
            return startedNow;
        }

        // ===== STOP =====
        public void Stop()
        {
            running = false;

            try
            {
                if (rxThread != null && rxThread.IsAlive)
                    rxThread.Join(300);
            }
            catch { }

            if (rxBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(rxBuffer);
                rxBuffer = IntPtr.Zero;
            }

            CloseCan();

            System.Diagnostics.Debug.WriteLine("ZLG USB-CAN STOPPED");
        }

        // ===== RECEIVE LOOP =====
        void ReceiveLoop()
        {
            try
            {
                while (running)
                {
                    if (!canOnline)
                    {
                        if ((DateTime.UtcNow - lastReconnectAttemptUtc).TotalMilliseconds >= ReconnectIntervalMs)
                        {
                            lastReconnectAttemptUtc = DateTime.UtcNow;
                            if (TryOpenAndStartCan())
                            {
                                System.Diagnostics.Debug.WriteLine("ZLG USB-CAN RECONNECTED");
                            }
                        }

                        Thread.Sleep(50);
                        continue;
                    }

                    // Driver may stay "online" but stop delivering frames after adapter replug.
                    if (hadFramesOnCurrentSession &&
                        lastFrameUtc != DateTime.MinValue &&
                        (DateTime.UtcNow - lastFrameUtc).TotalMilliseconds >= InactivityReconnectMs &&
                        (DateTime.UtcNow - lastReconnectAttemptUtc).TotalMilliseconds >= ReconnectIntervalMs)
                    {
                        lastReconnectAttemptUtc = DateTime.UtcNow;
                        System.Diagnostics.Debug.WriteLine("ZLG USB-CAN INACTIVE, forcing reopen...");
                        CloseCan();
                        Thread.Sleep(50);
                        continue;
                    }

                    int count = VCI_Receive(
                        DEVICE_TYPE,
                        DEVICE_INDEX,
                        CAN_INDEX,
                        rxBuffer,
                        100,
                        100);

                    if (count < 0)
                    {
                        System.Diagnostics.Debug.WriteLine("ZLG USB-CAN DISCONNECTED, trying to reconnect...");
                        CloseCan();
                        Thread.Sleep(50);
                        continue;
                    }

                    if (count > 0)
                    {
                        hadFramesOnCurrentSession = true;
                        lastFrameUtc = DateTime.UtcNow;
                        for (int i = 0; i < count; i++)
                        {
                            IntPtr ptr = IntPtr.Add(rxBuffer, i * objSize);
                            VCI_CAN_OBJ frame;
                            try
                            {
                                frame = Marshal.PtrToStructure<VCI_CAN_OBJ>(ptr);
                            }
                            catch
                            {
                                continue;
                            }

                            if (frame.DataLen == 0 || frame.Data == null)
                                continue;

                            int len = Math.Min((int)frame.DataLen, 8);
                            byte[] data = new byte[len];
                            Array.Copy(frame.Data, data, len);

                            bool isExtended = frame.ExternFlag == 1;

                            // ===== OPTICAL GRAIN SENSOR =====
                            if (grainSensor.ProcessFrame(frame.ID, isExtended, data))
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    $"GRAIN CAN: fill={grainSensor.Fill255} freq={grainSensor.FrequencyHz:F1}Hz flags=0x{grainSensor.Flags:X2}");
                            }
                        }
                    }

                    Thread.Sleep(5);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("USB-CAN RX ERROR: " + ex.Message);
                CloseCan();
            }
        }
    }
}
