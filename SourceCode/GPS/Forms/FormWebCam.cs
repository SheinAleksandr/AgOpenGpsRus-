using System;
using System.Threading;
using System.Windows.Forms;
using Accord.Video;
using Accord.Video.DirectShow;

namespace AgOpenGPS
{
    public partial class FormWebCam : Form
    {
        private FilterInfoCollection _videoDevices;
        private bool _wifiStopping;

        public FormWebCam()
        {
            InitializeComponent();
        }

        private void FormWebCam_Load(object sender, EventArgs e)
        {
            _videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

            foreach (var videoDevice in _videoDevices)
            {
                deviceComboBox.Items.Add(videoDevice.Name);
            }

            if (deviceComboBox.Items.Count > 0)
            {
                deviceComboBox.SelectedItem = deviceComboBox.Items[0];
            }

            UpdateButtons();
        }

        private void UpdateButtons()
        {
            if (wifiCheckBox.Checked)
                startButton.Enabled = !string.IsNullOrWhiteSpace(wifiUrlTextBox.Text);
            else
                startButton.Enabled = deviceComboBox.SelectedItem != null;

            stopButton.Enabled = videoSourcePlayer.IsRunning;
        }

        private void wifiCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            deviceComboBox.Visible = !wifiCheckBox.Checked;
            wifiUrlTextBox.Visible = wifiCheckBox.Checked;
            UpdateButtons();
        }

        private void wifiUrlTextBox_TextChanged(object sender, EventArgs e)
        {
            UpdateButtons();
        }

        private void deviceComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateButtons();
        }

        private void StartMjpegStream()
        {
            _wifiStopping = false;
            var stream = new MJPEGStream(wifiUrlTextBox.Text.Trim());
            stream.RequestTimeout = 10000;
            stream.VideoSourceError += MjpegStream_VideoSourceError;
            videoSourcePlayer.VideoSource = stream;
            videoSourcePlayer.Start();
        }

        private void MjpegStream_VideoSourceError(object sender, VideoSourceErrorEventArgs e)
        {
            if (_wifiStopping) return;

            // переподключение из UI-потока
            BeginInvoke(new Action(() =>
            {
                if (_wifiStopping) return;
                try
                {
                    videoSourcePlayer.SignalToStop();
                    var t = new Thread(() => { try { videoSourcePlayer.WaitForStop(); } catch { } });
                    t.IsBackground = true;
                    t.Start();
                    t.Join(3000);
                    if (t.IsAlive)
                        videoSourcePlayer.Stop();
                }
                catch { }

                StartMjpegStream();
            }));
        }

        private void startButton_Click(object sender, EventArgs e)
        {
            if (wifiCheckBox.Checked)
            {
                StartMjpegStream();
            }
            else
            {
                var videoSource = new VideoCaptureDevice(_videoDevices[deviceComboBox.SelectedIndex].MonikerString);
                videoSourcePlayer.VideoSource = videoSource;
                videoSourcePlayer.Start();
            }

            UpdateButtons();
        }

        private void stopButton_Click(object sender, EventArgs e)
        {
            _wifiStopping = true;
            try
            {
                videoSourcePlayer.SignalToStop();
                var t = new Thread(() => { try { videoSourcePlayer.WaitForStop(); } catch { } });
                t.IsBackground = true;
                t.Start();
                t.Join(3000);
                if (t.IsAlive)
                    videoSourcePlayer.Stop();
            }
            catch { }

            UpdateButtons();
        }

        private void FormWebCam_FormClosing(object sender, FormClosingEventArgs e)
        {
            _wifiStopping = true;
            try
            {
                videoSourcePlayer.SignalToStop();
            }
            catch { }
        }
    }
}
