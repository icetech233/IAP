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

        // const byte C = 67;   // 4*16 + 3 capital letter C

        public void YmodemUploadFile()
        {
            /* control signals */
            // 1k 数据头
            const byte STX = 2;  // Start of TeXt 
            // EOT 04H 发送结束
            const byte EOT = 4;  // End Of Transmission
            // 确认消息
            const byte ACK = 6;  // Positive ACknowledgement
            // const byte C = 67;   // 4*16 + 3 capital letter C
            
            /* sizes */
            const int dataSize = 1024;
            const int crcSize = 2;

            /* THE PACKET: 1029 bytes */
            /* header: 3 bytes */
            // STX
            int packetNumber = 0;
            int invertedPacketNumber = 255;
            /* data: 1024 bytes */
            byte[] data = new byte[dataSize];
            /* footer: 2 bytes */
            byte[] CRC = new byte[crcSize];

            /* get the file */
            FileStream fileStream = new FileStream(@path, FileMode.Open, FileAccess.Read);
            serialPort.PortName = portName; 
            serialPort.BaudRate = 921600;


            serialPort.DataBits = 8;
            serialPort.StopBits = StopBits.One;
            serialPort.Parity = Parity.None;

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
 
                sendYmodemInitialPacket(STX, packetNumber, invertedPacketNumber, data, dataSize, path, fileStream, CRC, crcSize);
               // Thread.Sleep(5000);
                byte temp = (byte)serialPort.ReadByte();
                Console.WriteLine("init temp"+temp);
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
                array = new byte[this.serialPort.BytesToRead];
                aa = this.serialPort.Read(array, 0, array.Length);
                Console.WriteLine("清空缓存clean read cache; array len:" + array.Length);

                /* send packets with a cycle until we send the last byte */
                int fileReadCount;
                do
                {
                    data = new byte[dataSize];
                    /* if this is the last packet fill the remaining bytes with 0 */
                    fileReadCount = fileStream.Read(data, 0, dataSize);
                    if (fileReadCount == 0)
                    {
                        break;
                    }
                    // 
                    if (fileReadCount != dataSize) 
                    {
                        // 数据帧 用 0x1a 补全
                        for (int i = fileReadCount; i < dataSize; i++) 
                        {
                            data[i] = 0x1A;
                        }
                    }

                    /* calculate packetNumber */
                    packetNumber++;
                    if (packetNumber > 255)
                        packetNumber -= 256;
                    //Console.WriteLine(packetNumber);

                    /* calculate invertedPacketNumber */
                    invertedPacketNumber = 255 - packetNumber;

                    /* calculate CRC */
                    Crc16Ccitt crc16Ccitt = new Crc16Ccitt(InitialCrcValue.Zeros);
                    CRC = crc16Ccitt.ComputeChecksumBytes(data);

                    /* send the packet */
                    sendYmodemPacket(STX, packetNumber, invertedPacketNumber, data, dataSize, CRC, crcSize);
                    int progress = (int)(((float)dataSize * packetNumber) / fileStream.Length * 100);
                    Console.WriteLine("progress:  " + progress);
                    if (progress > 100) progress = 100;
                    NowDownloadProgressEvent.Invoke(progress, new EventArgs());
                    // 
                    Thread.Sleep(40);
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
                } while (dataSize == fileReadCount);

                /* send EOT (tell the downloader we are finished) */
                serialPort.Write(new byte[] { EOT }, 0, 1);
                /* send closing packet */
                packetNumber = 0;
                invertedPacketNumber = 255;
                data = new byte[dataSize];
                CRC = new byte[crcSize];


                sendYmodemClosingPacket(STX, packetNumber, invertedPacketNumber, data, dataSize, CRC, crcSize);
                /* get ACK (downloader acknowledge the EOT) */
                //if (serialPort.ReadByte() != ACK)
                //{
                //    Console.WriteLine("Can't complete the transfer.");
                //    DownloadResultEvent.Invoke(false, new EventArgs());
                //    return;// false;
                //}
            }
            catch (TimeoutException)
            {
                throw new Exception("Eductor does not answering");
                
            }
            finally
            {
                fileStream.Close();

            }
            serialPort.Close();
            Console.WriteLine("File transfer is succesful");
            DownloadResultEvent.Invoke(true,new EventArgs());
            return;// true;
        }

        private void sendYmodemInitialPacket(byte STX, int packetNumber, int invertedPacketNumber, byte[] data, int dataSize, string path, FileStream fileStream, byte[] CRC, int crcSize)
        {
            string fileName = System.IO.Path.GetFileName(path);
            string fileSize = fileStream.Length.ToString();
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
            for (int k = ((i + 1) + j) + 1; k < dataSize; k++)
            {
                data[k] = 0;
            }

            /* calculate CRC */
            Crc16Ccitt crc16Ccitt = new Crc16Ccitt(InitialCrcValue.Zeros);
            CRC = crc16Ccitt.ComputeChecksumBytes(data);

            /* send the packet */
            sendYmodemPacket(STX, packetNumber, invertedPacketNumber, data, dataSize, CRC, crcSize);
        }

        private void sendYmodemClosingPacket(byte STX, int packetNumber, int invertedPacketNumber, byte[] data, int dataSize, byte[] CRC, int crcSize)
        {
            /* calculate CRC */
            Crc16Ccitt crc16Ccitt = new Crc16Ccitt(InitialCrcValue.Zeros);
            CRC = crc16Ccitt.ComputeChecksumBytes(data);

            /* send the packet */
            sendYmodemPacket(STX, packetNumber, invertedPacketNumber, data, dataSize, CRC, crcSize);
        }

        private void sendYmodemPacket(byte STX, int packetNumber, int invertedPacketNumber, byte[] data, int dataSize, byte[] CRC, int crcSize)
        {
            serialPort.Write(new byte[] { STX }, 0, 1);
            serialPort.Write(new byte[] { (byte)packetNumber }, 0, 1);
            serialPort.Write(new byte[] { (byte)invertedPacketNumber }, 0, 1);
            Console.WriteLine("\rpacketNumber:" + packetNumber + "\rinvertedPacketNumber:" + invertedPacketNumber);
            serialPort.Write(data, 0, dataSize);
            serialPort.Write(CRC, 0, crcSize);
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