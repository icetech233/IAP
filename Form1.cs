using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Ymodem;
namespace IAP
{
    
   
    public partial class mainForm : Form
    {
        public mainForm()
        {
            InitializeComponent();
        }

        private void mainForm_Load(object sender, EventArgs e)
        {
            foreach (string s in System.IO.Ports.SerialPort.GetPortNames())
            {
                this.SerialPortComboBox.Items.Add(s);
                this.SerialPortComboBox.Text = this.SerialPortComboBox.Items[0].ToString();
            }
        }


        private void selectFileButton_Click(object sender, EventArgs e)
        {
            Button button = (Button)sender;
            if (button.Text == "选择程序")
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    this.pathTextBox.Text = openFileDialog.FileName.ToString();
                }
            }

        }

        private void downloadButton_Click(object sender, EventArgs e)
        {
            Button button = (Button)sender;

            if (button.Text == "开始下载")
            {
                button.Text = "正在下载";
                ymodem = new Ymodem.Ymodem();
                ymodem.Path = pathTextBox.Text.ToString();

                ymodem.PortName = SerialPortComboBox.SelectedItem.ToString();
                ymodem.BaudRate = 921600;// Convert.ToInt32(BaudRateComboBox.SelectedItem.ToString());
                downloadThread = new System.Threading.Thread(ymodem.YmodemUploadFile);
                ymodem.NowDownloadProgressEvent += new EventHandler(NowDownloadProgressEvent);
                ymodem.DownloadResultEvent += new EventHandler(DownloadFinishEvent);
                downloadThread.Start();
            } 
        }
        #region 下载进度委托及事件响应
        private delegate void NowDownloadProgress(int nowValue);
        private void NowDownloadProgressEvent(object sender, EventArgs e)
        {
            int value = Convert.ToInt32(sender);
            NowDownloadProgress count = new NowDownloadProgress(UploadFileProgress);
           this.Invoke(count, value);
        }
        private void UploadFileProgress(int count)
        {
            DownloadProgressBar.Value = count;
        }
        #endregion
        #region 下载完成委托及事件响应
        private delegate void DownloadFinish(bool finish);
        private void DownloadFinishEvent(object sender, EventArgs e)
        {
            bool finish = (bool)sender;
            DownloadFinish status = new DownloadFinish(UploadFileResult);
            this.Invoke(status, finish);
        }
        private void UploadFileResult(bool result)
        {
            if (result == true)
            {
                //MessageBox.Show("下载成功");
                this.downloadButton.Text = "下载成功";
                this.DownloadProgressBar.Value = 0;
            }
            else
            {
                MessageBox.Show("下载失败");
                this.downloadButton.Text = "开始下载";
                this.DownloadProgressBar.Value = 0;
            }
        }
        #endregion

        private void DownloadProgressBar_Click(object sender, EventArgs e)
        {

        }

    }
}
