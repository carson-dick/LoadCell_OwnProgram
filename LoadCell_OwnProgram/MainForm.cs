using System;
using System.Windows.Forms;
using System.Threading;
using FUTEK_USB_DLL;
using Zaber.Motion;
using Zaber.Motion.Ascii;
using SpinnakerNET;
using SpinnakerNET.GenApi;
using Spinnaker;
using System.IO;
using System.Collections.Generic;
using Zaber.Motion.Binary;

namespace LoadCell_OwnProgram
{
    public partial class MainForm : Form
    {
        private Thread _calculationThread;
        private Thread _linearAcctuatorThread;
        private Thread _initializationThread;
        private IntPtr DeviceHandle;
        private int DeviceStatus;
        private string t_Off_Val;
        private string t_Full_Val;
        private string t_FullLoad_Val;
        private string t_Deci_Point;
        private string t_NormData;
        private string t_UnitCode;
        public Int32 UnitCode;
        public Int32 OffsetVal;
        public Int32 FullVal;
        public Int32 FullLoadVal;
        public Int32 DeciPoint;
        public Int32 NormalVal;
        public Double CalcVal;
        public Int32 act_count;
        public StreamWriter writer;


        //This method moves the actuator to home position and then makes a messagebox telling the sample to be loaded
        private void Initialize()
        {
            using (var connection = Zaber.Motion.Ascii.Connection.OpenSerialPort("COM8")) //change this based on what computer you're using - port that your controller is connected to 
            {
            connection.EnableAlerts(); //connecting to actuator
            var deviceList = connection.DetectDevices();
            Console.WriteLine($"Found {deviceList.Length} devices.");
            var device = deviceList[0];
            var axis = device.GetAxis(1);
            if (!axis.IsHomed())
            {
                axis.Home();
            }
            axis.MoveAbsolute(60, Units.Length_Millimetres);
            MessageBox.Show("Grips are now at home position. Load the sample and press Start button when you are ready to begin test.");
            }
        }

        //This is the main method. 
        private void Running()
        {
            FUTEK_USB_DLL.USB_DLL futek = new FUTEK_USB_DLL.USB_DLL();
            // Connect to load cell
            futek.Open_Device_Connection("538827");
            DeviceStatus = futek.DeviceStatus;
            if (DeviceStatus == 0)
            {
            }
            DeviceHandle = futek.DeviceHandle;

            //Creating .csv file to write output data to 
            writer = new StreamWriter("data.csv"); // Create a new StreamWriter object to write to the CSV file
            writer.WriteLine($"'Time', 'Force [N]', 'Stress [MPa]'"); //What the .csv is actually 
            bool isFirstExport = true; //This is so that it exports the data at time 0

            // Initialize the first and most recent export time
            DateTime lastExportTime = DateTime.Now;
            DateTime firstExportTime = DateTime.Now;

            while (true) //main loop that runs the functions
            {
                //Load cell initialization
                t_Off_Val = futek.Get_Offset_Value(DeviceHandle);
                OffsetVal = Int32.Parse(t_Off_Val);
                t_Full_Val = futek.Get_Fullscale_Value(DeviceHandle);
                FullVal = Int32.Parse(t_Full_Val);
                t_FullLoad_Val = futek.Get_Fullscale_Load(DeviceHandle);
                FullLoadVal = Int32.Parse(t_FullLoad_Val);
                t_Deci_Point = futek.Get_Decimal_Point(DeviceHandle);
                DeciPoint = Int32.Parse(t_Deci_Point);
                t_NormData = futek.Normal_Data_Request(DeviceHandle);
                NormalVal = Int32.Parse(t_NormData);
                t_UnitCode = futek.Get_Unit_Code(DeviceHandle);
                UnitCode = Int32.Parse(t_UnitCode);

                //Calculate the force in lbf from load cell 
                CalcVal = (double)(NormalVal - OffsetVal) / (FullVal - OffsetVal) * FullLoadVal / Math.Pow(10, DeciPoint);
                decimal NewtForce = Convert.ToDecimal(CalcVal) * Convert.ToDecimal(4.4482189159); //Convert to Newton from lbf
                decimal stress = Convert.ToDecimal(NewtForce) / (Convert.ToDecimal(width) * Convert.ToDecimal(thick)); //Convert from Newtons to MPa

                //Calculating time delay based on acquistion rate, calcualting total time elapsed and delta 
                int time_between_data = (int)(1 / Convert.ToDecimal(acq_rate));//Calculating sleep time based on user entered acquisition rate
                TimeSpan timeSinceFirstExport = DateTime.Now - firstExportTime;//total time elapsed since first data point
                TimeSpan timeSinceLastExport = DateTime.Now - lastExportTime;//time elapsed between data points

                //Print Time in s to GUI
                if (InvokeRequired)
                {
                    Invoke(new Action(() => TimeOutput.Text = timeSinceFirstExport.ToString("mm\\:ss\\.ff")));
                }
                else
                {
                    TimeOutput.Text = timeSinceFirstExport.ToString("mm\\:ss\\.ff");
                }

                //Print Force in Newtons to GUI
                if (InvokeRequired)
                {
                    Invoke(new Action(() => ResultsLab.Text = Convert.ToDecimal(NewtForce).ToString("n2")));
                }
                else
                {
                    ResultsLab.Text = Convert.ToDecimal(NewtForce).ToString("n2");
                }
                //Print Stress in MPa to GUI
                if (InvokeRequired)
                {
                    Invoke(new Action(() => StressOutput.Text = stress.ToString("n2")));
                }
                else
                {
                    StressOutput.Text = stress.ToString("n2");
                }

                Console.WriteLine(act_count);
                // Export to CSV file every 5 seconds
                if (timeSinceLastExport.TotalSeconds >= time_between_data || isFirstExport) //added if statement so that it starts at 0 rather than 5
                {
                    writer.WriteLine($"{timeSinceFirstExport}, {NewtForce}, {stress}"); //What the .csv is actually writing
                    Console.WriteLine("timeSinceFirstExport " + timeSinceFirstExport);
                    lastExportTime = DateTime.Now;
                    isFirstExport = false; //so that it will take a data point at time 0. without this line it waits until after the first cycle so like t=5s is first data point
                }
                
            }
        }

        //This is to move the linear actuator
        private void LinearActuator()
        {
            using (var connection = Zaber.Motion.Ascii.Connection.OpenSerialPort("COM8")) //change this based on what computer you're using - port that your controller is connected to 
            {
                connection.EnableAlerts(); //connecting to actuator
                var deviceList = connection.DetectDevices();
                Console.WriteLine($"Found {deviceList.Length} devices.");
                var device = deviceList[0];
                var axis = device.GetAxis(1);
                if (!axis.IsHomed())
                {
                    axis.Home();
                }

                //calculating time_delay in order to get desired linear velocity 
                decimal velo = length * 1000 * strainrate; //this is in um/s
                int increment = -500; //should always be -1. This means grips move 1um at a time. To change velo, change time_delay via changing strain rate from GUI 
                decimal time_delay = 1 / velo * 1000; //converting velo to strain rate time delay [s] and then to [ms]                
                int act_count = 0; //initializing actuator count 


                //moving actuator at continuous strain rate
                while (true)
                {
                    axis.MoveRelative(increment, Units.Length_Micrometres);//this is where the strain rate is entered
                    act_count += 1; //keeping track of how many um the actuator has moved
                    
                    //Print displacement to GUI
                    if (InvokeRequired)
                    {
                        Invoke(new Action(() => disp_output.Text = Convert.ToDecimal(act_count).ToString("n2")));
                    }
                    else
                    {
                        disp_output.Text = Convert.ToDecimal(act_count).ToString("n2");
                    }
                    Thread.Sleep((int)time_delay); //should be 200 to have a strain rate of .001
                }
            }
        }




        //Initialize Button. Moves grip to home position and runs "Initialize" thread
        private void InitializeButton_Click(object sender, EventArgs e)
        {
            //Disable initialization button SEE IF THIS MATTERS
            InitializeButton.Enabled = false;
            //Start the intialization thread
            _initializationThread = new Thread(Initialize);
            _initializationThread.Start();
        }


        //Start Button. Activates Load Cell / data export thread and linear actuator thread.
        private void button3_Click(object sender, EventArgs e)
        {
            // Disable the start button
            button3.Enabled = false;
            // Start the calculation thread
            _calculationThread = new Thread(Running);
            _calculationThread.Start();
            //Moving the linear actuator
            _linearAcctuatorThread = new Thread(LinearActuator);
            _linearAcctuatorThread.Start();
        }

        //Stop Button.
        private void button4_Click(object sender, EventArgs e)
        {
            if (_calculationThread != null && _calculationThread.IsAlive)
            {
                _calculationThread.Abort();
                _linearAcctuatorThread.Abort();
            }
            //closes csv file
            writer.Close();
            button3.Enabled = true;
            this.Close();
            Application.Exit();
        }

        //defining geometries
        private decimal length = 5;
        private decimal width = 1;
        private decimal thick = 1;
        //this next part only matters if the user makes a change, otherwise it is just the above three lines 
        private void numericUpDown1_ValueChanged(object sender, EventArgs e)//taking user input to determine gauge length
        {
            length = (decimal)numericUpDown1.Value;
        }
        private void numericUpDown2_ValueChanged(object sender, EventArgs e)
        {
            width = (decimal)numericUpDown2.Value;
        }
        private void numericUpDown3_ValueChanged(object sender, EventArgs e)
        {
            thick = (decimal)numericUpDown3.Value;
        }
        //intaking strain rate and converting that to linear velocity in um/s
        private decimal strainrate = 0.001m; // default is 10^-3 [1/s]
        private void numericUpDown4_ValueChanged(object sender, EventArgs e)
        {
            strainrate = (decimal)numericUpDown4.Value;
        }
        public MainForm()
        {
            InitializeComponent();
        }
        private void MainForm_Load(object sender, EventArgs e) //pointless - move to end
        {

        }

        private void label2_Click(object sender, EventArgs e) //pointless, you wont click on the label
        {

        }
        private void label6_Click(object sender, EventArgs e) //pointless - move to end
        {

        }

        private void ResultsLab_Click(object sender, EventArgs e) //pointless - move to end
        {

        }

        //Acquisition Rate
        private decimal acq_rate = 0.2m; //default acquisiton rate set to 0.2 Hz
        private void numericUpDown5_ValueChanged(object sender, EventArgs e)
        {
            acq_rate = (decimal)numericUpDown5.Value; //if user input is entered then this changes acq rate variable
        }

        //trying to make the .csv file able to be renamed / relocated
        private void saveFileDialog1_FileOk(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveFileDialog saveFileDialog1 = new SaveFileDialog();

            saveFileDialog1.Filter = "CSV file (*.csv)|*.csv|All files (*.*)|*.*";
            saveFileDialog1.FilterIndex = 1;
            saveFileDialog1.RestoreDirectory = true;

            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                string filename = saveFileDialog1.FileName;
                // Save the file with the chosen file name and location
            }

        }
        //Time output to GUI
        private void label8_Click(object sender, EventArgs e)
        {

        }
    }
}
