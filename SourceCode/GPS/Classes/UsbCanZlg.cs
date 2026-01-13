using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AgOpenGPS
{
    public unsafe class UsbCanZlg
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
            public fixed byte Data[8];
            public fixed byte Reserved[3];
        }

        // ===== FIELDS =====
        private Thread rxThread;
        private volatile bool running;

        private IntPtr rxBuffer;
        private int objSize;

        // ===== RADAR =====
        public readonly RadarSr71 radar = new RadarSr71();
        public readonly CRadar cradar = new CRadar();

        // ===== START =====
        public bool Start()
        {
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

                // 500 kbit/s
                Timing0 = 0x00,
                Timing1 = 0x1C
            };

            if (VCI_InitCAN(DEVICE_TYPE, DEVICE_INDEX, CAN_INDEX, ref cfg) == 0)
                return false;

            if (VCI_StartCAN(DEVICE_TYPE, DEVICE_INDEX, CAN_INDEX) == 0)
                return false;

            objSize = sizeof(VCI_CAN_OBJ);
            rxBuffer = Marshal.AllocHGlobal(objSize * 100);

            running = true;
            rxThread = new Thread(ReceiveLoop)
            {
                IsBackground = true
            };
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
                rxThread?.Join(300);
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
        private void ReceiveLoop()
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
                            VCI_CAN_OBJ* frame =
                                (VCI_CAN_OBJ*)((byte*)rxBuffer + i * objSize);

                            if (frame->DataLen == 0 || frame->DataLen > 8)
                                continue;

                            // ===== FILTER RADAR =====
                            if (frame->ID != RadarSr71.ID_HEADER &&
                                frame->ID != RadarSr71.ID_OBJECT)
                                continue;

                            byte[] data = new byte[frame->DataLen];
                            for (int j = 0; j < frame->DataLen; j++)
                                data[j] = frame->Data[j];

                            radar.ProcessFrame(frame->ID, data);
                        }

                        // Кадр завершён — просто сбрасываем флаг
                        if (radar.FrameComplete)
                        {
                            radar.CommitFrame();
                        }
                    }

                    Thread.Sleep(5);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("USB-CAN RX ERROR: " + ex);
            }
        }
    }
}
