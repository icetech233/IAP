using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using IAP;
using System.IO.Ports;
namespace Ymodem
{
    public enum InitialCrcValue { Zeros, NonZero1 = 0xffff, NonZero2 = 0x1D0F }
    
    public class Ymodem
    {

        /*
         * Upload file via Ymodem protocol to the device
         * ret: is the transfer succeeded? true is if yes
         */
        private string path;
        public string Path{get {return Path;} set { path = value; } }
        private string portName;
        public string PortName { get { return portName; } set { portName = value; } }
        private int baudRate;
        public int BaudRate { get { return baudRate; } set { baudRate = value; } }
        private System.IO.Ports.SerialPort serialPort = new System.IO.Ports.SerialPort();
        public event EventHandler NowDownloadProgressEvent;
        public event EventHandler DownloadResultEvent;


        public static byte C { get; set; } = 67;
        /* control signals */
        // 1k 数据头
        const byte STX = 2;  // Start of TeXt 
                             // EOT 04H 发送结束
        const byte EOT = 4;  // End Of Transmission
                             // 确认消息
        const byte ACK = 6;  // Positive ACknowledgement
                             // const byte C = 67;   // 4*16 + 3 capital letter C
        /* sizes */
        // const int dataSize = 1024;

        // const byte C = 67;   // 4*16 + 3 capital letter C

        public void YmodemUploadFile()
        {
            /* THE PACKET: 1029 bytes */
            /* header: 3 bytes */
            // STX
            int packetNum = 0;
            int invertedPacketNum = 255;
            /* data: 1024 bytes */
            byte[] data = new byte[1024];
            /* footer: 2 bytes */
            byte[] CRC = new byte[2];
            /* get the file */
            FileStream fileStream1 = new FileStream(@path, FileMode.Open, FileAccess.Read);

            MemoryStream ms1 = new MemoryStream();
            fileStream1.CopyTo(ms1);
            fileStream1.Dispose();
            fileStream1 = null;
            ms1.Position = 0;
            long fileAllDataCount = ms1.Length;

            serialPort.PortName = portName; 
            serialPort.BaudRate = 921600;
            // 
            serialPort.DataBits = 8;
            serialPort.StopBits = StopBits.One;
            serialPort.Parity = Parity.None;
            // 
            serialPort.Open();
            try
            {
                // 清空缓存 
                byte[] array = new byte[1024];
                int aa = this.serialPort.Read(array, 0, array.Length);
                Console.WriteLine("清空缓存clean read cache; array len:" + array.Length);

                //serialPort.Write(new byte[] { 0x31 }, 0, 1);
                /* send the initial packet with filename and filesize */
                if (serialPort.ReadByte() != C)
                { 
                    Console.WriteLine("Can't begin the transfer.");

                    serialPort.Close();
                    DownloadResultEvent.Invoke(false, new EventArgs());
                    return;// false;
                }
                else 
                { 
                    Console.WriteLine(" begin the transfer.");
                }
                //
                sendYmodemInitialPacket(packetNum, invertedPacketNum, data, path, ms1, CRC);
                //
                byte temp = (byte)serialPort.ReadByte();
                if (temp != ACK)//(serialPort.ReadByte() != ACK)
                {
                    Console.WriteLine("Can't send the initial packet.");
                    DownloadResultEvent.Invoke(false, new EventArgs());
                    return;// false;
                }
                Console.WriteLine("过了1");
                if (serialPort.ReadByte() != C)
                {
                    DownloadResultEvent.Invoke(false, new EventArgs());
                    return;// false;
                }
                Console.WriteLine("过了2");

                // 清空换成
                array = new byte[1024];
                aa = this.serialPort.Read(array, 0, array.Length);
                Console.WriteLine("清空缓存; array len:" + array.Length);

                 
                var currentFileCount = 0;

                /* send packets with a cycle until we send the last byte */
                int fileReadCount;
                do
                {
                    data = new byte[1024];
                    /* if this is the last packet fill the remaining bytes with 0 */
                    fileReadCount = ms1.Read(data, 0, 1024);
                    if (fileReadCount == 0)
                    {
                        break;
                    }
                    // 
                    if (fileReadCount != 1024) 
                    {
                        // 数据帧 用 0x1a 补全
                        for (int i = fileReadCount; i < 1024; i++) 
                        {
                            data[i] = 0x1A;
                        }
                    }

                    /* calculate packetNumber */
                    packetNum++;
                    if (packetNum > 255) packetNum -= 256;
                    /* calculate invertedPacketNum */
                    invertedPacketNum = 255 - packetNum;

                    /* calculate CRC */
                    Crc16Ccitt crc16Ccitt = new Crc16Ccitt(InitialCrcValue.Zeros);
                    CRC = crc16Ccitt.ComputeChecksumBytes(data);

                    /* send the packet */
                    SendYmodemPacket(packetNum, invertedPacketNum, data, CRC);
                    currentFileCount += 1024;
                    // 新进度条计算
                    float _p = (float)currentFileCount / fileAllDataCount * 100;
                    int progress = (int)_p;
                    if (progress > 100) progress = 100;
                    NowDownloadProgressEvent.Invoke(progress, new EventArgs());
                    // 
                    Thread.Sleep(30);
                    // 59 6D 6F 64 65   6D 5F 52 65 63      65 69 76 65 20      20 32 0D 0A
                    /* wait for ACK */
                    array = new byte[19];
                    this.serialPort.Read(array, 0, array.Length);
                    // 
                    temp = (byte)serialPort.ReadByte();
                    Console.WriteLine("temp,*," + temp);
                    // 
                    if (temp != ACK)
                    {
                        Console.WriteLine("temp,*," + temp);
                        Console.WriteLine("Couldn't send a packet.");
                        DownloadResultEvent.Invoke(false, new EventArgs());
                        return;// false;
                    }
                } while (1024 == fileReadCount);

                /* send EOT (tell the downloader we are finished) */
                serialPort.Write(new byte[] { EOT }, 0, 1);
                /* send closing packet */
                packetNum = 0;
                invertedPacketNum = 255;
                data = new byte[1024];
                CRC = new byte[2];

                sendYmodemClosingPacket(packetNum, invertedPacketNum, data, CRC);

            }
            catch (TimeoutException)
            {
                throw new Exception("Eductor does not answering");
            }
            finally
            {
                ms1.Close();
            }
            serialPort.Close();
            Console.WriteLine("File transfer is succesful");
            DownloadResultEvent.Invoke(true, new EventArgs());
            return;// true;
        }

        private void sendYmodemInitialPacket(int packetNumber, int invertedPacketNumber, byte[] data, string path, Stream ms1, byte[] CRC)
        {
            string fileName = System.IO.Path.GetFileName(path);
            string fileSize = ms1.Length.ToString();
            Console.WriteLine("ymode init fileSize" + fileSize);
            /* add filename to data */
            int i;
            for (i = 0; i < fileName.Length && (fileName.ToCharArray()[i] != 0); i++)
            {
                data[i] = (byte)fileName.ToCharArray()[i];
            }
            data[i] = 0;

            /* add filesize to data */
            int j;
            for (j = 0; j < fileSize.Length && (fileSize.ToCharArray()[j] != 0); j++)
            {
                data[(i + 1) + j] = (byte)fileSize.ToCharArray()[j];
            }
            data[(i + 1) + j] = 0;

            /* fill the remaining data bytes with 0 */
            for (int k = ((i + 1) + j) + 1; k < 1024; k++)
            {
                data[k] = 0;
            }

            /* calculate CRC */
            Crc16Ccitt crc16Ccitt = new Crc16Ccitt(InitialCrcValue.Zeros);
            CRC = crc16Ccitt.ComputeChecksumBytes(data);

            /* send the packet */
            SendYmodemPacket(packetNumber, invertedPacketNumber, data, CRC);
        }

        private void sendYmodemClosingPacket(int pn, int ipn, byte[] data, byte[] crc)
        {
            /* calculate CRC */
            Crc16Ccitt crc16Ccitt = new Crc16Ccitt(InitialCrcValue.Zeros);
            crc = crc16Ccitt.ComputeChecksumBytes(data);

            /* send the packet */
            SendYmodemPacket(pn, ipn, data, crc);
        }

        private void SendYmodemPacket(int pn, int ipn, byte[] data, byte[] CRC)
        {
            serialPort.Write(new byte[] { STX }, 0, 1);
            serialPort.Write(new byte[] { (byte)pn }, 0, 1);
            serialPort.Write(new byte[] { (byte)ipn }, 0, 1);
            Console.WriteLine("packetNum:" + pn + "\rinvertedPacketNum:" + ipn);
            serialPort.Write(data, 0, 1024);
            serialPort.Write(CRC, 0, 2);
        }
    }
    public class Crc16Ccitt
    {
        const ushort poly = 4129;
        ushort[] table = new ushort[256];
        ushort initialValue = 0;

        private ushort ComputeChecksum(byte[] bytes)
        {
            ushort crc = this.initialValue;
            for (int i = 0; i < bytes.Length; ++i)
            {
                crc = (ushort)((crc << 8) ^ table[((crc >> 8) ^ (0xff & bytes[i]))]);
            }
            return crc;
        }

        public byte[] ComputeChecksumBytes(byte[] bytes)
        {
            ushort crc = ComputeChecksum(bytes);
            return BitConverter.GetBytes(crc);
        }

        public Crc16Ccitt(InitialCrcValue initialValue)
        {
            this.initialValue = (ushort)0xffff;
            ushort temp, a;
            for (int i = 0; i < table.Length; ++i)
            {
                temp = 0;
                a = (ushort)(i << 8);
                for (int j = 0; j < 8; ++j)
                {
                    if (((temp ^ a) & 0x8000) != 0)
                    {
                        temp = (ushort)((temp << 1) ^ poly);
                    }
                    else
                    {
                        temp <<= 1;
                    }
                    a <<= 1;
                }
                table[i] = temp;
            }
        }
    }
}