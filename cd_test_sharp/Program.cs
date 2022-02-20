using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.IO;
using System.Threading;

namespace Example
{
    class Program
    {
        [StructLayout(LayoutKind.Sequential)]
        class sptd_with_sense
        {
            public SCSI_PASS_THROUGH_DIRECT sptd = new SCSI_PASS_THROUGH_DIRECT();
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 18)]
            public byte[] sense = new byte[18];
        }

        [StructLayout(LayoutKind.Sequential)]
        class SCSI_PASS_THROUGH_DIRECT
        {
            public UInt16 Length;
            public byte ScsiStatus;
            public byte PathId;
            public byte TargetId;
            public byte Lun;
            public byte CdbLength;
            public byte SenseInfoLength;
            public byte DataIn;
            public UInt32 DataTransferLength;
            public UInt32 TimeOutValue;
            public IntPtr DataBuffer;
            public UInt32 SenseInfoOffset;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] Cdb = { 0xBE, 0, 0, 0, 0, 0, 0, 0, 1, 0b11111000, 0, 0, 0, 0, 0, 0 };
        }

        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern SafeFileHandle CreateFile(
             string fileName,
             uint fileAccess,
             uint fileShare,
             IntPtr securityAttributes,
             uint creationDisposition,
             uint flags,
             IntPtr template);

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool DeviceIoControl(
            [In] SafeFileHandle hDevice,
            [In] uint dwIoControlCode,
            [In] IntPtr lpInBuffer,
            [In] int nInBufferSize,
            [Out] IntPtr lpOutBuffer,
            [Out] int nOutBufferSize,
            ref int lpBytesReturned,
            [In] IntPtr lpOverlapped);

        private static uint IOCTL_SCSI_PASS_THROUGH_DIRECT()
        {
            return CTL_CODE(IOCTL_SCSI_BASE, 0x0405, METHOD_BUFFERED, FILE_READ_ACCESS | FILE_WRITE_ACCESS);
        }

        static UInt32 IOCTL_SCSI_BASE = 0x00000004;
        static UInt32 METHOD_BUFFERED = 0;
        static UInt32 FILE_READ_ACCESS = 0x0001;
        static UInt32 FILE_WRITE_ACCESS = 0x0002;

        public static uint CTL_CODE(uint DeviceType, uint Function, uint Method, uint Access)
        {
            return (((DeviceType) << 16) | ((Access) << 14) | ((Function) << 2) | (Method));
        }

        static void Main(string[] args)
        {
            SafeFileHandle _hdev = CreateFile(@"\\.\J:", (uint)FileAccess.ReadWrite, (uint)FileShare.ReadWrite, IntPtr.Zero, (uint)FileMode.Open, 0, IntPtr.Zero);

            bool result = false;
            int bytesReturned = 0;

            IntPtr buffer = Marshal.AllocHGlobal(sizeof(ulong));

            result = DeviceIoControl(_hdev, CTL_CODE(0x00000007, 0x0017, 0, 1), IntPtr.Zero, 0, buffer, sizeof(ulong), ref bytesReturned, IntPtr.Zero);

            long disk_size = Marshal.ReadInt64(buffer);
            Marshal.FreeHGlobal(buffer);

            sptd_with_sense sptd = new sptd_with_sense();

            sptd.sptd.CdbLength = (byte)sptd.sptd.Cdb.Length;
            sptd.sptd.Length = (ushort)Marshal.SizeOf(sptd.sptd);
            sptd.sptd.DataIn = 1;// SCSI_IOCTL_DATA_IN;
            sptd.sptd.TimeOutValue = 10;
            sptd.sptd.DataBuffer = Marshal.AllocHGlobal(2352);
            sptd.sptd.DataTransferLength = 2352;
            //sptd.sptd.SenseInfoLength = (byte)sptd.sense.Length;
            //sptd.sptd.SenseInfoOffset = (uint)Marshal.OffsetOf(typeof(sptd_with_sense), "sense");

            byte[] raw_sample = new byte[2352];



            using (BinaryWriter bw = new BinaryWriter(new FileStream("data.bin", FileMode.Create)))
            {
                for (int i = 0; i < disk_size / 2048; i++)
                {
                    sptd.sptd.Cdb[2] = (byte)(i >> 24);
                    sptd.sptd.Cdb[3] = (byte)(i >> 16);
                    sptd.sptd.Cdb[4] = (byte)(i >> 8);
                    sptd.sptd.Cdb[5] = (byte)(i >> 0);

                    IntPtr sptd_ptr = Marshal.AllocHGlobal(Marshal.SizeOf(sptd));
                    Marshal.StructureToPtr(sptd, sptd_ptr, false);

                    result = DeviceIoControl(_hdev, IOCTL_SCSI_PASS_THROUGH_DIRECT(), sptd_ptr, Marshal.SizeOf(sptd), sptd_ptr, Marshal.SizeOf(sptd), ref bytesReturned, IntPtr.Zero);

                    Marshal.Copy(sptd.sptd.DataBuffer, raw_sample, 0, 2352);
                    Marshal.FreeHGlobal(sptd_ptr);

                    bw.Write(raw_sample);
                }
            }
            _hdev.Close();
        }
    }
}