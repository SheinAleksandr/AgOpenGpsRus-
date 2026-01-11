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

        IntPtr rxBuffer;
        int objSize;

        public RadarSr71 radar = new RadarSr71(); //парсит кан
        public CRadar cradar = new CRadar(); // ресует


        // ===== START =====
        public bool Start()
        {
            radar.ToolHalfWidth = Properties.Settings.Default.setVehicle_toolWidth * 0.5;
            radar.RadarOffsetY =
                    Properties.Settings.Default.setVehicle_radarOffsetY;

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
                return false;

            if (VCI_StartCAN(DEVICE_TYPE, DEVICE_INDEX, CAN_INDEX) == 0)
                return false;

            objSize = Marshal.SizeOf<VCI_CAN_OBJ>();
            rxBuffer = Marshal.AllocHGlobal(objSize * 100);

            running = true;
            rxThread = new Thread(ReceiveLoop);
            rxThread.IsBackground = true;
            rxThread.Start();

            System.Diagnostics.Debug.WriteLine("ZLG USB-CAN STARTED");
            return true;
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

            VCI_CloseDevice(DEVICE_TYPE, DEVICE_INDEX);

            System.Diagnostics.Debug.WriteLine("ZLG USB-CAN STOPPED");
        }

        // ===== RECEIVE LOOP =====
        void ReceiveLoop()
        {
            try
            {
                while (running)
                {
                    int count = VCI_Receive(
                        DEVICE_TYPE,
                        DEVICE_INDEX,
                        CAN_INDEX,
                        rxBuffer,
                        100,
                        100);

                    if (count > 0)
                    {
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

                            // ===== FILTER RADAR ONLY =====
                            if (frame.ID == 0x61A || frame.ID == 0x61B)
                            {
                                radar.ProcessFrame(frame.ID, data);
                            }
                        }
                        if (radar.FrameComplete)
                        {
                            var objs = radar.GetCRadarObjects();
                            cradar.Update(objs);

                            radar.ResetFrame();
                        }
                    }

                    Thread.Sleep(5);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("USB-CAN RX ERROR: " + ex.Message);
            }
        }
    }
}