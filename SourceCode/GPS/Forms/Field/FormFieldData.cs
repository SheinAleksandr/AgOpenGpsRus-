//Please, if you use this give me some credit
//Copyright BrianTee, copy right out of it.

using AgOpenGPS.Core.Translations;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public partial class FormFieldData : Form
    {
        private const int GrainButtonWidth = 175;
        private const int GrainButtonHeight = 28;
        private static readonly (string Name, double MaxCha)[] CropPresets =
        {
            ("Пшеница", 35.0),
            ("Ячмень", 20.0),
            ("Подсолнечник", 20.0),
            ("Нут", 20.0),
        };
        private readonly FormGPS mf = null;
        private readonly Label labelGrain = new Label();
        private readonly Label lblGrainData = new Label();
        private readonly Button btnSetEmptyBaseline = new Button();
        private readonly Button btnSetYieldScaleK = new Button();
        private readonly Button btnSelectCrop = new Button();
        private readonly Button btnSetDelay = new Button();
        private readonly Button btnRawLogToggle = new Button();

        public FormFieldData(Form callingForm)
        {
            mf = callingForm as FormGPS;
            InitializeComponent();
            labelTotal.Text = gStr.gsTotal + ":";
            labelWorked.Text = gStr.gsWorked;
            labelApplied.Text = gStr.gsApplied + ":";
            labelApplied2.Text = gStr.gsApplied + ":";
            labelRemain.Text = gStr.gsRemain + ":";
            labelRemain2.Text = gStr.gsRemain + ":";
            labelOverlap.Text = gStr.gsOverlap + ":";
            labelActual.Text = gStr.gsActual;
            labelRate.Text = gStr.gsRate + ":";
            labelArea.Text = gStr.gsArea + ":";
            labelDistance.Text = gStr.gsDistance + ":";

            // Extra live diagnostics for CAN grain sensor.
            this.ClientSize = new Size(this.ClientSize.Width, 720);

            labelGrain.AutoSize = true;
            labelGrain.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(0)));
            labelGrain.ForeColor = Color.White;
            labelGrain.Location = new Point(10, 490);
            labelGrain.Name = "labelGrain";
            labelGrain.Text = "Grain CAN:";

            lblGrainData.AutoSize = true;
            lblGrainData.Font = new Font("Tahoma", 9.75F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(0)));
            lblGrainData.ForeColor = Color.White;
            lblGrainData.Location = new Point(10, 512);
            lblGrainData.Name = "lblGrainData";
            lblGrainData.Text = "-";

            btnSetEmptyBaseline.Font = new Font("Tahoma", 8.25F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(0)));
            btnSetEmptyBaseline.ForeColor = Color.Black;
            btnSetEmptyBaseline.BackColor = Color.Gainsboro;
            btnSetEmptyBaseline.FlatStyle = FlatStyle.Flat;
            btnSetEmptyBaseline.Location = new Point(10, 566);
            btnSetEmptyBaseline.Size = new Size(GrainButtonWidth, GrainButtonHeight);
            btnSetEmptyBaseline.Text = "Empty conveyor = 0";
            btnSetEmptyBaseline.Click += BtnSetEmptyBaseline_Click;

            btnSetYieldScaleK.Font = new Font("Tahoma", 8.25F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(0)));
            btnSetYieldScaleK.ForeColor = Color.Black;
            btnSetYieldScaleK.BackColor = Color.Gainsboro;
            btnSetYieldScaleK.FlatStyle = FlatStyle.Flat;
            btnSetYieldScaleK.Location = new Point(10, 596);
            btnSetYieldScaleK.Size = new Size(GrainButtonWidth, GrainButtonHeight);
            btnSetYieldScaleK.Text = "Set K";
            btnSetYieldScaleK.Click += BtnSetYieldScaleK_Click;

            btnSelectCrop.Font = new Font("Tahoma", 8.25F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(0)));
            btnSelectCrop.ForeColor = Color.Black;
            btnSelectCrop.BackColor = Color.Gainsboro;
            btnSelectCrop.FlatStyle = FlatStyle.Flat;
            btnSelectCrop.Location = new Point(10, 626);
            btnSelectCrop.Size = new Size(GrainButtonWidth, GrainButtonHeight);
            btnSelectCrop.Text = "Crop";
            btnSelectCrop.Click += BtnSelectCrop_Click;

            btnSetDelay.Font = new Font("Tahoma", 8.25F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(0)));
            btnSetDelay.ForeColor = Color.Black;
            btnSetDelay.BackColor = Color.Gainsboro;
            btnSetDelay.FlatStyle = FlatStyle.Flat;
            btnSetDelay.Location = new Point(10, 656);
            btnSetDelay.Size = new Size(GrainButtonWidth, GrainButtonHeight);
            btnSetDelay.Text = "Delay";
            btnSetDelay.Click += BtnSetDelay_Click;

            btnRawLogToggle.Font = new Font("Tahoma", 8.25F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(0)));
            btnRawLogToggle.ForeColor = Color.Black;
            btnRawLogToggle.BackColor = Color.Gainsboro;
            btnRawLogToggle.FlatStyle = FlatStyle.Flat;
            btnRawLogToggle.Location = new Point(10, 686);
            btnRawLogToggle.Size = new Size(GrainButtonWidth, GrainButtonHeight);
            btnRawLogToggle.Text = "RAW log OFF";
            btnRawLogToggle.Click += BtnRawLogToggle_Click;

            Controls.Add(labelGrain);
            Controls.Add(lblGrainData);
            Controls.Add(btnSetEmptyBaseline);
            Controls.Add(btnSetYieldScaleK);
            Controls.Add(btnSelectCrop);
            Controls.Add(btnSetDelay);
            Controls.Add(btnRawLogToggle);

        }
        private void FormFieldData_Load(object sender, EventArgs e)
        {
            timer1_Tick(this, EventArgs.Empty);

        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            //lblEastingField.Text = Math.Round(mf.pn.fix.easting, 1).ToString();
            //lblNorthingField.Text = Math.Round(mf.pn.fix.northing, 1).ToString();

            lblOverlapPercent.Text = mf.fd.ActualOverlapPercent;

            if (mf.isMetric)
            {
                lblWorkRate.Text = mf.fd.WorkRateHectares;
                lblApplied.Text = mf.fd.WorkedHectares;
                lblActualLessOverlap.Text = mf.fd.ActualAreaWorkedHectares;
                labelAreaValue.Text = mf.fd.WorkedUserHectares + " ha";
                labelDistanceDriven.Text = mf.fd.DistanceUserMeters + " m";

            }
            else
            {
                lblWorkRate.Text = mf.fd.WorkRateAcres;
                lblApplied.Text = mf.fd.WorkedAcres;
                lblActualLessOverlap.Text = mf.fd.ActualAreaWorkedAcres;
                labelAreaValue.Text = mf.fd.WorkedUserAcres + " ac";
                labelDistanceDriven.Text = mf.fd.DistanceUserFeet + " ft";
            }

            if (mf.bnd.bndList.Count > 0)
            {
                lblTimeRemaining.Text = mf.fd.TimeTillFinished;
                lblRemainPercent.Text = mf.fd.WorkedAreaRemainPercentage;
                lblTotalArea.Visible = true;
                lblAreaRemain.Visible = true;
                lblTimeRemaining.Visible = true;
                lblRemainPercent.Visible = true;
                labelRemain.Visible = true;
                lblActualRemain.Visible = true;
                labelRemain2.Visible = true;

                if (mf.isMetric)
                {
                    lblTotalArea.Text = mf.fd.AreaBoundaryLessInnersHectares;
                    lblAreaRemain.Text = mf.fd.WorkedAreaRemainHectares;
                    lblActualRemain.Text = mf.fd.ActualRemainHectares;
                }
                else
                {
                    lblTotalArea.Text = mf.fd.AreaBoundaryLessInnersAcres;
                    lblAreaRemain.Text = mf.fd.WorkedAreaRemainAcres;
                    lblActualRemain.Text = mf.fd.ActualRemainAcres;
                }
            }
            else
            {
                lblTotalArea.Visible = false;
                lblAreaRemain.Visible = false;
                lblTimeRemaining.Visible = false;
                lblRemainPercent.Visible = false;
                lblActualRemain.Visible = false;
                labelRemain2.Visible = false;
                labelRemain.Visible = false;

                //if (mf.isMetric) lblActualLessOverlap.Text = 
                //        ((100-mf.fd.overlapPercent) * 0.01 * mf.fd.workedAreaTotal * glm.m2ha).ToString("N2");
                //else
                //    lblActualLessOverlap.Text =
                //    ((100-mf.fd.overlapPercent) * 0.01 * mf.fd.workedAreaTotal * glm.m2ac).ToString("N2");
            }

            if (mf.usbCan != null && mf.usbCan.grainSensor != null && mf.usbCan.grainSensor.HasRecentData())
            {
                GrainCanSensor gs = mf.usbCan.grainSensor;


                bool hasYieldCha = gs.TryGetYieldCentnerPerHa(
                    mf.avgSpeed,
                    mf.tool.width,
                    out double yieldCha,
                    Properties.Settings.Default.setYieldMap_emptyBaseline,
                    Properties.Settings.Default.setYieldMap_scaleK);

                double fillPct = gs.Fill255 * 100.0 / 255.0;
                string chaText = hasYieldCha ? yieldCha.ToString("N1") : "-";
                string kText = Properties.Settings.Default.setYieldMap_scaleK.ToString("N2");
                string dText = Properties.Settings.Default.setYieldMap_transportDelaySec.ToString("N1");
                bool almostEmpty = (gs.Flags & (1 << 1)) != 0;
                bool almostBlocked = (gs.Flags & (1 << 2)) != 0;
                bool noData = (gs.Flags & (1 << 3)) != 0;

                string stateText;
                if (noData) stateText = "Нет данных";
                else if (almostBlocked) stateText = "Почти перекрыт";
                else if (almostEmpty) stateText = "Низкий поток";
                else stateText = "Норма";

                labelGrain.Text = $"Grain CAN: {stateText}";

                lblGrainData.Text =
                    $"Fill {gs.Fill255} ({fillPct:N0}%)  Freq {gs.FrequencyHz:N1} Hz{Environment.NewLine}" +
                    $"Ton {gs.TonMs:N1} ms  Per {gs.PeriodMs:N1} ms{Environment.NewLine}" +
                    $"Yield {chaText} ц/га  K {kText}  D {dText}s";
            }
            else
            {
                labelGrain.Text = "Grain CAN: Нет данных";
                lblGrainData.Text = "No CAN grain data";
            }

            btnSetYieldScaleK.Text = $"K = {Properties.Settings.Default.setYieldMap_scaleK:N2}";
            btnSelectCrop.Text = $"{Properties.Settings.Default.setYieldMap_cropName} Max {Properties.Settings.Default.setYieldMap_colorMaxCha:N0}";
            btnSetDelay.Text = $"Delay = {Properties.Settings.Default.setYieldMap_transportDelaySec:N1}s";
            btnRawLogToggle.Text = (mf?.usbCan != null && mf.usbCan.GrainRawLogEnabled) ? "RAW log ON" : "RAW log OFF";
        }

        private async void BtnSetEmptyBaseline_Click(object sender, EventArgs e)
        {
            if (mf?.usbCan?.grainSensor == null) return;
            const double stationarySpeedKmh = 0.3;
            if (Math.Abs(mf.avgSpeed) > stationarySpeedKmh)
            {
                btnSetEmptyBaseline.Text = "Stop machine for calibration";
                await Task.Delay(1200);
                btnSetEmptyBaseline.Text = "Empty conveyor = 0";
                return;
            }

            const int sampleDurationMs = 15000;
            const int sampleDelayMs = 100;
            const double trimRatio = 0.10;

            btnSetEmptyBaseline.Enabled = false;
            string oldText = btnSetEmptyBaseline.Text;
            btnSetEmptyBaseline.Text = "Calibrating...";
            try
            {
                List<double> samples = new List<double>(sampleDurationMs / sampleDelayMs);
                int loops = sampleDurationMs / sampleDelayMs;

                for (int i = 0; i < loops; i++)
                {
                    // Calibration is valid only while machine is stationary.
                    if (Math.Abs(mf.avgSpeed) > stationarySpeedKmh)
                    {
                        btnSetEmptyBaseline.Text = "Calibration canceled: machine moving";
                        await Task.Delay(1200);
                        return;
                    }

                    if (mf.usbCan.grainSensor.TryGetFlowProxy(out double proxy))
                    {
                        samples.Add(Math.Max(0.0, proxy));
                    }
                    await Task.Delay(sampleDelayMs);
                }

                if (samples.Count == 0) return;

                samples.Sort();

                int trimCount = (int)(samples.Count * trimRatio);
                int from = trimCount;
                int to = samples.Count - trimCount;
                if (to <= from)
                {
                    from = 0;
                    to = samples.Count;
                }

                double sum = 0.0;
                int valid = 0;
                for (int i = from; i < to; i++)
                {
                    sum += samples[i];
                    valid++;
                }

                if (valid == 0) return;

                Properties.Settings.Default.setYieldMap_emptyBaseline = sum / valid;
                Properties.Settings.Default.Save();
                timer1_Tick(this, EventArgs.Empty);
            }
            finally
            {
                btnSetEmptyBaseline.Text = oldText;
                btnSetEmptyBaseline.Enabled = true;
            }
        }


        private void BtnSetYieldScaleK_Click(object sender, EventArgs e)
        {
            using (FormNumeric form = new FormNumeric(0.0, 10000.0, Properties.Settings.Default.setYieldMap_scaleK))
            {
                form.Text = "Yield scale K";
                if (form.ShowDialog(this) != DialogResult.OK) return;

                Properties.Settings.Default.setYieldMap_scaleK = Math.Max(0.0, form.ReturnValue);
                Properties.Settings.Default.Save();
                timer1_Tick(this, EventArgs.Empty);
            }
        }

        private void BtnSelectCrop_Click(object sender, EventArgs e)
        {
            string current = Properties.Settings.Default.setYieldMap_cropName ?? "";
            int idx = Array.FindIndex(CropPresets, p => string.Equals(p.Name, current, StringComparison.Ordinal));
            if (idx < 0) idx = 0;
            else idx = (idx + 1) % CropPresets.Length;

            var preset = CropPresets[idx];
            Properties.Settings.Default.setYieldMap_cropName = preset.Name;
            Properties.Settings.Default.setYieldMap_colorMinCha = 0.0;
            Properties.Settings.Default.setYieldMap_colorMaxCha = preset.MaxCha;
            Properties.Settings.Default.Save();
            timer1_Tick(this, EventArgs.Empty);
        }

        private void BtnSetDelay_Click(object sender, EventArgs e)
        {
            using (FormNumeric form = new FormNumeric(0.0, 20.0, Properties.Settings.Default.setYieldMap_transportDelaySec))
            {
                form.Text = "Transport delay, sec";
                if (form.ShowDialog(this) != DialogResult.OK) return;

                Properties.Settings.Default.setYieldMap_transportDelaySec = Math.Max(0.0, form.ReturnValue);
                Properties.Settings.Default.Save();
                timer1_Tick(this, EventArgs.Empty);
            }
        }

        private void BtnRawLogToggle_Click(object sender, EventArgs e)
        {
            if (mf?.usbCan == null) return;
            if (!mf.isJobStarted)
            {
                btnRawLogToggle.Text = "Open field first";
                return;
            }

            bool enable = !mf.usbCan.GrainRawLogEnabled;
            string fieldDir = Path.Combine(RegistrySettings.fieldsDirectory, mf.currentFieldDirectory);
            mf.usbCan.SetGrainRawLog(enable, fieldDir);
            timer1_Tick(this, EventArgs.Empty);
        }
        private void btnTripReset_Click(object sender, EventArgs e)
        {
            mf.fd.workedAreaTotalUser = 0;
            mf.fd.distanceUser = 0;
        }
    }
}

//lblLookOnLeft.Text = mf.tool.lookAheadDistanceOnPixelsLeft.ToString("N0");
//lblLookOnRight.Text = mf.tool.lookAheadDistanceOnPixelsRight.ToString("N0");
//lblLookOffLeft.Text = mf.tool.lookAheadDistanceOffPixelsLeft.ToString("N0");
//lblLookOffRight.Text = mf.tool.lookAheadDistanceOffPixelsRight.ToString("N0");

//lblLeftToolSpd.Text = (mf.tool.toolFarLeftSpeed*3.6).ToString("N1");
//lblRightToolSpd.Text = (mf.tool.toolFarRightSpeed*3.6).ToString("N1");

//lblSectSpdLeft.Text = (mf.section[0].speedPixels*0.36).ToString("N1");
//lblSectSpdRight.Text = (mf.section[mf.tool.numOfSections-1].speedPixels*0.36).ToString("N1");









