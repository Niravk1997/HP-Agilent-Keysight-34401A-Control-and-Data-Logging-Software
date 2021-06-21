using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Speech.Synthesis;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Reflection;

namespace HP_34401A
{
    public static class Serial_COM_Info
    {
        public static bool isConnected = false;

        //HP 34401A COM Device Info
        public static string COM_Port;
        public static int COM_BaudRate;
        public static int COM_Parity;
        public static int COM_StopBits;
        public static int COM_DataBits;
        public static int COM_Handshake;
        public static int COM_WriteTimeout;
        public static int COM_ReadTimeout;
        public static bool COM_RtsEnable;
        public static int GPIB_Address;

        public static string folder_Directory;
    }

    public partial class MainWindow : Window
    {
        //Reference to the graph window
        Graphing_Window HP34401A_Graph_Window;

        //Reference to the N graph Window
        N_Sample_Graph_Window HP34401A_N_Graph_Window;

        //Reference to Measurement Table
        Measurement_Data_Table HP34401A_Table;
        string Current_Measurement_Unit = "VDC";

        //HP34401A serial connection
        SerialPort HP34401A;

        //Which Measurement is currently selected
        int Measurement_Selected = 0;
        int Selected_Measurement_type = 0;
        //VDC = 0
        //ADC = 1
        //VAC = 2
        //AAC = 3
        //2Ohm = 4
        //4Ohm = 5
        //FREQ = 6
        //PER = 7
        //DIODE = 8
        //CONT = 9

        //All Serial Write Commands are stored in this queue
        BlockingCollection<string> SerialWriteQueue = new BlockingCollection<string>();

        //Clear Logs after this count
        int Auto_Clear_Output_Log_Count = 20;

        //Lets the function know the queue has data
        bool isUserSendCommand = false;
        bool isSamplingOnly = false;
        bool isUpdateSpeed_Changed = false;

        //User set Null Value
        double Null_Value = 0;

        //User decides whether to save data to text file or not
        //to save output log or not
        bool saveOutputLog = false;
        //to save measurements or not
        bool saveMeasurements = false;
        //to add data to table
        bool save_to_Table = false;
        //to add data to graphs
        bool save_to_Graph = false;
        bool Save_to_N_Graph = false;

        //Data is stored in these queues, waiting for it to be written to text files
        BlockingCollection<string> save_data_VDC = new BlockingCollection<string>();
        BlockingCollection<string> save_data_ADC = new BlockingCollection<string>();
        BlockingCollection<string> save_data_VAC = new BlockingCollection<string>();
        BlockingCollection<string> save_data_AAC = new BlockingCollection<string>();
        BlockingCollection<string> save_data_2Ohm = new BlockingCollection<string>();
        BlockingCollection<string> save_data_4Ohm = new BlockingCollection<string>();
        BlockingCollection<string> save_data_FREQ = new BlockingCollection<string>();
        BlockingCollection<string> save_data_PER = new BlockingCollection<string>();
        BlockingCollection<string> save_data_DIODE = new BlockingCollection<string>();
        BlockingCollection<string> save_data_CONT = new BlockingCollection<string>();

        //Options for Speech Synthesizer
        SpeechSynthesizer Voice = new SpeechSynthesizer();
        int Speech_Value_Precision = 1;
        int isSpeechActive = 0;
        int isSpeechContinuous = 0;
        int isSpeechMIN = 0;
        int isSpeechMAX = 0;
        double Speech_Continuous_Voice_Value = 0;
        double Speech_min_value = 0;
        double Speech_max_value = 0;

        //Default border color for when a switch is selected or not
        SolidColorBrush Selected = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00CE30"));
        SolidColorBrush Deselected = new SolidColorBrush((Color)ColorConverter.ConvertFromString("White"));

        //Options for Measurement Data sampling speed
        double UpdateSpeed = 1000;

        //COM Select Window
        COM_Select_Window COM_Select;

        //Timer for getting data from multimeter at specified update speed.
        private System.Timers.Timer Speech_MIN_Max;
        private System.Timers.Timer Speech_Measurement_Interval;
        private System.Timers.Timer DataTimer;
        private DispatcherTimer runtime_Timer;
        private DispatcherTimer Process_Data;
        private System.Timers.Timer saveMeasurements_Timer;

        //Allow data timer to get data from multimeter or not
        bool DataSampling = false;

        //Data is stored here for display
        BlockingCollection<string> measurements = new BlockingCollection<string>();
        int Total_Samples = 0;
        int Invalid_Samples = 0;

        //Display Measurement as
        bool Partial_SI_Prefix = true;
        bool Full_SI_Prefix = false;

        //Calculate Runtime from this
        DateTime StartDateTime;

        //Min, Max, Avg values
        //Program will compare input values to these value
        //and update these values
        double min = 0;
        double max = 0;
        double avg = 0;
        int AVG_Calculate = 1;
        int avg_count = 0;
        int avg_factor = 1000;
        int avg_resolution = 5;
        int resetMinMaxAvg = 1;

        public MainWindow()
        {
            InitializeComponent();
            if (Thread.CurrentThread.CurrentCulture.Name != "en-US")
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-US");
                Thread.CurrentThread.CurrentUICulture = CultureInfo.CreateSpecificCulture("en-US");
                insert_Log("Culture set to en-US, decimal numbers will use dot as the seperator.", 0);
                insert_Log("Write decimal values with a dot as a seperator, not a comma.", 2);
            }
            Create_GetDataTimer();
            General_Timer();
            SetupSpeechSythesis();
            Check_Speech_MIN_MAX_Timer();
            Continuous_Voice_Measurement();
            Save_measurements_to_files_Timer();
            Load_Main_Window_Settings();
            insert_Log("Click the Config Menu then click Connect.", 5);
            insert_Log("AR488 GPIB Adapter and a HP 34401A are required to use this software.", 5);
        }

        private void Save_measurements_to_files_Timer()
        {
            saveMeasurements_Timer = new System.Timers.Timer();
            saveMeasurements_Timer.Interval = 60000; //Default is 1 minute;
            saveMeasurements_Timer.AutoReset = false;
            saveMeasurements_Timer.Enabled = false;
            saveMeasurements_Timer.Elapsed += Save_MeasurementData_to_files;
        }

        private void Save_MeasurementData_to_files(Object source, ElapsedEventArgs e)
        {
            string Date = DateTime.UtcNow.ToString("yyyy-MM-dd");
            int VDC_Count = save_data_VDC.Count;
            int ADC_Count = save_data_ADC.Count;
            int VAC_Count = save_data_VAC.Count;
            int AAC_Count = save_data_AAC.Count;
            int TwoOhm_Count = save_data_2Ohm.Count;
            int FourOhm_Count = save_data_4Ohm.Count;
            int FREQ_Count = save_data_FREQ.Count;
            int PER_Count = save_data_PER.Count;
            int DIODE_Count = save_data_DIODE.Count;
            int CONT_Count = save_data_CONT.Count;

            if (VDC_Count > 0)
            {
                try
                {
                    using (TextWriter datatotxt = new StreamWriter(Serial_COM_Info.folder_Directory + @"\" + "VDC" + @"\" + Date + "_" + Serial_COM_Info.COM_Port + "_VDC.txt", true))
                    {
                        for (int i = 0; i < VDC_Count; i++)
                        {
                            datatotxt.WriteLine(save_data_VDC.Take());
                        }
                    }
                }
                catch (Exception)
                {
                    insert_Log("Cannot save VDC measurements to text file.", 1);
                }
            }

            if (ADC_Count > 0)
            {
                try
                {
                    using (TextWriter datatotxt = new StreamWriter(Serial_COM_Info.folder_Directory + @"\" + "ADC" + @"\" + Date + "_" + Serial_COM_Info.COM_Port + "_ADC.txt", true))
                    {
                        for (int i = 0; i < ADC_Count; i++)
                        {
                            datatotxt.WriteLine(save_data_ADC.Take());
                        }
                    }
                }
                catch (Exception)
                {
                    insert_Log("Cannot save ADC measurements to text file.", 1);
                }
            }

            if (VAC_Count > 0)
            {
                try
                {
                    using (TextWriter datatotxt = new StreamWriter(Serial_COM_Info.folder_Directory + @"\" + "VAC" + @"\" + Date + "_" + Serial_COM_Info.COM_Port + "_VAC.txt", true))
                    {
                        for (int i = 0; i < VAC_Count; i++)
                        {
                            datatotxt.WriteLine(save_data_VAC.Take());
                        }
                    }
                }
                catch (Exception)
                {
                    insert_Log("Cannot save VAC measurements to text file.", 1);
                }
            }

            if (AAC_Count > 0)
            {
                try
                {
                    using (TextWriter datatotxt = new StreamWriter(Serial_COM_Info.folder_Directory + @"\" + "AAC" + @"\" + Date + "_" + Serial_COM_Info.COM_Port + "_AAC.txt", true))
                    {
                        for (int i = 0; i < AAC_Count; i++)
                        {
                            datatotxt.WriteLine(save_data_AAC.Take());
                        }
                    }
                }
                catch (Exception)
                {
                    insert_Log("Cannot save AAC measurements to text file.", 1);
                }
            }

            if (TwoOhm_Count > 0)
            {
                try
                {
                    using (TextWriter datatotxt = new StreamWriter(Serial_COM_Info.folder_Directory + @"\" + "2WireOhms" + @"\" + Date + "_" + Serial_COM_Info.COM_Port + "_2WireOhms.txt", true))
                    {
                        for (int i = 0; i < TwoOhm_Count; i++)
                        {
                            datatotxt.WriteLine(save_data_2Ohm.Take());
                        }
                    }
                }
                catch (Exception)
                {
                    insert_Log("Cannot save 2 Wire Ohms measurements to text file.", 1);
                }
            }

            if (FourOhm_Count > 0)
            {
                try
                {
                    using (TextWriter datatotxt = new StreamWriter(Serial_COM_Info.folder_Directory + @"\" + "4WireOhms" + @"\" + Date + "_" + Serial_COM_Info.COM_Port + "_4WireOhms.txt", true))
                    {
                        for (int i = 0; i < FourOhm_Count; i++)
                        {
                            datatotxt.WriteLine(save_data_4Ohm.Take());
                        }
                    }
                }
                catch (Exception)
                {
                    insert_Log("Cannot save 4 Wire Ohms measurements to text file.", 1);
                }
            }

            if (FREQ_Count > 0)
            {
                try
                {
                    using (TextWriter datatotxt = new StreamWriter(Serial_COM_Info.folder_Directory + @"\" + "FREQ" + @"\" + Date + "_" + Serial_COM_Info.COM_Port + "_FREQ.txt", true))
                    {
                        for (int i = 0; i < FREQ_Count; i++)
                        {
                            datatotxt.WriteLine(save_data_FREQ.Take());
                        }
                    }
                }
                catch (Exception)
                {
                    insert_Log("Cannot save FREQ measurements to text file.", 1);
                }
            }

            if (PER_Count > 0)
            {
                try
                {
                    using (TextWriter datatotxt = new StreamWriter(Serial_COM_Info.folder_Directory + @"\" + "PER" + @"\" + Date + "_" + Serial_COM_Info.COM_Port + "_PER.txt", true))
                    {
                        for (int i = 0; i < PER_Count; i++)
                        {
                            datatotxt.WriteLine(save_data_PER.Take());
                        }
                    }
                }
                catch (Exception)
                {
                    insert_Log("Cannot save PER measurements to text file.", 1);
                }
            }

            if (DIODE_Count > 0)
            {
                try
                {
                    using (TextWriter datatotxt = new StreamWriter(Serial_COM_Info.folder_Directory + @"\" + "DIODE" + @"\" + Date + "_" + Serial_COM_Info.COM_Port + "_DIODE.txt", true))
                    {
                        for (int i = 0; i < DIODE_Count; i++)
                        {
                            datatotxt.WriteLine(save_data_DIODE.Take());
                        }
                    }
                }
                catch (Exception)
                {
                    insert_Log("Cannot save VDCVAC measurements to text file.", 1);
                }
            }

            if (CONT_Count > 0)
            {
                try
                {
                    using (TextWriter datatotxt = new StreamWriter(Serial_COM_Info.folder_Directory + @"\" + "CONTINUITY" + @"\" + Date + "_" + Serial_COM_Info.COM_Port + "_CONTINUITY.txt", true))
                    {
                        for (int i = 0; i < CONT_Count; i++)
                        {
                            datatotxt.WriteLine(save_data_CONT.Take());
                        }
                    }
                }
                catch (Exception)
                {
                    insert_Log("Cannot save ADCAAC measurements to text file.", 1);
                }
            }

            saveMeasurements_Timer.Enabled = true;
            if (saveMeasurements == false)
            {
                while (save_data_VDC.TryTake(out _)) { }
                while (save_data_ADC.TryTake(out _)) { }
                while (save_data_VAC.TryTake(out _)) { }
                while (save_data_AAC.TryTake(out _)) { }
                while (save_data_2Ohm.TryTake(out _)) { }
                while (save_data_4Ohm.TryTake(out _)) { }
                while (save_data_FREQ.TryTake(out _)) { }
                while (save_data_PER.TryTake(out _)) { }
                while (save_data_DIODE.TryTake(out _)) { }
                while (save_data_CONT.TryTake(out _)) { }
                saveMeasurements_Timer.Enabled = false;
                saveMeasurements_Timer.Stop();
                insert_Log("Save Measurements Queues Cleared.", 0);
            }
        }

        private void SetupSpeechSythesis()
        {
            Voice.Volume = 100;
            Voice.SelectVoiceByHints(VoiceGender.Male);
            Voice.Rate = 1;
        }

        private void General_Timer()
        {
            runtime_Timer = new DispatcherTimer();
            runtime_Timer.Interval = TimeSpan.FromSeconds(1);
            runtime_Timer.Tick += runtime_Update;
            runtime_Timer.Start();
        }

        public void Serial_COM_Selected()
        {
            if (Serial_COM_Info.isConnected == true)
            {
                Connect.IsEnabled = false;
                unlockControls();
                Serial_Connect();
                this.Title = "HP 34401A " + Serial_COM_Info.COM_Port;
                DataSampling = true;
                saveOutputLog = true;
                saveMeasurements = true;
                Stop_Sampling.IsEnabled = true;
                DataTimer.Enabled = true;
                StartDateTime = DateTime.Now;
                Data_process();
                saveMeasurements_Timer.Enabled = true;
                Sampling_Only.IsEnabled = true;
                Local_Exit.IsEnabled = true;
                DataLogger.IsEnabled = true;
            }
        }

        private void runtime_Update(object sender, EventArgs e)
        {
            InvalidSamples_Total.Content = Invalid_Samples.ToString();
            Samples_Total.Content = Total_Samples.ToString();
            if (DataSampling == true)
            {
                Runtime_Timer.Content = GetTimeSpan();
            }
        }

        private void Continuous_Voice_Measurement()
        {
            Speech_Measurement_Interval = new System.Timers.Timer();
            Speech_Measurement_Interval.Interval = 60000; //Default is 1 minute;
            Speech_Measurement_Interval.AutoReset = false;
            Speech_Measurement_Interval.Enabled = false;
            Speech_Measurement_Interval.Elapsed += Check_Continuous_Voice_Measurement;
        }

        private void Check_Continuous_Voice_Measurement(Object source, ElapsedEventArgs e)
        {
            if (isSpeechActive == 1)
            {
                try
                {
                    if (isSpeechContinuous == 1)
                    {
                        if (Speech_Continuous_Voice_Value > 999999999)
                        {
                            Voice.Speak("Overload" + " " + MeasurementType_String());
                        }
                        else
                        {
                            Voice.Speak((decimal)Math.Round(Speech_Continuous_Voice_Value, Speech_Value_Precision) + " " + MeasurementType_String());
                        }
                        Speech_Measurement_Interval.Enabled = true;
                    }
                }
                catch (Exception)
                {
                    insert_Log("Speech Synthesizer Continuous Voice measurement feature failed.", 1);
                    insert_Log("Don't worry. Trying again.", 2);
                    Speech_Measurement_Interval.Enabled = true;
                }
            }
            else
            {
                Interlocked.Exchange(ref isSpeechContinuous, 0);
            }
        }

        private void Check_Speech_MIN_MAX_Timer()
        {
            Speech_MIN_Max = new System.Timers.Timer();
            Speech_MIN_Max.Interval = 1000;
            Speech_MIN_Max.AutoReset = false;
            Speech_MIN_Max.Enabled = false;
            Speech_MIN_Max.Elapsed += Check_Speech_MIN_MAX;
        }

        private void Check_Speech_MIN_MAX(Object source, ElapsedEventArgs e)
        {
            if (isSpeechActive == 1)
            {
                try
                {
                    if (isSpeechMAX == 1)
                    {
                        if (Speech_max_value <= max)
                        {
                            Voice.Speak("Warning, maximum value of " + (decimal)Math.Round(Speech_max_value, Speech_Value_Precision) + " " + MeasurementType_String() + " reached.");
                            if (max > 9999999999)
                            {
                                Voice.Speak("maximum value is " + "overload" + " " + MeasurementType_String());
                            }
                            else
                            {
                                Voice.Speak("maximum value is " + (decimal)Math.Round(max, Speech_Value_Precision) + " " + MeasurementType_String());
                            }
                        }
                    }
                    if (isSpeechMIN == 1)
                    {
                        if (Speech_min_value >= min)
                        {
                            Voice.Speak("Warning, minimum value of " + (decimal)Math.Round(Speech_min_value, Speech_Value_Precision) + " " + MeasurementType_String() + " reached.");
                            if (min < -9999999999)
                            {
                                Voice.Speak("minimum value is " + "overload" + " " + MeasurementType_String());
                            }
                            else
                            {
                                Voice.Speak("minimum value is " + (decimal)Math.Round(min, Speech_Value_Precision) + " " + MeasurementType_String());
                            }
                        }
                    }
                    Speech_MIN_Max.Enabled = true;
                }
                catch (Exception)
                {
                    insert_Log("Speech Synthesizer MIN and MAX feature failed.", 1);
                    insert_Log("Don't worry. Trying again.", 2);
                    Speech_MIN_Max.Enabled = true;
                }
            }
            if (isSpeechMAX == 0 & isSpeechMIN == 0)
            {
                Speech_MIN_Max.Enabled = false;
                Speech_MIN_Max.Stop();
            }
        }

        private void setContinuousVoiceMeasurement(double value)
        {
            if (isSpeechContinuous == 1)
            {
                Interlocked.Exchange(ref Speech_Continuous_Voice_Value, value);
            }
        }

        private string MeasurementType_String()
        {
            switch (Selected_Measurement_type)
            {
                case 0:
                    return "volts DC";
                case 1:
                    return "amps DC";
                case 2:
                    return "volts AC";
                case 3:
                    return "amps AC";
                case 4:
                    return "ohms";
                case 5:
                    return "ohms";
                case 6:
                    return "hertz";
                case 7:
                    return "seconds";
                case 8:
                    return "volts DC";
                case 9:
                    return "ohms";
                default:
                    return "value";
            }
        }

        private (string, string) MeasurementUnit_String()
        {
            switch (Selected_Measurement_type)
            {
                case 0:
                    return ("VDC", "VDC Voltage");
                case 1:
                    return ("ADC", "ADC Current");
                case 2:
                    return ("VAC", "VAC Voltage");
                case 3:
                    return ("AAC", "AAC Current");
                case 4:
                    return ("Ω", "Ω 2Wire Ohms");
                case 5:
                    return ("Ω", "Ω 4Wire Ohms");
                case 6:
                    return ("Hz", "Hz Frequency");
                case 7:
                    return ("s", "T Period");
                case 8:
                    return ("VDC", "VDC Voltage Diode");
                case 9:
                    return ("Ω", "Ω Ohms Continuity");
                default:
                    return ("Unk", "Unknown");
            }
        }

        private string GetTimeSpan()
        {
            TimeSpan span = (DateTime.Now - StartDateTime);
            return (String.Format("{0:00}:{1:00}:{2:00}", span.Hours, span.Minutes, span.Seconds));
        }

        private void unlockControls()
        {
            this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new ThreadStart(delegate
            {
                Measurements.IsEnabled = true;
                Range.IsEnabled = true;
                Meter_Config.IsEnabled = true;
                UpdateSpeed_Box.IsEnabled = true;
            }));
        }

        private void lockControls()
        {
            this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new ThreadStart(delegate
            {
                Measurements.IsEnabled = false;
                Range.IsEnabled = false;
                Meter_Config.IsEnabled = false;
                UpdateSpeed_Box.IsEnabled = false;
            }));
        }

        private void Speedup_Interval()
        {
            if (UpdateSpeed > 2000)
            {
                DataTimer.Interval = 0.01;
            }
        }

        private void Restore_Interval()
        {
            DataTimer.Interval = UpdateSpeed;
        }

        private void Serial_Connect()
        {
            HP34401A = new SerialPort(Serial_COM_Info.COM_Port, Serial_COM_Info.COM_BaudRate, (Parity)Serial_COM_Info.COM_Parity, Serial_COM_Info.COM_DataBits, (StopBits)Serial_COM_Info.COM_StopBits);
            HP34401A.WriteTimeout = Serial_COM_Info.COM_WriteTimeout;
            HP34401A.ReadTimeout = Serial_COM_Info.COM_ReadTimeout;
            HP34401A.RtsEnable = Serial_COM_Info.COM_RtsEnable;
            HP34401A.Handshake = (Handshake)Serial_COM_Info.COM_Handshake;
            HP34401A.Open();
        }

        private void Create_GetDataTimer()
        {
            DataTimer = new System.Timers.Timer();
            DataTimer.Interval = 1000;
            DataTimer.Elapsed += HP34401ACommunicateEvent;
            DataTimer.AutoReset = false;
        }

        private void Data_process()
        {
            Process_Data = new DispatcherTimer();
            Process_Data.Interval = TimeSpan.FromSeconds(0);
            Process_Data.Tick += DataProcessor;
            Process_Data.Start();
        }

        private void DataProcessor(object sender, EventArgs e)
        {
            while (measurements.Count > 0)
            {
                try
                {
                    string measurement = measurements.Take();
                    if (measurement == "+9.90000000E+37")
                    {
                        if (Measurement_Selected == 8 || Measurement_Selected == 9)
                        {
                            Measurement_Value.Content = "OPEN";
                            Measurement_Scale.Content = "";
                            Display_MIN_MAX_AVG(100000000000);
                            setContinuousVoiceMeasurement(100000000000);
                        }
                        else
                        {
                            Measurement_Value.Content = "OVLD";
                            Measurement_Scale.Content = "";
                            Display_MIN_MAX_AVG(100000000000);
                            setContinuousVoiceMeasurement(100000000000);
                        }
                    }
                    else if (measurement == "-9.90000000E+37")
                    {
                        Measurement_Value.Content = "OVLD";
                        Measurement_Scale.Content = "";
                        Display_MIN_MAX_AVG(-100000000000);
                        setContinuousVoiceMeasurement(-100000000000);
                    }
                    else
                    {
                        double value = double.Parse(measurement, System.Globalization.NumberStyles.Float);
                        DisplayData(measurement, value);
                        Display_MIN_MAX_AVG(value);
                        setContinuousVoiceMeasurement(value);
                    }
                }
                catch (Exception Ex)
                {
                    if (Show_Display_Error.IsChecked == true)
                    {
                        insert_Log(Ex.ToString(), 2);
                        insert_Log("Sample display process failed. Trying again.", 2);
                    }
                }
            }
            Process_Data.Stop();
        }

        private void DisplayData(string measurement, double value)
        {
            if (Partial_SI_Prefix == true)
            {
                Display_PSI_Double(measurement, value);
            }
            else if (Full_SI_Prefix == true)
            {
                Display_FSI_Double(measurement, value);
            }
        }

        private void Display_PSI_Double(string measurement, double value)
        {
            if (value == 0)
            {
                Measurement_Value.Content = (decimal)(value);
                Measurement_Scale.Content = "";
            }
            else if (value > -1 & value < 1)
            {
                Measurement_Value.Content = (decimal)(value * 1000);
                Measurement_Scale.Content = "m";
            }
            else if (value > 999999999)
            {
                Measurement_Value.Content = (decimal)(value / 1000000000);
                Measurement_Scale.Content = "G";
            }
            else if (value > 999999)
            {
                Measurement_Value.Content = (decimal)(value / 1000000);
                Measurement_Scale.Content = "M";
            }
            else if (value > 999)
            {
                Measurement_Value.Content = (decimal)(value / 1000);
                Measurement_Scale.Content = "k";
            }
            else
            {
                Measurement_Value.Content = (decimal)(value);
                Measurement_Scale.Content = "";
            }
        }

        private void Display_FSI_Double(string measurement, double value)
        {
            string unit = measurement.Substring(measurement.IndexOf("E") + 1);
            switch (unit)
            {
                case "-12":
                    Measurement_Scale.Content = "p"; //pico official
                    Measurement_Value.Content = double.Parse(measurement.Substring(0, measurement.IndexOf("E")));
                    break;
                case "-11":
                    Measurement_Value.Content = (decimal)(value * 1E9);
                    Measurement_Scale.Content = "n"; //nano
                    break;
                case "-10":
                    Measurement_Value.Content = (decimal)(value * 1E9);
                    Measurement_Scale.Content = "n"; //nano
                    break;
                case "-09":
                    Measurement_Scale.Content = "n"; //nano official
                    Measurement_Value.Content = double.Parse(measurement.Substring(0, measurement.IndexOf("E")));
                    break;
                case "-08":
                    Measurement_Value.Content = (decimal)(value * 1E6);
                    Measurement_Scale.Content = "μ"; //micro
                    break;
                case "-07":
                    Measurement_Value.Content = (decimal)(value * 1E6);
                    Measurement_Scale.Content = "μ"; //micro
                    break;
                case "-06":
                    Measurement_Scale.Content = "μ"; //micro official
                    Measurement_Value.Content = double.Parse(measurement.Substring(0, measurement.IndexOf("E")));
                    break;
                case "-05":
                    Measurement_Value.Content = (decimal)(value * 1E3);
                    Measurement_Scale.Content = "m"; //milli
                    break;
                case "-04":
                    Measurement_Value.Content = (decimal)(value * 1E3);
                    Measurement_Scale.Content = "m"; //milli
                    break;
                case "-03":
                    Measurement_Scale.Content = "m"; //milli official
                    Measurement_Value.Content = double.Parse(measurement.Substring(0, measurement.IndexOf("E")));
                    break;
                case "-02":
                    Measurement_Value.Content = (decimal)(value * 1E3);
                    Measurement_Scale.Content = "m"; //milli
                    break;
                case "-01":
                    Measurement_Value.Content = (decimal)(value * 1E3);
                    Measurement_Scale.Content = "m"; //milli
                    break;
                case "+01":
                    Measurement_Value.Content = (decimal)(value * 1E-3);
                    Measurement_Scale.Content = "K"; //kilo
                    break;
                case "+02":
                    Measurement_Value.Content = (decimal)(value * 1E-3);
                    Measurement_Scale.Content = "K"; //kilo
                    break;
                case "+03":
                    Measurement_Scale.Content = "K"; //kilo official
                    Measurement_Value.Content = double.Parse(measurement.Substring(0, measurement.IndexOf("E")));
                    break;
                case "+04":
                    Measurement_Value.Content = (decimal)(value * 1E-3);
                    Measurement_Scale.Content = "K"; //kilo
                    break;
                case "+05":
                    Measurement_Value.Content = (decimal)(value * 1E-3);
                    Measurement_Scale.Content = "K"; //kilo
                    break;
                case "+06":
                    Measurement_Scale.Content = "M"; //Mega official
                    Measurement_Value.Content = double.Parse(measurement.Substring(0, measurement.IndexOf("E")));
                    break;
                case "+07":
                    Measurement_Value.Content = (decimal)(value * 1E-6);
                    Measurement_Scale.Content = "M"; //Mega
                    break;
                case "+08":
                    Measurement_Value.Content = (decimal)(value * 1E-6);
                    Measurement_Scale.Content = "M"; //Mega
                    break;
                case "+09":
                    Measurement_Scale.Content = "G"; //Giga official
                    Measurement_Value.Content = double.Parse(measurement.Substring(0, measurement.IndexOf("E")));
                    break;
                case "+10":
                    Measurement_Value.Content = (decimal)(value * 1E-9);
                    Measurement_Scale.Content = "G"; //Giga
                    break;
                case "+11":
                    Measurement_Value.Content = (decimal)(value * 1E-9);
                    Measurement_Scale.Content = "G"; //Giga
                    break;
                case "+12":
                    Measurement_Value.Content = double.Parse(measurement.Substring(0, measurement.IndexOf("E")));
                    Measurement_Scale.Content = "T"; //Tera official
                    break;
                default:
                    Measurement_Scale.Content = "";
                    Measurement_Value.Content = double.Parse(measurement.Substring(0, measurement.IndexOf("E")));
                    break;
            }
        }

        private void Display_MIN_MAX_AVG(double measurement)
        {
            if (resetMinMaxAvg == 1)
            {
                min = measurement;
                max = measurement;
                avg = 0;
                avg_count = 0;
                insert_Log("Reset MIN, MAX, AVG values.", 0);
                updateMIN(measurement);
                updateMAX(measurement);
                Interlocked.Exchange(ref resetMinMaxAvg, 0);
            }
            if (measurement < min)
            {
                updateMIN(measurement);
            }
            if (measurement > max)
            {
                updateMAX(measurement);
            }
            if (AVG_Calculate == 1)
            {
                updateAVG(measurement);
            }
        }

        private void Reset_Click_MIN_MAX_AVG(object sender, MouseButtonEventArgs e)
        {
            Interlocked.Exchange(ref resetMinMaxAvg, 1);
            insert_Log("Reset MIN, MAX, AVG command has been send.", 4);
        }

        private void updateAVG(double measurement)
        {
            avg_count += 1;
            avg = avg + (measurement - avg) / Math.Min(avg_count, avg_factor);
            if (avg == 0)
            {
                AVG_Value.Content = ((decimal)Math.Round((avg), avg_resolution)).ToString();
                AVG_Scale.Content = "";
            }
            else if (avg < 1E-6 & avg > -1E-6)
            {
                AVG_Value.Content = ((decimal)Math.Round((avg * 1E9), avg_resolution)).ToString();
                AVG_Scale.Content = "n";
            }
            else if (avg < 1 & avg > -1)
            {
                AVG_Value.Content = ((decimal)Math.Round((avg * 1000), avg_resolution)).ToString();
                AVG_Scale.Content = "m";
            }
            else if (avg < -99999999999 || avg > 99999999999)
            {
                AVG_Value.Content = "OVLD";
                AVG_Scale.Content = "";
            }
            else if (avg < -999999999 || avg > 999999999)
            {
                AVG_Value.Content = ((decimal)Math.Round((avg / 1000000000), avg_resolution)).ToString();
                AVG_Scale.Content = "G";
            }
            else if (avg < -999999 || avg > 999999)
            {
                AVG_Value.Content = ((decimal)Math.Round((avg / 1000000), avg_resolution)).ToString();
                AVG_Scale.Content = "M";
            }
            else if (avg < -999 || avg > 999)
            {
                AVG_Value.Content = ((decimal)Math.Round((avg / 1000), avg_resolution)).ToString();
                AVG_Scale.Content = "K";
            }
            else
            {
                AVG_Value.Content = ((decimal)Math.Round((avg), avg_resolution)).ToString();
                AVG_Scale.Content = "";
            }
        }

        private void updateMIN(double measurement)
        {
            min = measurement;
            if (min == 0)
            {
                MIN_Value.Content = ((decimal)(min)).ToString();
                MIN_Scale.Content = "";
            }
            else if (min < 1E-6 & min > -1E-6)
            {
                MIN_Value.Content = ((decimal)(min * 1E9)).ToString();
                MIN_Scale.Content = "n";
            }
            else if (min < 1 & min > -1)
            {
                MIN_Value.Content = ((decimal)(min * 1000)).ToString();
                MIN_Scale.Content = "m";
            }
            else if (min < -99999999999 || min > 99999999999)
            {
                MIN_Value.Content = "OVLD";
                MIN_Scale.Content = "";
            }
            else if (min < -999999999 || min > 999999999)
            {
                MIN_Value.Content = ((decimal)(min / 1000000000)).ToString();
                MIN_Scale.Content = "G";
            }
            else if (min < -999999 || min > 999999)
            {
                MIN_Value.Content = ((decimal)(min / 1000000)).ToString();
                MIN_Scale.Content = "M";
            }
            else if (min < -999 || min > 999)
            {
                MIN_Value.Content = ((decimal)(min / 1000)).ToString();
                MIN_Scale.Content = "K";
            }
            else
            {
                MIN_Value.Content = ((decimal)min).ToString();
                MIN_Scale.Content = "";
            }
        }

        private void updateMAX(double measurement)
        {
            max = measurement;
            if (max == 0)
            {
                MAX_Value.Content = ((decimal)(max)).ToString();
                MAX_Scale.Content = "";
            }
            else if (max < 1E-6 & max > -1E-6)
            {
                MAX_Value.Content = ((decimal)(max * 1E9)).ToString();
                MAX_Scale.Content = "n";
            }
            else if (max < 1 & max > -1)
            {
                MAX_Value.Content = ((decimal)(max * 1000)).ToString();
                MAX_Scale.Content = "m";
            }
            else if (max < -99999999999 || max > 99999999999)
            {
                MAX_Value.Content = "OVLD";
                MAX_Scale.Content = "";
            }
            else if (max < -999999999 || max > 999999999)
            {
                MAX_Value.Content = ((decimal)(max * 1000000000)).ToString();
                MAX_Scale.Content = "G";
            }
            else if (max < -999999 || max > 999999)
            {
                MAX_Value.Content = ((decimal)(max / 1000000)).ToString();
                MAX_Scale.Content = "M";
            }
            else if (max < -999 || max > 999)
            {
                MAX_Value.Content = ((decimal)(max / 1000)).ToString();
                MAX_Scale.Content = "K";
            }
            else
            {
                MAX_Value.Content = ((decimal)max).ToString();
                MAX_Scale.Content = "";
            }
        }

        private void HP34401ACommunicateEvent(Object source, ElapsedEventArgs e)
        {
            try
            {
                if (isUserSendCommand == true)
                {
                    Serial_WriteQueue();
                    Measurement_Type_Select();
                    unlockControls();
                    isUserSendCommand = false;
                    if (UpdateSpeed > 1000)
                    {
                        Restore_Interval();
                    }
                }

                if (DataSampling == true)
                {
                    do
                    {
                        Read_Measurement();
                        Process_Data.Start();
                    } while (isSamplingOnly == true & DataSampling == true);
                }
                if (isUpdateSpeed_Changed == true)
                {
                    isUpdateSpeed_Changed = false;
                    insert_Log("Update Speed has been set to " + (UpdateSpeed / 1000) + " seconds.", 0);
                    DataTimer.Interval = UpdateSpeed;
                }
                DataTimer.Enabled = true;
            }
            catch (Exception Ex)
            {
                this.Dispatcher.Invoke(DispatcherPriority.ContextIdle, new ThreadStart(delegate
                {
                    if (Show_COM_Error.IsChecked == true)
                    {
                        insert_Log(Ex.Message, 2);
                        insert_Log("Could not get a measurement reading.", 2);
                        insert_Log("Don't worry. Trying again.", 2);
                        insert_Log("Slow the Update Speed if warning persists.", 2);
                    }
                }));
                if (isUpdateSpeed_Changed == true)
                {
                    isUpdateSpeed_Changed = false;
                    insert_Log("Update Speed has been set to " + (UpdateSpeed / 1000) + " seconds.", 0);
                    DataTimer.Interval = UpdateSpeed;
                }
                DataTimer.Enabled = true;
            }
        }

        private void Read_Measurement()
        {
            HP34401A.WriteLine("READ?");
            HP34401A.WriteLine("++read");
            string data = HP34401A.ReadLine().Trim();
            int Length = data.Length;
            if (Length == 15)
            {
                measurements.Add(data);
                Total_Samples++;
                if (saveMeasurements == true || save_to_Table == true || save_to_Graph == true || Save_to_N_Graph == true)
                {
                    Process_Measurement_Data(data);
                }
            }
            else
            {
                Invalid_Samples++;
                insert_Log(data, 1);
            }

        }

        private void Process_Measurement_Data(string data)
        {
            string Date = DateTime.Now.ToString("yyyy-MM-dd h:mm:ss tt");
            if (saveMeasurements == true)
            {
                switch (Selected_Measurement_type)
                {
                    case 0:
                        save_data_VDC.Add(Date + "," + data);
                        break;
                    case 1:
                        save_data_ADC.Add(Date + "," + data);
                        break;
                    case 2:
                        save_data_VAC.Add(Date + "," + data);
                        break;
                    case 3:
                        save_data_AAC.Add(Date + "," + data);
                        break;
                    case 4:
                        save_data_2Ohm.Add(Date + "," + data);
                        break;
                    case 5:
                        save_data_4Ohm.Add(Date + "," + data);
                        break;
                    case 6:
                        save_data_FREQ.Add(Date + "," + data);
                        break;
                    case 7:
                        save_data_PER.Add(Date + "," + data);
                        break;
                    case 8:
                        save_data_DIODE.Add(Date + "," + data);
                        break;
                    case 9:
                        save_data_CONT.Add(Date + "," + data);
                        break;
                    default:
                        insert_Log("Data was not saved. Something went wrong.", 0);
                        break;
                }
            }

            if (save_to_Table == true)
            {
                try
                {
                    HP34401A_Table.Table_Data_Queue.Add(Date + "," + data + "," + Current_Measurement_Unit);
                }
                catch (Exception)
                {
                    insert_Log("Could not add data to Table Window.", 2);
                    insert_Log("This could happen if the table window was opened or closed recently.", 2);
                }
            }

            if (save_to_Graph == true)
            {
                try
                {
                    HP34401A_Graph_Window.Data_Queue.Add(Date + "," + data);
                }
                catch (Exception)
                {
                    insert_Log("Could not add data to Graph Window.", 2);
                    insert_Log("This could happen if the graph window was opened or closed recently.", 2);
                }
            }

            if (Save_to_N_Graph == true)
            {
                try
                {
                    HP34401A_N_Graph_Window.Data_Queue.Add(Date + "," + data);
                }
                catch (Exception)
                {
                    insert_Log("Could not add data to N Sample Graph Window.", 2);
                    insert_Log("This could happen if the N Sample Graph Window was opened or closed recently.", 2);
                }
            }
        }

        private void Serial_WriteQueue()
        {
            while (SerialWriteQueue.Count != 0)
            {
                string WriteCommand = SerialWriteQueue.Take();
                Serial_Queue_Command_Process(WriteCommand);
            }
        }

        private void Serial_Queue_Command_Process(string Command)
        {
            switch (Command)
            {
                case "CONF:VOLT:DC DEF,DEF":
                case "CONF:CURR:DC DEF,DEF":
                case "CONF:VOLT:AC DEF,DEF":
                case "CONF:CURR:AC DEF,DEF":
                case "CONF:RES DEF,DEF":
                case "CONF:FRES DEF,DEF":
                case "CONF:FREQ DEF,DEF":
                case "CONF:PER DEF,DEF":
                case "CONF:DIOD":
                case "CONF:CONT":
                    Interlocked.Exchange(ref resetMinMaxAvg, 1);
                    HP34401A.WriteLine(Command);
                    break;
                case "TRIGger:DELay?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string Trig_Delay_Query = HP34401A.ReadLine().Trim();
                    insert_Log("Trigger Delay Value: " + Trig_Delay_Query, 0);
                    break;
                case "TRIGger:DELay:AUTO?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string Trig_Delay_Auto_Query = HP34401A.ReadLine().Trim();
                    bool isValid_Trig_Delay_Auto_Query = double.TryParse(Trig_Delay_Auto_Query, out double Trig_Delay_Auto_Query_Value);
                    if (isValid_Trig_Delay_Auto_Query == true)
                    {
                        if (Trig_Delay_Auto_Query_Value == 1)
                        {
                            insert_Log("Trigger Auto Delay is ON", 0);
                        }
                        else if (Trig_Delay_Auto_Query_Value == 0)
                        {
                            insert_Log("Trigger Auto Delay is OFF", 0);
                        }
                        else
                        {
                            insert_Log("Trigger Auto Delay: " + Trig_Delay_Auto_Query, 0);
                        }
                    }
                    else
                    {
                        insert_Log("Trigger Auto Delay: " + Trig_Delay_Auto_Query, 0);
                    }
                    break;
                case "CALibration:COUNt?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string CAL_Count_Query = HP34401A.ReadLine().Trim();
                    insert_Log("Calibration Count: " + CAL_Count_Query, 0);
                    break;
                case "CALibration:SECure:STATe?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string CAL_Secure_State_Query = HP34401A.ReadLine().Trim();
                    insert_Log("Calibration Secure State: " + CAL_Secure_State_Query, 0);
                    break;
                case "CALibration:STRing?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string CAL_Message_Query = HP34401A.ReadLine().Trim();
                    insert_Log("Calibration Stored Message: " + CAL_Message_Query, 0);
                    break;
                case "CALibration:VALue?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string CAL_Value_Query = HP34401A.ReadLine().Trim();
                    insert_Log("Calibration Value: " + CAL_Value_Query, 0);
                    break;
                case "SYSTem:ERRor?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string Error_Query = HP34401A.ReadLine().Trim();
                    insert_Log("Error: " + Error_Query, 0);
                    break;
                case "ROUTe:TERMinals?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string Terminal_Query = HP34401A.ReadLine().Trim();
                    insert_Log("Input Terminal: " + Terminal_Query, 0);
                    break;
                case "*IDN?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string IDN_Query = HP34401A.ReadLine().Trim();
                    insert_Log("Device ID: " + IDN_Query, 0);
                    break;
                case "*STB?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string STB_Query = HP34401A.ReadLine().Trim();
                    insert_Log("STB: " + STB_Query, 0);
                    break;
                case "SYSTem:VERSion?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string SCPI_Version_Query = HP34401A.ReadLine().Trim();
                    insert_Log("SCPI Version: " + SCPI_Version_Query, 0);
                    break;
                case "DISPlay?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string Display_Query = HP34401A.ReadLine().Trim();
                    bool isValid_Display_Query = double.TryParse(Display_Query, out double Display_Query_Value);
                    if (isValid_Display_Query == true)
                    {
                        if (Display_Query_Value == 1)
                        {
                            insert_Log("VFD Display is ON", 0);
                        }
                        else if (Display_Query_Value == 0)
                        {
                            insert_Log("VFD Display is OFF", 0);
                        }
                        else
                        {
                            insert_Log("VFD Display: " + Display_Query, 0);
                        }
                    }
                    else
                    {
                        insert_Log("VFD Display: " + Display_Query, 0);
                    }
                    break;
                case "INPut:IMPedance:AUTO?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string DC_IMP_Query = HP34401A.ReadLine().Trim();
                    bool isValid_DC_IMP_Query = double.TryParse(DC_IMP_Query, out double DC_IMP_Query_Value);
                    if (isValid_DC_IMP_Query == true)
                    {
                        if (DC_IMP_Query_Value == 1)
                        {
                            insert_Log("Auto Input Impedance is ON.", 0);
                            insert_Log("Input Impedance is >10G Ω for DC 100mV 1V 10V", 0);
                        }
                        else if (DC_IMP_Query_Value == 0)
                        {
                            insert_Log("Auto Input Impedance is OFF", 0);
                            insert_Log("Input Impedance is 10M Ω for all DC Ranges.", 0);
                        }
                        else
                        {
                            insert_Log("DC Input Impedance: " + DC_IMP_Query, 0);
                        }
                    }
                    else
                    {
                        insert_Log("DC Input Impedance: " + DC_IMP_Query, 0);
                    }
                    break;
                case "DETector:BANDwidth?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string DET_BAND_Query = HP34401A.ReadLine().Trim();
                    bool isValid_DET_BAND_Query = double.TryParse(DET_BAND_Query, out double DET_BAND_Query_Value);
                    if (isValid_DET_BAND_Query == true)
                    {
                        insert_Log("ACV ACI Bandwidth: " + (decimal)DET_BAND_Query_Value + "Hz", 0);
                    }
                    else
                    {
                        insert_Log("ACV ACI Bandwidth: " + DET_BAND_Query, 0);
                    }
                    break;
                case "PERiod:APERture?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string PER_APER_Query = HP34401A.ReadLine().Trim();
                    bool isValid_PER_APER_Query = double.TryParse(PER_APER_Query, out double PER_APER_Query_Value);
                    if (isValid_PER_APER_Query == true)
                    {
                        insert_Log("Period Aperture: " + (decimal)PER_APER_Query_Value, 0);
                    }
                    else
                    {
                        insert_Log("Period Aperture: " + PER_APER_Query, 0);
                    }
                    break;
                case "FREQuency:APERture?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string FREQ_APER_Query = HP34401A.ReadLine().Trim();
                    bool isValid_FREQ_APER_Query = double.TryParse(FREQ_APER_Query, out double FREQ_APER_Query_Value);
                    if (isValid_FREQ_APER_Query == true)
                    {
                        insert_Log("Frequency Aperture: " + (decimal)FREQ_APER_Query_Value, 0);
                    }
                    else
                    {
                        insert_Log("Frequency Aperture: " + FREQ_APER_Query, 0);
                    }
                    break;
                case "ZERO:AUTO?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string AutoZero_Query = HP34401A.ReadLine().Trim();
                    bool isValid_AutoZero_Query = double.TryParse(AutoZero_Query, out double AutoZero_Query_Value);
                    if (isValid_AutoZero_Query == true)
                    {
                        if (AutoZero_Query_Value == 1)
                        {
                            insert_Log("Auto Zero is ON", 0);
                        }
                        else if (AutoZero_Query_Value == 0)
                        {
                            insert_Log("Auto Zero is OFF", 0);
                        }
                        else
                        {
                            insert_Log("Auto Zero: " + AutoZero_Query, 0);
                        }
                    }
                    else
                    {
                        insert_Log("Auto Zero: " + AutoZero_Query, 0);
                    }
                    break;
                case "FUNCtion?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string Function_Query = HP34401A.ReadLine().Trim();
                    switch (Function_Query)
                    {
                        case "\"VOLT\"":
                            insert_Log("DCV measurement is selected.", 0);
                            break;
                        case "\"CURR\"":
                            insert_Log("DCI measurement is selected.", 0);
                            break;
                        case "\"VOLT:AC\"":
                            insert_Log("ACV measurement is selected.", 0);
                            break;
                        case "\"CURR:AC\"":
                            insert_Log("ACI measurement is selected.", 0);
                            break;
                        case "\"RES\"":
                            insert_Log("2 Wire Ω measurement is selected.", 0);
                            break;
                        case "\"FRES\"":
                            insert_Log("4 Wire Ω measurement is selected.", 0);
                            break;
                        case "\"FREQ\"":
                            insert_Log("Frequency measurement is selected.", 0);
                            break;
                        case "\"PER\"":
                            insert_Log("Period measurement is selected.", 0);
                            break;
                        case "\"DIOD\"":
                            insert_Log("Diode measurement is selected.", 0);
                            break;
                        case "\"CONT\"":
                            insert_Log("Continuity measurement is selected.", 0);
                            break;
                        default:
                            insert_Log(Function_Query + " measurement is selected.", 0);
                            break;
                    }
                    break;
                case "CONFigure?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string Function_Config_Query = HP34401A.ReadLine().Trim();
                    insert_Log(Function_Config_Query, 0);
                    break;
                case "VOLTage:DC:RANGe?":
                case "CURRent:DC:RANGe?":
                case "VOLTage:AC:RANGe?":
                case "CURRent:AC:RANGe?":
                case "RESistance:RANGe?":
                case "FRESistance:RANGe?":
                case "FREQuency:VOLTage:RANGe?":
                case "PERiod:VOLTage:RANGe?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string Range_Query = HP34401A.ReadLine().Trim();
                    insert_Log(Command.ToUpper() + " " + Range_Query, 0);
                    break;
                case "VOLTage:DC:RANGe:AUTO?":
                case "CURRent:DC:RANGe:AUTO?":
                case "VOLTage:AC:RANGe:AUTO?":
                case "CURRent:AC:RANGe:AUTO?":
                case "RESistance:RANGe:AUTO?":
                case "FRESistance:RANGe:AUTO?":
                case "FREQuency:VOLTage:RANGe:AUTO?":
                case "PERiod:VOLTage:RANGe:AUTO?":
                    HP34401A.WriteLine(Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string Auto_Range_Query = HP34401A.ReadLine().Trim();
                    bool isValid_Auto_Range_Query = double.TryParse(Auto_Range_Query, out double Auto_Range_Query_Value);
                    if (isValid_Auto_Range_Query == true)
                    {
                        if (Auto_Range_Query_Value == 1)
                        {
                            insert_Log(Command.ToUpper() + " is ON", 0);
                        }
                        else if (Auto_Range_Query_Value == 0)
                        {
                            insert_Log(Command.ToUpper() + " is OFF", 0);
                        }
                        else
                        {
                            insert_Log(Command.ToUpper() + " " + Auto_Range_Query, 0);
                        }
                    }
                    else
                    {
                        insert_Log(Command.ToUpper() + " " + Auto_Range_Query, 0);
                    }
                    break;
                case "RESolution MIN":
                case "RESolution MAX":
                    string Set_Resolution_Command = SCPI_Measurement_Selected() + Command;
                    HP34401A.WriteLine(Set_Resolution_Command);
                    insert_Log(Set_Resolution_Command, 5);
                    break;
                case "RES Query":
                    string Resolution_Command = SCPI_Measurement_Selected() + "RESolution?";
                    HP34401A.WriteLine(Resolution_Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string Resolution_Query = HP34401A.ReadLine().Trim();
                    bool is_Res_valid = double.TryParse(Resolution_Query, out double Res_value);
                    if (is_Res_valid == true)
                    {
                        insert_Log(Resolution_Command + "  " + (decimal)Res_value, 0);
                    }
                    else
                    {
                        insert_Log(Resolution_Command + "  " + Resolution_Query, 0);
                    }
                    break;
                case "TRIG Query":
                    HP34401A.WriteLine("TRIGger:SOURce?");
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string Trig_Query = HP34401A.ReadLine().Trim();
                    insert_Log("Trigger is currently set to " + Trig_Query, 0);
                    break;
                case "NPLC Query":
                    string NPLC_Command = SCPI_Measurement_Selected() + "NPLCycles?";
                    HP34401A.WriteLine(NPLC_Command);
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string NPLC_Query = HP34401A.ReadLine().Trim();
                    bool is_NPLC_valid = double.TryParse(NPLC_Query, out double NPLC_value);
                    if (is_NPLC_valid == true)
                    {
                        insert_Log(NPLC_Command + "  " + (decimal)NPLC_value, 0);
                    }
                    else
                    {
                        insert_Log(NPLC_Command + "  " + NPLC_Query, 0);
                    }
                    break;
                case "NULL ON":
                    HP34401A.WriteLine("CALCulate:FUNCtion NULL");
                    Thread.Sleep(100);
                    HP34401A.WriteLine("CALCulate:STATe ON");
                    Thread.Sleep(100);
                    break;
                case "NULL OFF":
                    HP34401A.WriteLine("CALCulate:STATe OFF");
                    Thread.Sleep(100);
                    break;
                case "NULL?":
                    HP34401A.WriteLine("CALCulate:FUNCtion?");
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string Math_Function_Query = HP34401A.ReadLine().Trim();
                    Thread.Sleep(100);
                    HP34401A.WriteLine("CALCulate:STATe?");
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string Math_Function_State_Query = HP34401A.ReadLine().Trim();
                    bool is_Math_Function_State_Value_valid = double.TryParse(Math_Function_State_Query, out double Null_State_value);
                    if (is_Math_Function_State_Value_valid == true)
                    {
                        if (Null_State_value == 0)
                        {
                            insert_Log(Math_Function_Query + " function is not active.", 0);
                        }
                        else if (Null_State_value == 1)
                        {
                            insert_Log(Math_Function_Query + " function is active.", 0);
                        }
                        else
                        {
                            insert_Log("Math Function: " + Math_Function_Query + " State: " + Math_Function_State_Query, 0);
                        }
                    }
                    else
                    {
                        insert_Log("Math Function: " + Math_Function_Query + " State: " + Math_Function_State_Query, 0);
                    }
                    break;
                case "NULL_SET":
                    HP34401A.WriteLine("CALCulate:NULL:OFFSet " + (decimal)Null_Value);
                    Thread.Sleep(100);
                    insert_Log("Null Value has been set to " + (decimal)Null_Value, 0);
                    break;
                case "NULL_SET_QUERY":
                    HP34401A.WriteLine("CALCulate:NULL:OFFSet?");
                    Thread.Sleep(100);
                    HP34401A.WriteLine("++read");
                    string Null_Set_Value_Query = HP34401A.ReadLine().Trim();
                    bool is_Null_Set_Value_valid = double.TryParse(Null_Set_Value_Query, out double Null_set_value);
                    if (is_Null_Set_Value_valid == true)
                    {
                        insert_Log("Query Null Set Value: " + (decimal)Null_set_value, 0);
                    }
                    else
                    {
                        insert_Log("Query Null Set Value: " + Null_Set_Value_Query, 0);
                    }
                    break;
                case "LOCAL_EXIT":
                    HP34401A.WriteLine("++ren 0");
                    Thread.Sleep(200);
                    Application.Current.Dispatcher.Invoke(() => { Application.Current.Shutdown(); }, DispatcherPriority.Send);
                    break;
                default:
                    HP34401A.WriteLine(Command);
                    break;
            }
        }

        private string SCPI_Measurement_Selected()
        {
            switch (Measurement_Selected)
            {
                case 0: //DCV
                    return "VOLTage:DC:";
                case 1: //DCI
                    return "CURRent:DC:";
                case 2: //ACV
                    return "VOLTage:AC:";
                case 3: //ACI
                    return "CURRent:AC:";
                case 4: //2 OHMS
                    return "RESistance:";
                case 5: //4 OHMS
                    return "FRESistance:";
                case 6: //FREQ
                    return "FREQuency:";
                case 7: //PER
                    return "PERiod:";
                default:
                    return "";
            }
        }

        private void Measurement_Type_Select()
        {
            if (Measurement_Selected == 0)
            {
                this.Dispatcher.Invoke(DispatcherPriority.Background, new ThreadStart(delegate
                {
                    Measurement_Type.Content = "VDC";
                    Measurement_Scale.Content = "";
                    Measurement_Value.Content = "";
                    MIN_Type.Content = "VDC";
                    MAX_Type.Content = "VDC";
                    AVG_Type.Content = "VDC";
                    Current_Measurement_Unit = "VDC";
                }));
            }
            else if (Measurement_Selected == 1)
            {
                this.Dispatcher.Invoke(DispatcherPriority.Background, new ThreadStart(delegate
                {
                    Measurement_Type.Content = "ADC";
                    Measurement_Scale.Content = "";
                    Measurement_Value.Content = "";
                    MIN_Type.Content = "ADC";
                    MAX_Type.Content = "ADC";
                    AVG_Type.Content = "ADC";
                    Current_Measurement_Unit = "ADC";
                }));
            }
            else if (Measurement_Selected == 2)
            {
                this.Dispatcher.Invoke(DispatcherPriority.Background, new ThreadStart(delegate
                {
                    Measurement_Type.Content = "VAC";
                    Measurement_Scale.Content = "";
                    Measurement_Value.Content = "";
                    MIN_Type.Content = "VAC";
                    MAX_Type.Content = "VAC";
                    AVG_Type.Content = "VAC";
                    Current_Measurement_Unit = "VAC";
                }));
            }
            else if (Measurement_Selected == 3)
            {
                this.Dispatcher.Invoke(DispatcherPriority.Background, new ThreadStart(delegate
                {
                    Measurement_Type.Content = "AAC";
                    Measurement_Scale.Content = "";
                    Measurement_Value.Content = "";
                    MIN_Type.Content = "ADC";
                    MAX_Type.Content = "ADC";
                    AVG_Type.Content = "ADC";
                    Current_Measurement_Unit = "ADC";
                }));
            }
            else if (Measurement_Selected == 4)
            {
                this.Dispatcher.Invoke(DispatcherPriority.Background, new ThreadStart(delegate
                {
                    Measurement_Type.Content = "Ω";
                    Measurement_Scale.Content = "";
                    Measurement_Value.Content = "";
                    MIN_Type.Content = "Ω";
                    MAX_Type.Content = "Ω";
                    AVG_Type.Content = "Ω";
                    Current_Measurement_Unit = "Ω";
                }));
            }
            else if (Measurement_Selected == 5)
            {
                this.Dispatcher.Invoke(DispatcherPriority.Background, new ThreadStart(delegate
                {
                    Measurement_Type.Content = "Ω";
                    Measurement_Scale.Content = "";
                    Measurement_Value.Content = "";
                    MIN_Type.Content = "Ω";
                    MAX_Type.Content = "Ω";
                    AVG_Type.Content = "Ω";
                    Current_Measurement_Unit = "Ω";
                }));
            }
            else if (Measurement_Selected == 6)
            {
                this.Dispatcher.Invoke(DispatcherPriority.Background, new ThreadStart(delegate
                {
                    Measurement_Type.Content = "Hz";
                    Measurement_Scale.Content = "";
                    Measurement_Value.Content = "";
                    MIN_Type.Content = "Hz";
                    MAX_Type.Content = "Hz";
                    AVG_Type.Content = "Hz";
                    Current_Measurement_Unit = "Hz";
                }));
            }
            else if (Measurement_Selected == 7)
            {
                this.Dispatcher.Invoke(DispatcherPriority.Background, new ThreadStart(delegate
                {
                    Measurement_Type.Content = "SEC";
                    Measurement_Scale.Content = "";
                    Measurement_Value.Content = "";
                    MIN_Type.Content = "SEC";
                    MAX_Type.Content = "SEC";
                    AVG_Type.Content = "SEC";
                    Current_Measurement_Unit = "SEC";
                }));
            }
            else if (Measurement_Selected == 8)
            {
                this.Dispatcher.Invoke(DispatcherPriority.Background, new ThreadStart(delegate
                {
                    Measurement_Type.Content = "VDC";
                    Measurement_Scale.Content = "";
                    Measurement_Value.Content = "";
                    MIN_Type.Content = "VDC";
                    MAX_Type.Content = "VDC";
                    AVG_Type.Content = "VDC";
                    Current_Measurement_Unit = "VDC";
                }));
            }
            else if (Measurement_Selected == 9)
            {
                this.Dispatcher.Invoke(DispatcherPriority.Background, new ThreadStart(delegate
                {
                    Measurement_Type.Content = "Ω";
                    Measurement_Scale.Content = "";
                    Measurement_Value.Content = "";
                    MIN_Type.Content = "Ω";
                    MAX_Type.Content = "Ω";
                    AVG_Type.Content = "Ω";
                    Current_Measurement_Unit = "Ω";
                }));
            }
        }

        //Check if user input is a number and if it is then converts it from string to double.
        private (bool, double) isNumber(string Number)
        {
            bool isNum = double.TryParse(Number, out double number);
            return (isNum, number);
        }

        //inserts message to the output log
        private void insert_Log(string Message, int Code)
        {
            string date = DateTime.Now.ToString("yyyy-MM-dd h:mm:ss tt");
            SolidColorBrush Color;
            this.Dispatcher.Invoke(DispatcherPriority.ContextIdle, new ThreadStart(delegate
            {
                if (Output_Log.Inlines.Count >= Auto_Clear_Output_Log_Count)
                {
                    Output_Log.Text = String.Empty;
                    Output_Log.Inlines.Clear();
                    Output_Log.Inlines.Add(new Run("[" + date + "]" + " " + "Output Log has been auto cleared. \n") { Foreground = Brushes.Green });
                }
            }));
            string Status = "";
            switch (Code)
            {
                case 0:
                    Status = "[Success]";
                    Color = Brushes.Green;
                    break;
                case 1:
                    Status = "[Error]";
                    Color = Brushes.Red;
                    break;
                case 2:
                    Status = "[Warning]";
                    Color = Brushes.Orange;
                    break;
                case 3:
                    Status = "";
                    Color = Brushes.Blue;
                    break;
                case 4:
                    Status = "";
                    Color = Brushes.Black;
                    break;
                case 5:
                    Status = "";
                    Color = Brushes.BlueViolet;
                    break;
                default:
                    Status = "Unknown";
                    Color = Brushes.Black;
                    break;
            }
            Application.Current.Dispatcher.Invoke(DispatcherPriority.Background, new ThreadStart(delegate
            {
                Output_Log.Inlines.Add(new Run("[" + date + "]" + " " + Status + " " + Message + "\n") { Foreground = Color });
                if (AutoScroll.IsChecked == true)
                {
                    Output_Log_Scroll.ScrollToBottom();
                }
            }));
            //Saves output log to a text file
            if (saveOutputLog == true)
            {
                writeToFile("[" + date + "]" + " " + Status + " " + Message, Serial_COM_Info.folder_Directory, Serial_COM_Info.COM_Port + "_" + "Output Log.txt", true);
            }
        }

        //Writes data to a file
        private void writeToFile(string data, string filePath, string fileName, bool append)
        {
            try
            {
                using (TextWriter datatotxt = new StreamWriter(filePath + @"\" + fileName, append))
                {
                    datatotxt.WriteLine(data.Trim());
                }
            }
            catch (Exception)
            {
                saveOutputLog = false;
                SaveOutputLog.IsChecked = false;
                insert_Log("Cannot write Output Log to text file.", 1);
                insert_Log("Save Output Log option disabled.", 1);
                insert_Log("Enable it again from Data Logger Menu if you wish to try again.", 1);
            }
        }

        //------------------------Config Options-----------------------------------------------

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (COM_Select == null)
            {
                COM_Select = new COM_Select_Window();
                COM_Select.Closed += (a, b) => { COM_Select = null; Serial_COM_Selected(); };
                COM_Select.Owner = this;
                COM_Select.Show();
            }
            else
            {
                COM_Select.Show();
                insert_Log("COM Select Window is already open.", 2);
            }
        }

        private void Stop_Sampling_Click(object sender, RoutedEventArgs e)
        {
            if (Stop_Sampling.IsChecked == false)
            {
                DataSampling = false;
            }
            else
            {
                StartDateTime = DateTime.Now;
                DataSampling = true;
            }
            if (DataSampling == true)
            {
                insert_Log("Software is reading measurement data from multimeter.", 0);
            }
            else
            {
                insert_Log("Software will not read measurement data from multimeter.", 2);
            }
        }

        private void Sampling_Only_Click(object sender, RoutedEventArgs e)
        {
            if (Sampling_Only.IsChecked == true)
            {
                isSamplingOnly = true;
                Local_Exit.IsEnabled = false;
                lockControls();
            }
            else
            {
                isSamplingOnly = false;
                Local_Exit.IsEnabled = true;
                unlockControls();
            }
            if (isSamplingOnly == true)
            {
                insert_Log("Software will now only read measurements from the multimeter.", 2);
                insert_Log("All Write (front panel) operations are disabled.", 2);
            }
            else
            {
                insert_Log("Software will allow commands to be send to the multimeter.", 0);
                insert_Log("Sampling only mode disabled. Returned to normal mode.", 0);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        //-----------------------------------------------------------------------

        //---------------------------Data Logger--------------------------------------------

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", Serial_COM_Info.folder_Directory);
            }
            catch (Exception)
            {
                insert_Log("Cannot open test files directory.", 1);
            }
        }

        private void SaveOutputLog_Click(object sender, RoutedEventArgs e)
        {
            if (SaveOutputLog.IsChecked == true)
            {
                saveOutputLog = true;
            }
            else
            {
                saveOutputLog = false;
            }
            if (saveOutputLog == true)
            {
                insert_Log("Output Log entries will be saved to a text file.", 0);
            }
            else
            {
                insert_Log("Output Log entries will not be saved.", 2);
            }
        }

        private void ClearOutputLog_Click(object sender, RoutedEventArgs e)
        {
            Output_Log.Text = String.Empty;
            Output_Log.Inlines.Clear();
        }

        private void SaveMeasurements_Click(object sender, RoutedEventArgs e)
        {
            if (SaveMeasurements.IsChecked == true)
            {
                saveMeasurements = true;
            }
            else
            {
                saveMeasurements = false;
            }
            if (saveMeasurements == true)
            {
                insert_Log("Measurement data will be saved.", 0);
                saveMeasurements_Timer.Enabled = true;
            }
            else
            {
                insert_Log("Measurement data will not be saved.", 2);
            }
        }

        private void SaveMeasurements_Interval_5Sec_Click(object sender, RoutedEventArgs e)
        {
            saveMeasurements_Timer.Interval = 5000;
            insert_Log("Save Measurement Interval set to 5 seconds.", 0);
            SaveMeasurements_IntervalSelected(5);
        }

        private void SaveMeasurements_Interval_10Sec_Click(object sender, RoutedEventArgs e)
        {
            saveMeasurements_Timer.Interval = 10000;
            insert_Log("Save Measurement Interval set to 10 seconds.", 0);
            SaveMeasurements_IntervalSelected(10);
        }

        private void SaveMeasurements_Interval_20Sec_Click(object sender, RoutedEventArgs e)
        {
            saveMeasurements_Timer.Interval = 20000;
            insert_Log("Save Measurement Interval set to 20 seconds.", 0);
            SaveMeasurements_IntervalSelected(20);
        }

        private void SaveMeasurements_Interval_40Sec_Click(object sender, RoutedEventArgs e)
        {
            saveMeasurements_Timer.Interval = 40000;
            insert_Log("Save Measurement Interval set to 40 seconds.", 0);
            SaveMeasurements_IntervalSelected(40);
        }

        private void SaveMeasurements_Interval_1Min_Click(object sender, RoutedEventArgs e)
        {
            saveMeasurements_Timer.Interval = 60000;
            insert_Log("Save Measurement Interval set to 1 Minute.", 0);
            SaveMeasurements_IntervalSelected(60);
        }

        private void SaveMeasurements_Interval_4Min_Click(object sender, RoutedEventArgs e)
        {
            saveMeasurements_Timer.Interval = 240000;
            insert_Log("Save Measurement Interval set to 4 Minutes.", 0);
            SaveMeasurements_IntervalSelected(240);
        }

        private void SaveMeasurements_Interval_8Min_Click(object sender, RoutedEventArgs e)
        {
            saveMeasurements_Timer.Interval = 480000;
            insert_Log("Save Measurement Interval set to 8 Minutes.", 0);
            SaveMeasurements_IntervalSelected(480);
        }

        private void SaveMeasurements_Interval_10Min_Click(object sender, RoutedEventArgs e)
        {
            saveMeasurements_Timer.Interval = 600000;
            insert_Log("Save Measurement Interval set to 10 Minutes.", 0);
            SaveMeasurements_IntervalSelected(600);
        }

        private void SaveMeasurements_IntervalSelected(int interval)
        {
            if (interval == 5)
            {
                SaveMeasurements_Interval_5Sec.IsChecked = true;
            }
            else
            {
                SaveMeasurements_Interval_5Sec.IsChecked = false;
            }
            if (interval == 10)
            {
                SaveMeasurements_Interval_10Sec.IsChecked = true;
            }
            else
            {
                SaveMeasurements_Interval_10Sec.IsChecked = false;
            }
            if (interval == 20)
            {
                SaveMeasurements_Interval_20Sec.IsChecked = true;
            }
            else
            {
                SaveMeasurements_Interval_20Sec.IsChecked = false;
            }
            if (interval == 40)
            {
                SaveMeasurements_Interval_40Sec.IsChecked = true;
            }
            else
            {
                SaveMeasurements_Interval_40Sec.IsChecked = false;
            }
            if (interval == 60)
            {
                SaveMeasurements_Interval_1Min.IsChecked = true;
            }
            else
            {
                SaveMeasurements_Interval_1Min.IsChecked = false;
            }
            if (interval == 240)
            {
                SaveMeasurements_Interval_4Min.IsChecked = true;
            }
            else
            {
                SaveMeasurements_Interval_4Min.IsChecked = false;
            }
            if (interval == 480)
            {
                SaveMeasurements_Interval_8Min.IsChecked = true;
            }
            else
            {
                SaveMeasurements_Interval_8Min.IsChecked = false;
            }
            if (interval == 600)
            {
                SaveMeasurements_Interval_10Min.IsChecked = true;
            }
            else
            {
                SaveMeasurements_Interval_10Min.IsChecked = false;
            }
        }

        private void Auto_Clear_20_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Auto_Clear_Output_Log_Count, 20);
            insert_Log("Output Log will be cleared after " + Auto_Clear_Output_Log_Count + " logs are inserted into it.", 0);
            Auto_Clear_20.IsChecked = true;
            Auto_Clear_40.IsChecked = false;
            Auto_Clear_60.IsChecked = false;
        }

        private void Auto_Clear_40_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Auto_Clear_Output_Log_Count, 40);
            insert_Log("Output Log will be cleared after " + Auto_Clear_Output_Log_Count + " logs are inserted into it.", 0);
            Auto_Clear_20.IsChecked = false;
            Auto_Clear_40.IsChecked = true;
            Auto_Clear_60.IsChecked = false;
        }

        private void Auto_Clear_60_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Auto_Clear_Output_Log_Count, 60);
            insert_Log("Output Log will be cleared after " + Auto_Clear_Output_Log_Count + " logs are inserted into it.", 0);
            Auto_Clear_20.IsChecked = false;
            Auto_Clear_40.IsChecked = false;
            Auto_Clear_60.IsChecked = true;
        }

        //-----------------------------------------------------------------------

        //---------------------------------Graph Options--------------------------------------
        private void ShowMeasurementGraph_Click(object sender, RoutedEventArgs e)
        {
            if (HP34401A_Graph_Window == null)
            {
                Create_HP34401A_Graph_Window();
                ShowMeasurementGraph.IsChecked = true;
                AddDataGraph.IsChecked = true;
                save_to_Graph = true;
                insert_Log("HP34401A Graph Module has been opened.", 0);
            }
            else
            {
                ShowMeasurementGraph.IsChecked = true;
            }
        }

        private void Create_HP34401A_Graph_Window()
        {
            try
            {
                (string Measurement_Unit, string Graph_Y_Axis_Label) = MeasurementUnit_String();
                Thread Waveform_Thread = new Thread(new ThreadStart(() =>
                {
                    HP34401A_Graph_Window = new Graphing_Window(Measurement_Unit, Graph_Y_Axis_Label, "HP 34401A " + Serial_COM_Info.COM_Port);
                    HP34401A_Graph_Window.Show();
                    HP34401A_Graph_Window.Closed += Close_Graph_Event;
                    Dispatcher.Run();
                }));
                Waveform_Thread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-US");
                Waveform_Thread.CurrentUICulture = CultureInfo.CreateSpecificCulture("en-US");
                Waveform_Thread.SetApartmentState(ApartmentState.STA);
                Waveform_Thread.IsBackground = true;
                Waveform_Thread.Start();
            }
            catch (Exception Ex)
            {
                insert_Log(Ex.Message, 1);
                insert_Log("HP34401A Graph Window creation failed.", 1);
            }
        }

        private void Close_Graph_Event(object sender, EventArgs e)
        {
            HP34401A_Graph_Window.Dispatcher.InvokeShutdown();
            HP34401A_Graph_Window = null;
            Close_Graph_Module();
        }

        private void Close_Graph_Module()
        {
            this.Dispatcher.Invoke(() =>
            {
                if (HP34401A_Graph_Window == null & HP34401A_N_Graph_Window == null)
                {
                    AddDataGraph.IsChecked = false;
                }
                save_to_Graph = false;
                ShowMeasurementGraph.IsChecked = false;
                insert_Log("HP34401A Graph Module has been closed.", 0);
            });
        }

        private void Try_Graph_Reset()
        {
            try
            {
                (string Measurement_Unit, string Graph_Y_Axis_Label) = MeasurementUnit_String();
                if (HP34401A_Graph_Window != null)
                {
                    HP34401A_Graph_Window.Measurement_Unit = Measurement_Unit;
                    HP34401A_Graph_Window.Graph_Y_Axis_Label = Graph_Y_Axis_Label;
                    HP34401A_Graph_Window.Graph_Reset = true;
                }
                if (HP34401A_N_Graph_Window != null)
                {
                    HP34401A_N_Graph_Window.Measurement_Unit = Measurement_Unit;
                    HP34401A_N_Graph_Window.Graph_Y_Axis_Label = Graph_Y_Axis_Label;
                    HP34401A_N_Graph_Window.Graph_Reset = true;
                }
            }
            catch (Exception)
            {

            }
        }

        private void AddDataGraph_Click(object sender, RoutedEventArgs e)
        {
            if (AddDataGraph.IsChecked == true & HP34401A_Graph_Window != null)
            {
                save_to_Graph = true;
                insert_Log("Data will be added to graph.", 0);
                AddDataGraph.IsChecked = true;
            }
            else
            {
                save_to_Graph = false;
            }
            if (AddDataGraph.IsChecked == true & HP34401A_N_Graph_Window != null)
            {
                Save_to_N_Graph = true;
                AddDataGraph.IsChecked = true;
            }
            else
            {
                Save_to_N_Graph = false;
            }
            if (HP34401A_Graph_Window == null & HP34401A_N_Graph_Window == null)
            {
                save_to_Graph = false;
                Save_to_N_Graph = false;
                AddDataGraph.IsChecked = false;
            }
        }

        private void N_Sample_Graph_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isValidNum, double N_Sample_Value) = Text_Num(N_Sample_Graph_Text.Text, false, true);
            if (isValidNum == true)
            {
                if (N_Sample_Value >= 10)
                {
                    if (HP34401A_N_Graph_Window == null)
                    {
                        Create_HP34401A_N_Sample_Graph_Window((int)N_Sample_Value);
                        Show_N_Sample_Graph.IsChecked = true;
                        AddDataGraph.IsChecked = true;
                        Save_to_N_Graph = true;
                        insert_Log("HP34401A N Sample Graph Module has been opened.", 0);
                    }
                }
                else
                {
                    insert_Log("N Sample Graph Creation Value must be a positive integer greater than 10.", 2);
                }
            }
            else
            {
                insert_Log("N Sample Graph Creation Value must be a positive integer greater than 10.", 2);
            }
        }

        private void Create_HP34401A_N_Sample_Graph_Window(int N_Samples)
        {
            try
            {
                (string Measurement_Unit, string Graph_Y_Axis_Label) = MeasurementUnit_String();
                Thread Waveform_Thread = new Thread(new ThreadStart(() =>
                {
                    HP34401A_N_Graph_Window = new N_Sample_Graph_Window(N_Samples, Measurement_Unit, Graph_Y_Axis_Label, "HP 34401A " + Serial_COM_Info.COM_Port);
                    HP34401A_N_Graph_Window.Show();
                    HP34401A_N_Graph_Window.Closed += N_Sample_Close_Graph_Event;
                    Dispatcher.Run();
                }));
                Waveform_Thread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-US");
                Waveform_Thread.CurrentUICulture = CultureInfo.CreateSpecificCulture("en-US");
                Waveform_Thread.SetApartmentState(ApartmentState.STA);
                Waveform_Thread.IsBackground = true;
                Waveform_Thread.Start();
            }
            catch (Exception Ex)
            {
                insert_Log(Ex.Message, 1);
                insert_Log("HP34401A N Sample Graph Window creation failed.", 1);
            }
        }

        private void N_Sample_Close_Graph_Event(object sender, EventArgs e)
        {
            HP34401A_N_Graph_Window.Dispatcher.InvokeShutdown();
            HP34401A_N_Graph_Window = null;
            Close_N_Sample_Graph_Module();
        }

        private void Close_N_Sample_Graph_Module()
        {
            this.Dispatcher.Invoke(() =>
            {
                if (HP34401A_Graph_Window == null & HP34401A_N_Graph_Window == null)
                {
                    AddDataGraph.IsChecked = false;
                }
                Save_to_N_Graph = false;
                Show_N_Sample_Graph.IsChecked = false;
                insert_Log("HP34401A N Sample Graph Module has been closed.", 0);
            });
        }

        //-----------------------------------------------------------------------

        //------------------------------Table Options-----------------------------------------

        private void ShowTable_Click(object sender, RoutedEventArgs e)
        {
            if (HP34401A_Table == null)
            {
                Create_HP34401A_Table_Window();
                AddDataTable.IsChecked = true;
                ShowTable.IsChecked = true;
                save_to_Table = true;
                insert_Log("HP34401A Table Window has been opened.", 0);
            }
            else
            {
                ShowTable.IsChecked = true;
            }
        }

        private void AddDataTable_Click(object sender, RoutedEventArgs e)
        {
            if (AddDataTable.IsChecked == true & HP34401A_Table != null)
            {
                save_to_Table = true;
                insert_Log("Data will be added to the table.", 0);
                AddDataTable.IsChecked = true;
            }
            else
            {
                save_to_Table = false;
                AddDataTable.IsChecked = false;
            }
        }

        private void Create_HP34401A_Table_Window()
        {
            try
            {
                Thread Table_Thread = new Thread(new ThreadStart(() =>
                {
                    HP34401A_Table = new Measurement_Data_Table("HP 34401A " + Serial_COM_Info.COM_Port);
                    HP34401A_Table.Show();
                    HP34401A_Table.Closed += Close_Table_Event;
                    Dispatcher.Run();
                }));
                Table_Thread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-US");
                Table_Thread.CurrentUICulture = CultureInfo.CreateSpecificCulture("en-US");
                Table_Thread.SetApartmentState(ApartmentState.STA);
                Table_Thread.IsBackground = true;
                Table_Thread.Start();
            }
            catch (Exception Ex)
            {
                insert_Log(Ex.Message, 1);
                insert_Log("HP34401A Table Window creation failed.", 1);
            }
        }

        private void Close_Table_Event(object sender, EventArgs e)
        {
            HP34401A_Table.Dispatcher.InvokeShutdown();
            HP34401A_Table = null;
            Close_Table_Window();
        }

        private void Close_Table_Window()
        {
            this.Dispatcher.Invoke(() =>
            {
                save_to_Table = false;
                AddDataTable.IsChecked = false;
                ShowTable.IsChecked = false;
                insert_Log("HP34401A Table Window has been closed.", 0);
            });
        }

        //-----------------------------------------------------------------------

        //----------------------------Speech Options-------------------------------------------

        private void EnableSpeech_Click(object sender, RoutedEventArgs e)
        {
            if (EnableSpeech.IsChecked == true)
            {
                Interlocked.Exchange(ref isSpeechActive, 1);
                Speech_Status.Content = "Enabled";
                insert_Log("The Speech Synthesizer is Enabled.", 4);
            }
            else
            {
                Interlocked.Exchange(ref isSpeechActive, 0);
                Speech_Status.Content = "Disabled";
                insert_Log("The Speech Synthesizer is Disabled.", 4);
            }
        }

        private void VoiceMale_Click(object sender, RoutedEventArgs e)
        {
            Voice.SelectVoiceByHints(VoiceGender.Male);
            VoiceMale.IsChecked = true;
            VoiceFemale.IsChecked = false;
            insert_Log("David will voice your measurements.", 0);
        }

        private void VoiceFemale_Click(object sender, RoutedEventArgs e)
        {
            Voice.SelectVoiceByHints(VoiceGender.Female);
            VoiceMale.IsChecked = false;
            VoiceFemale.IsChecked = true;
            insert_Log("Zira will voice your measurements.", 0);
        }

        private void VoiceSlow_Click(object sender, RoutedEventArgs e)
        {
            Voice.Rate = 0;
            VoiceSpeedSelected(0);
            insert_Log("Voice speed set to slow.", 4);
        }

        private void VoiceMedium_Click(object sender, RoutedEventArgs e)
        {
            Voice.Rate = 1;
            VoiceSpeedSelected(1);
            insert_Log("Voice speed set to medium.", 4);
        }

        private void VoiceFast_Click(object sender, RoutedEventArgs e)
        {
            Voice.Rate = 2;
            VoiceSpeedSelected(2);
            insert_Log("Voice speed set to fast.", 4);
        }

        private void VoiceVeryFast_Click(object sender, RoutedEventArgs e)
        {
            Voice.Rate = 3;
            VoiceSpeedSelected(3);
            insert_Log("Voice speed set to very fast.", 4);
        }

        private void VoiceFastest_Click(object sender, RoutedEventArgs e)
        {
            Voice.Rate = 4;
            VoiceSpeedSelected(4);
            insert_Log("Voice speed set to fastest.", 4);
        }

        private void VoiceSpeedSelected(int speed)
        {
            if (speed == 0)
            {
                VoiceSlow.IsChecked = true;
            }
            else
            {
                VoiceSlow.IsChecked = false;
            }
            if (speed == 1)
            {
                VoiceMedium.IsChecked = true;
            }
            else
            {
                VoiceMedium.IsChecked = false;
            }
            if (speed == 2)
            {
                VoiceFast.IsChecked = true;
            }
            else
            {
                VoiceFast.IsChecked = false;
            }
            if (speed == 3)
            {
                VoiceVeryFast.IsChecked = true;
            }
            else
            {
                VoiceVeryFast.IsChecked = false;
            }
            if (speed == 4)
            {
                VoiceFastest.IsChecked = true;
            }
            else
            {
                VoiceFastest.IsChecked = false;
            }
        }

        private void Voice_Volume_10_Click(object sender, RoutedEventArgs e)
        {
            Voice.Volume = 10;
            VoiceVolumeSelected(0);
            insert_Log("Voice volume set to 10%.", 4);
        }

        private void Voice_Volume_20_Click(object sender, RoutedEventArgs e)
        {
            Voice.Volume = 20;
            VoiceVolumeSelected(1);
            insert_Log("Voice volume set to 20%.", 4);
        }

        private void Voice_Volume_30_Click(object sender, RoutedEventArgs e)
        {
            Voice.Volume = 30;
            VoiceVolumeSelected(2);
            insert_Log("Voice volume set to 30%.", 4);
        }

        private void Voice_Volume_40_Click(object sender, RoutedEventArgs e)
        {
            Voice.Volume = 40;
            VoiceVolumeSelected(3);
            insert_Log("Voice volume set to 40%.", 4);
        }

        private void Voice_Volume_50_Click(object sender, RoutedEventArgs e)
        {
            Voice.Volume = 50;
            VoiceVolumeSelected(4);
            insert_Log("Voice volume set to 50%.", 4);
        }

        private void Voice_Volume_60_Click(object sender, RoutedEventArgs e)
        {
            Voice.Volume = 60;
            VoiceVolumeSelected(5);
            insert_Log("Voice volume set to 60%.", 4);
        }

        private void Voice_Volume_70_Click(object sender, RoutedEventArgs e)
        {
            Voice.Volume = 70;
            VoiceVolumeSelected(6);
            insert_Log("Voice volume set to 70%.", 4);
        }

        private void Voice_Volume_80_Click(object sender, RoutedEventArgs e)
        {
            Voice.Volume = 80;
            VoiceVolumeSelected(7);
            insert_Log("Voice volume set to 80%.", 4);
        }

        private void Voice_Volume_90_Click(object sender, RoutedEventArgs e)
        {
            Voice.Volume = 90;
            VoiceVolumeSelected(8);
            insert_Log("Voice volume set to 90%.", 4);
        }

        private void Voice_Volume_100_Click(object sender, RoutedEventArgs e)
        {
            Voice.Volume = 100;
            VoiceVolumeSelected(9);
            insert_Log("Voice volume set to 100%.", 4);
        }

        private void VoiceVolumeSelected(int volume)
        {
            if (volume == 0)
            {
                Voice_Volume_10.IsChecked = true;
            }
            else
            {
                Voice_Volume_10.IsChecked = false;
            }
            if (volume == 1)
            {
                Voice_Volume_20.IsChecked = true;
            }
            else
            {
                Voice_Volume_20.IsChecked = false;
            }
            if (volume == 2)
            {
                Voice_Volume_30.IsChecked = true;
            }
            else
            {
                Voice_Volume_30.IsChecked = false;
            }
            if (volume == 3)
            {
                Voice_Volume_40.IsChecked = true;
            }
            else
            {
                Voice_Volume_40.IsChecked = false;
            }
            if (volume == 4)
            {
                Voice_Volume_50.IsChecked = true;
            }
            else
            {
                Voice_Volume_50.IsChecked = false;
            }
            if (volume == 5)
            {
                Voice_Volume_60.IsChecked = true;
            }
            else
            {
                Voice_Volume_60.IsChecked = false;
            }
            if (volume == 6)
            {
                Voice_Volume_70.IsChecked = true;
            }
            else
            {
                Voice_Volume_70.IsChecked = false;
            }
            if (volume == 7)
            {
                Voice_Volume_80.IsChecked = true;
            }
            else
            {
                Voice_Volume_80.IsChecked = false;
            }
            if (volume == 8)
            {
                Voice_Volume_90.IsChecked = true;
            }
            else
            {
                Voice_Volume_90.IsChecked = false;
            }
            if (volume == 9)
            {
                Voice_Volume_100.IsChecked = true;
            }
            else
            {
                Voice_Volume_100.IsChecked = false;
            }
        }

        //-----------------------------------------------------------------------

        //----------------------------About Options-------------------------------------------

        private void DeviceSupport_Click(object sender, RoutedEventArgs e)
        {
            insert_Log("This software was created for HP Agilent Keysight 34401A.", 4);
            insert_Log("You will need an AR488 Arduino GPIB adapter.", 4);
        }

        private void Credits_Click(object sender, RoutedEventArgs e)
        {
            insert_Log("Created by Niravk Patel.", 4);
            insert_Log("Email: niravkp97@gmail.com", 4);
            insert_Log("This program was created using C# WPF .Net Framework 4.7.2", 4);
            insert_Log("Supports Windows 10, 8, 8.1, and 7", 4);
        }

        //-----------------------------------------------------------------------

        //--------------------------Measurements Options---------------------------------------------


        //------------------------------Main Measurements---------------------------------------------
        private void VDC_Button_Click(object sender, RoutedEventArgs e)
        {
            MesurementSelector(0);
            Range_Tab_Selector(0);
            NPLC_Indicator(3); //NPLC 10
            Resolution_Indicator(9); //Nothing Selected
            insert_Log("DCV Measurement Selected.", 3);
            VDC_Range_Indicator(0); //Auto
            SerialWriteQueue.Add("CONF:VOLT:DC DEF,DEF");
            lockControls();
            isUserSendCommand = true;
            Try_Graph_Reset();
            Speedup_Interval();
        }

        private void ADC_Button_Click(object sender, RoutedEventArgs e)
        {
            MesurementSelector(1);
            Range_Tab_Selector(1);
            NPLC_Indicator(3); //NPLC 10
            Resolution_Indicator(9); //Nothing Selected
            insert_Log("DCI Measurement Selected.", 3);
            ADC_Range_Indicator(0); //Auto
            SerialWriteQueue.Add("CONF:CURR:DC DEF,DEF");
            lockControls();
            isUserSendCommand = true;
            Try_Graph_Reset();
            Speedup_Interval();
        }

        private void VAC_Button_Click(object sender, RoutedEventArgs e)
        {
            MesurementSelector(2);
            Range_Tab_Selector(2);
            NPLC_Indicator(9); //NPLC Nothing Selected
            Resolution_Indicator(9); //Nothing Selected
            insert_Log("ACV Measurement Selected.", 3);
            VAC_Range_Indicator(0); //Auto
            SerialWriteQueue.Add("CONF:VOLT:AC DEF,DEF");
            lockControls();
            isUserSendCommand = true;
            Try_Graph_Reset();
            Speedup_Interval();
        }

        private void AAC_Button_Click(object sender, RoutedEventArgs e)
        {
            MesurementSelector(3);
            Range_Tab_Selector(3);
            NPLC_Indicator(9); //NPLC Nothing Selected
            Resolution_Indicator(9); //Nothing Selected
            insert_Log("ACI Measurement Selected.", 3);
            AAC_Range_Indicator(0); //Auto
            SerialWriteQueue.Add("CONF:CURR:AC DEF,DEF");
            lockControls();
            isUserSendCommand = true;
            Try_Graph_Reset();
            Speedup_Interval();
        }

        private void TwoOhms_Button_Click(object sender, RoutedEventArgs e)
        {
            MesurementSelector(4);
            Range_Tab_Selector(4);
            NPLC_Indicator(3); //NPLC 10
            Resolution_Indicator(9); //Nothing Selected
            insert_Log("2 Wire Ohms Measurement Selected.", 3);
            Ohms_Range_Indicator(0); //Auto
            SerialWriteQueue.Add("CONF:RES DEF,DEF");
            lockControls();
            isUserSendCommand = true;
            Try_Graph_Reset();
            Speedup_Interval();
        }

        private void FourOhms_Button_Click(object sender, RoutedEventArgs e)
        {
            MesurementSelector(5);
            Range_Tab_Selector(4);
            NPLC_Indicator(3); //NPLC 10
            Resolution_Indicator(9); //Nothing Selected
            insert_Log("4 Wire Ohms Measurement Selected.", 3);
            Ohms_Range_Indicator(0); //Auto
            SerialWriteQueue.Add("CONF:FRES DEF,DEF");
            lockControls();
            isUserSendCommand = true;
            Try_Graph_Reset();
            Speedup_Interval();
        }

        private void FREQ_Button_Click(object sender, RoutedEventArgs e)
        {
            MesurementSelector(6);
            Range_Tab_Selector(5);
            NPLC_Indicator(9); //NPLC Nothing Selected
            Resolution_Indicator(9); //Nothing Selected
            insert_Log("Frequency Measurement Selected.", 3);
            FREQ_PER_Range_Indicator(0); //Auto
            SerialWriteQueue.Add("CONF:FREQ DEF,DEF");
            lockControls();
            isUserSendCommand = true;
            Try_Graph_Reset();
            Speedup_Interval();
        }

        private void PER_Button_Click(object sender, RoutedEventArgs e)
        {
            MesurementSelector(7);
            Range_Tab_Selector(5);
            NPLC_Indicator(9); //NPLC Nothing Selected
            Resolution_Indicator(9); //Nothing Selected
            insert_Log("Period Measurement Selected.", 3);
            FREQ_PER_Range_Indicator(0); //Auto
            SerialWriteQueue.Add("CONF:PER DEF,DEF");
            lockControls();
            isUserSendCommand = true;
            Try_Graph_Reset();
            Speedup_Interval();
        }

        private void DIODE_Button_Click(object sender, RoutedEventArgs e)
        {
            MesurementSelector(8);
            NPLC_Indicator(9); //NPLC Nothing Selected
            Resolution_Indicator(9); //Nothing Selected
            insert_Log("Diode Measurement Selected.", 3);
            SerialWriteQueue.Add("CONF:DIOD");
            lockControls();
            isUserSendCommand = true;
            Try_Graph_Reset();
            Speedup_Interval();
        }

        private void CONT_Button_Click(object sender, RoutedEventArgs e)
        {
            MesurementSelector(9);
            NPLC_Indicator(9); //NPLC Nothing Selected
            Resolution_Indicator(9); //Nothing Selected
            insert_Log("Continuity Measurement Selected.", 3);
            SerialWriteQueue.Add("CONF:CONT");
            lockControls();
            isUserSendCommand = true;
            Try_Graph_Reset();
            Speedup_Interval();
        }

        private void Range_Tab_Selector(int RangeType)
        {
            if (RangeType == 0) //VDC
            {
                VDC_Tab.IsSelected = true;
            }
            else
            {
                VDC_Tab.IsSelected = false;
            }
            if (RangeType == 1) //ADC
            {
                ADC_Tab.IsSelected = true;
            }
            else
            {
                ADC_Tab.IsSelected = false;
            }
            if (RangeType == 2) //VAC
            {
                VAC_Tab.IsSelected = true;
            }
            else
            {
                VAC_Tab.IsSelected = false;
            }
            if (RangeType == 3) //AAC
            {
                AAC_Tab.IsSelected = true;
            }
            else
            {
                AAC_Tab.IsSelected = false;
            }
            if (RangeType == 4) //Ohms
            {
                Ohms_Tab.IsSelected = true;
            }
            else
            {
                Ohms_Tab.IsSelected = false;
            }
            if (RangeType == 5) //FREQ/PER
            {
                FREQ_PER_Tab.IsSelected = true;
            }
            else
            {
                FREQ_PER_Tab.IsSelected = false;
            }

        }

        private void MesurementSelector(int MeasurementChoice)
        {
            if (MeasurementChoice == 0) //VDC
            {
                VDC_Border.BorderBrush = Selected;
                Measurement_Selected = 0;
                Interlocked.Exchange(ref Selected_Measurement_type, 0);
            }
            else
            {
                VDC_Border.BorderBrush = Deselected;
            }
            if (MeasurementChoice == 1) //ADC
            {
                ADC_Border.BorderBrush = Selected;
                Measurement_Selected = 1;
                Interlocked.Exchange(ref Selected_Measurement_type, 1);
            }
            else
            {
                ADC_Border.BorderBrush = Deselected;
            }
            if (MeasurementChoice == 2) //VAC
            {
                VAC_Border.BorderBrush = Selected;
                Measurement_Selected = 2;
                Interlocked.Exchange(ref Selected_Measurement_type, 2);
            }
            else
            {
                VAC_Border.BorderBrush = Deselected;
            }
            if (MeasurementChoice == 3) //AAC
            {
                AAC_Border.BorderBrush = Selected;
                Measurement_Selected = 3;
                Interlocked.Exchange(ref Selected_Measurement_type, 3);
            }
            else
            {
                AAC_Border.BorderBrush = Deselected;
            }
            if (MeasurementChoice == 4) //2 Wire Ohms
            {
                TwoOhms_Border.BorderBrush = Selected;
                Measurement_Selected = 4;
                Interlocked.Exchange(ref Selected_Measurement_type, 4);
            }
            else
            {
                TwoOhms_Border.BorderBrush = Deselected;
            }
            if (MeasurementChoice == 5) //4 Wire Ohms
            {
                FourOhms_Border.BorderBrush = Selected;
                Measurement_Selected = 5;
                Interlocked.Exchange(ref Selected_Measurement_type, 5);
            }
            else
            {
                FourOhms_Border.BorderBrush = Deselected;
            }
            if (MeasurementChoice == 6) //FREQ
            {
                FREQ_Border.BorderBrush = Selected;
                Measurement_Selected = 6;
                Interlocked.Exchange(ref Selected_Measurement_type, 6);
            }
            else
            {
                FREQ_Border.BorderBrush = Deselected;
            }
            if (MeasurementChoice == 7) //PER
            {
                PER_Border.BorderBrush = Selected;
                Measurement_Selected = 7;
                Interlocked.Exchange(ref Selected_Measurement_type, 7);
            }
            else
            {
                PER_Border.BorderBrush = Deselected;
            }
            if (MeasurementChoice == 8) //Diode
            {
                DIODE_Border.BorderBrush = Selected;
                Measurement_Selected = 8;
                Interlocked.Exchange(ref Selected_Measurement_type, 8);
            }
            else
            {
                DIODE_Border.BorderBrush = Deselected;
            }
            if (MeasurementChoice == 9) //Continuity
            {
                CONT_Border.BorderBrush = Selected;
                Measurement_Selected = 9;
                Interlocked.Exchange(ref Selected_Measurement_type, 9);
            }
            else
            {
                CONT_Border.BorderBrush = Deselected;
            }
        }

        //------------------------------Main Measurements---------------------------------------------

        //----------------------------------Measurements Ranges-------------------------------------

        //------------------------------------VDC Start-----------------------------------------------

        private void VDC_Auto_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0)
            {
                SerialWriteQueue.Add(VDC_Range_Indicator(0));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("VDC Range set to Auto.", 5);
            }
            else
            {
                insert_Log("Cannot set VDC Range when VDC Measurement is not selected.", 2);
            }
        }

        private void VDC_100mV_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0)
            {
                SerialWriteQueue.Add(VDC_Range_Indicator(1));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("VDC Range set to 100mV.", 5);
            }
            else
            {
                insert_Log("Cannot set VDC Range when VDC Measurement is not selected.", 2);
            }
        }

        private void VDC_1V_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0)
            {
                SerialWriteQueue.Add(VDC_Range_Indicator(2));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("VDC Range set to 1V.", 5);
            }
            else
            {
                insert_Log("Cannot set VDC Range when VDC Measurement is not selected.", 2);
            }
        }

        private void VDC_10V_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0)
            {
                SerialWriteQueue.Add(VDC_Range_Indicator(3));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("VDC Range set to 10V.", 5);
            }
            else
            {
                insert_Log("Cannot set VDC Range when VDC Measurement is not selected.", 2);
            }
        }

        private void VDC_100V_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0)
            {
                SerialWriteQueue.Add(VDC_Range_Indicator(4));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("VDC Range set to 100V.", 5);
            }
            else
            {
                insert_Log("Cannot set VDC Range when VDC Measurement is not selected.", 2);
            }
        }

        private void VDC_1000V_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0)
            {
                SerialWriteQueue.Add(VDC_Range_Indicator(5));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("VDC Range set to 1000V.", 5);
            }
            else
            {
                insert_Log("Cannot set VDC Range when VDC Measurement is not selected.", 2);
            }
        }

        private string VDC_Range_Indicator(int Range)
        {
            string RangeCommand = "VOLTage:DC:RANGe:AUTO ON";
            if (Range == 0)
            {
                VDC_Auto_Border.BorderBrush = Selected;
                RangeCommand = "VOLTage:DC:RANGe:AUTO ON";
            }
            else
            {
                VDC_Auto_Border.BorderBrush = Deselected;
            }
            if (Range == 1)
            {
                VDC_100mV_Border.BorderBrush = Selected;
                RangeCommand = "VOLTage:DC:RANGe 0.1";
            }
            else
            {
                VDC_100mV_Border.BorderBrush = Deselected;
            }
            if (Range == 2)
            {
                VDC_1V_Border.BorderBrush = Selected;
                RangeCommand = "VOLTage:DC:RANGe 1";
            }
            else
            {
                VDC_1V_Border.BorderBrush = Deselected;
            }
            if (Range == 3)
            {
                VDC_10V_Border.BorderBrush = Selected;
                RangeCommand = "VOLTage:DC:RANGe 10";
            }
            else
            {
                VDC_10V_Border.BorderBrush = Deselected;
            }
            if (Range == 4)
            {
                VDC_100V_Border.BorderBrush = Selected;
                RangeCommand = "VOLTage:DC:RANGe 100";
            }
            else
            {
                VDC_100V_Border.BorderBrush = Deselected;
            }
            if (Range == 5)
            {
                VDC_1000V_Border.BorderBrush = Selected;
                RangeCommand = "VOLTage:DC:RANGe 1000";
            }
            else
            {
                VDC_1000V_Border.BorderBrush = Deselected;
            }
            return RangeCommand;
        }

        //------------------------------------VDC END-----------------------------------------------

        //-------------------------ADC Range----------------------------------------------

        private void ADC_Auto_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 1)
            {
                SerialWriteQueue.Add(ADC_Range_Indicator(0));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("DCI Range set to Auto.", 5);
            }
            else
            {
                insert_Log("Cannot set DCI Range when DCI Measurement is not selected.", 2);
            }
        }

        private void ADC_10mA_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 1)
            {
                SerialWriteQueue.Add(ADC_Range_Indicator(1));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("DCI Range set to 10mA.", 5);
            }
            else
            {
                insert_Log("Cannot set DCI Range when DCI Measurement is not selected.", 2);
            }
        }

        private void ADC_100mA_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 1)
            {
                SerialWriteQueue.Add(ADC_Range_Indicator(2));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("DCI Range set to 100mA.", 5);
            }
            else
            {
                insert_Log("Cannot set DCI Range when DCI Measurement is not selected.", 2);
            }
        }

        private void ADC_1A_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 1)
            {
                SerialWriteQueue.Add(ADC_Range_Indicator(3));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("DCI Range set to 1A.", 5);
            }
            else
            {
                insert_Log("Cannot set DCI Range when DCI Measurement is not selected.", 2);
            }
        }

        private void ADC_3A_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 1)
            {
                SerialWriteQueue.Add(ADC_Range_Indicator(4));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("DCI Range set to 3A.", 5);
            }
            else
            {
                insert_Log("Cannot set DCI Range when DCI Measurement is not selected.", 2);
            }
        }

        private string ADC_Range_Indicator(int Range)
        {
            string RangeCommand = "CURRent:DC:RANGe:AUTO ON";
            if (Range == 0)
            {
                ADC_Auto_Border.BorderBrush = Selected;
                RangeCommand = "CURRent:DC:RANGe:AUTO ON";
            }
            else
            {
                ADC_Auto_Border.BorderBrush = Deselected;
            }
            if (Range == 1)
            {
                ADC_10mA_Border.BorderBrush = Selected;
                RangeCommand = "CURRent:DC:RANGe 0.01";
            }
            else
            {
                ADC_10mA_Border.BorderBrush = Deselected;
            }
            if (Range == 2)
            {
                ADC_100mA_Border.BorderBrush = Selected;
                RangeCommand = "CURRent:DC:RANGe 0.1";
            }
            else
            {
                ADC_100mA_Border.BorderBrush = Deselected;
            }
            if (Range == 3)
            {
                ADC_1A_Border.BorderBrush = Selected;
                RangeCommand = "CURRent:DC:RANGe 1";
            }
            else
            {
                ADC_1A_Border.BorderBrush = Deselected;
            }
            if (Range == 4)
            {
                ADC_3A_Border.BorderBrush = Selected;
                RangeCommand = "CURRent:DC:RANGe 3";
            }
            else
            {
                ADC_3A_Border.BorderBrush = Deselected;
            }
            return RangeCommand;
        }

        //-----------------------------------------------------------------------

        //------------------------VAC Range-----------------------------------------------

        private void VAC_Auto_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 2)
            {
                SerialWriteQueue.Add(VAC_Range_Indicator(0));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("VAC Range set to Auto.", 5);
            }
            else
            {
                insert_Log("Cannot set VAC Range when VAC Measurement is not selected.", 2);
            }
        }

        private void VAC_100mV_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 2)
            {
                SerialWriteQueue.Add(VAC_Range_Indicator(1));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("VAC Range set to 100mV.", 5);
            }
            else
            {
                insert_Log("Cannot set VAC Range when VAC Measurement is not selected.", 2);
            }
        }

        private void VAC_1V_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 2)
            {
                SerialWriteQueue.Add(VAC_Range_Indicator(2));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("VAC Range set to 1V.", 5);
            }
            else
            {
                insert_Log("Cannot set VAC Range when VAC Measurement is not selected.", 2);
            }
        }

        private void VAC_10V_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 2)
            {
                SerialWriteQueue.Add(VAC_Range_Indicator(3));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("VAC Range set to 10V.", 5);
            }
            else
            {
                insert_Log("Cannot set VAC Range when VAC Measurement is not selected.", 2);
            }
        }

        private void VAC_100V_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 2)
            {
                SerialWriteQueue.Add(VAC_Range_Indicator(4));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("VAC Range set to 100V.", 5);
            }
            else
            {
                insert_Log("Cannot set VAC Range when VAC Measurement is not selected.", 2);
            }
        }

        private void VAC_1000V_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 2)
            {
                SerialWriteQueue.Add(VAC_Range_Indicator(5));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("VAC Range set to 1000V.", 5);
            }
            else
            {
                insert_Log("Cannot set VAC Range when VAC Measurement is not selected.", 2);
            }
        }

        private string VAC_Range_Indicator(int Range)
        {
            string RangeCommand = "VOLTage:AC:RANGe:AUTO ON";
            if (Range == 0)
            {
                VAC_Auto_Border.BorderBrush = Selected;
                RangeCommand = "VOLTage:AC:RANGe:AUTO ON";
            }
            else
            {
                VAC_Auto_Border.BorderBrush = Deselected;
            }
            if (Range == 1)
            {
                VAC_100mV_Border.BorderBrush = Selected;
                RangeCommand = "VOLTage:AC:RANGe 0.1";
            }
            else
            {
                VAC_100mV_Border.BorderBrush = Deselected;
            }
            if (Range == 2)
            {
                VAC_1V_Border.BorderBrush = Selected;
                RangeCommand = "VOLTage:AC:RANGe 1";
            }
            else
            {
                VAC_1V_Border.BorderBrush = Deselected;
            }
            if (Range == 3)
            {
                VAC_10V_Border.BorderBrush = Selected;
                RangeCommand = "VOLTage:AC:RANGe 10";
            }
            else
            {
                VAC_10V_Border.BorderBrush = Deselected;
            }
            if (Range == 4)
            {
                VAC_100V_Border.BorderBrush = Selected;
                RangeCommand = "VOLTage:AC:RANGe 100";
            }
            else
            {
                VAC_100V_Border.BorderBrush = Deselected;
            }
            if (Range == 5)
            {
                VAC_1000V_Border.BorderBrush = Selected;
                RangeCommand = "VOLTage:AC:RANGe 1000";
            }
            else
            {
                VAC_1000V_Border.BorderBrush = Deselected;
            }
            return RangeCommand;
        }

        //------------------------------------VAC-----------------------------------------------

        //----------------------------AAC Range-------------------------------------------

        private void AAC_Auto_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 3)
            {
                SerialWriteQueue.Add(AAC_Range_Indicator(0));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("ACI Range set to Auto.", 5);
            }
            else
            {
                insert_Log("Cannot set ACI Range when ACI Measurement is not selected.", 2);
            }
        }

        private void AAC_1A_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 3)
            {
                SerialWriteQueue.Add(AAC_Range_Indicator(1));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("ACI Range set to 1A.", 5);
            }
            else
            {
                insert_Log("Cannot set ACI Range when ACI Measurement is not selected.", 2);
            }
        }

        private void AAC_3A_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 3)
            {
                SerialWriteQueue.Add(AAC_Range_Indicator(2));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("ACI Range set to 3A.", 5);
            }
            else
            {
                insert_Log("Cannot set ACI Range when ACI Measurement is not selected.", 2);
            }
        }

        private string AAC_Range_Indicator(int Range)
        {
            string RangeCommand = "CURRent:AC:RANGe:AUTO ON";
            if (Range == 0)
            {
                AAC_Auto_Border.BorderBrush = Selected;
                RangeCommand = "CURRent:AC:RANGe:AUTO ON";
            }
            else
            {
                AAC_Auto_Border.BorderBrush = Deselected;
            }
            if (Range == 1)
            {
                AAC_1A_Border.BorderBrush = Selected;
                RangeCommand = "CURRent:AC:RANGe 1";
            }
            else
            {
                AAC_1A_Border.BorderBrush = Deselected;
            }
            if (Range == 2)
            {
                AAC_3A_Border.BorderBrush = Selected;
                RangeCommand = "CURRent:AC:RANGe 3";
            }
            else
            {
                AAC_3A_Border.BorderBrush = Deselected;
            }
            return RangeCommand;
        }

        //-----------------------------------------------------------------------

        //-------------------------Ohms Range----------------------------------------------

        private void Ohms_Auto_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 4 || Measurement_Selected == 5)
            {
                SerialWriteQueue.Add(Ohms_Range_Indicator(0));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Ohms Range set to Auto.", 5);
            }
            else
            {
                insert_Log("Cannot set Ω Range when 2/4 Wire Ω Measurement is not selected.", 2);
            }
        }

        private void Ohms_100_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 4 || Measurement_Selected == 5)
            {
                SerialWriteQueue.Add(Ohms_Range_Indicator(1));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Ohms Range set to 100Ω.", 5);
            }
            else
            {
                insert_Log("Cannot set Ω Range when 2/4 Wire Ω Measurement is not selected.", 2);
            }
        }

        private void Ohms_1K_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 4 || Measurement_Selected == 5)
            {
                SerialWriteQueue.Add(Ohms_Range_Indicator(2));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Ohms Range set to 1KΩ.", 5);
            }
            else
            {
                insert_Log("Cannot set Ω Range when 2/4 Wire Ω Measurement is not selected.", 2);
            }
        }

        private void Ohms_10K_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 4 || Measurement_Selected == 5)
            {
                SerialWriteQueue.Add(Ohms_Range_Indicator(3));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Ohms Range set to 10KΩ.", 5);
            }
            else
            {
                insert_Log("Cannot set Ω Range when 2/4 Wire Ω Measurement is not selected.", 2);
            }
        }

        private void Ohms_100K_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 4 || Measurement_Selected == 5)
            {
                SerialWriteQueue.Add(Ohms_Range_Indicator(4));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Ohms Range set to 100KΩ.", 5);
            }
            else
            {
                insert_Log("Cannot set Ω Range when 2/4 Wire Ω Measurement is not selected.", 2);
            }
        }

        private void Ohms_1M_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 4 || Measurement_Selected == 5)
            {
                SerialWriteQueue.Add(Ohms_Range_Indicator(5));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Ohms Range set to 1MΩ.", 5);
            }
            else
            {
                insert_Log("Cannot set Ω Range when 2/4 Wire Ω Measurement is not selected.", 2);
            }
        }

        private void Ohms_10M_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 4 || Measurement_Selected == 5)
            {
                SerialWriteQueue.Add(Ohms_Range_Indicator(6));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Ohms Range set to 10MΩ.", 5);
            }
            else
            {
                insert_Log("Cannot set Ω Range when 2/4 Wire Ω Measurement is not selected.", 2);
            }
        }

        private void Ohms_100M_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 4 || Measurement_Selected == 5)
            {
                SerialWriteQueue.Add(Ohms_Range_Indicator(7));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Ohms Range set to 100MΩ.", 5);
            }
            else
            {
                insert_Log("Cannot set Ω Range when 2/4 Wire Ω Measurement is not selected.", 2);
            }
        }

        private string Ohms_Range_Indicator(int Range)
        {
            string RangeCommand = "";
            if (Range == 0)
            {
                Ohms_Auto_Border.BorderBrush = Selected;
                if (Measurement_Selected == 4)
                {
                    RangeCommand = "RESistance:RANGe:AUTO ON";
                }
                else if (Measurement_Selected == 5)
                {
                    RangeCommand = "FRESistance:RANGe:AUTO ON";
                }
            }
            else
            {
                Ohms_Auto_Border.BorderBrush = Deselected;
            }
            if (Range == 1)
            {
                Ohms_100_Border.BorderBrush = Selected;
                if (Measurement_Selected == 4)
                {
                    RangeCommand = "RESistance:RANGe 100";
                }
                else if (Measurement_Selected == 5)
                {
                    RangeCommand = "FRESistance:RANGe 100";
                }
            }
            else
            {
                Ohms_100_Border.BorderBrush = Deselected;
            }
            if (Range == 2)
            {
                Ohms_1K_Border.BorderBrush = Selected;
                if (Measurement_Selected == 4)
                {
                    RangeCommand = "RESistance:RANGe 1E+3";
                }
                else if (Measurement_Selected == 5)
                {
                    RangeCommand = "FRESistance:RANGe 1E+3";
                }
            }
            else
            {
                Ohms_1K_Border.BorderBrush = Deselected;
            }
            if (Range == 3)
            {
                Ohms_10K_Border.BorderBrush = Selected;
                if (Measurement_Selected == 4)
                {
                    RangeCommand = "RESistance:RANGe 10E+3";
                }
                else if (Measurement_Selected == 5)
                {
                    RangeCommand = "FRESistance:RANGe 10E+3";
                }
            }
            else
            {
                Ohms_10K_Border.BorderBrush = Deselected;
            }
            if (Range == 4)
            {
                Ohms_100K_Border.BorderBrush = Selected;
                if (Measurement_Selected == 4)
                {
                    RangeCommand = "RESistance:RANGe 100E+3";
                }
                else if (Measurement_Selected == 5)
                {
                    RangeCommand = "FRESistance:RANGe 100E+3";
                }
            }
            else
            {
                Ohms_100K_Border.BorderBrush = Deselected;
            }
            if (Range == 5)
            {
                Ohms_1M_Border.BorderBrush = Selected;
                if (Measurement_Selected == 4)
                {
                    RangeCommand = "RESistance:RANGe 1E+6";
                }
                else if (Measurement_Selected == 5)
                {
                    RangeCommand = "FRESistance:RANGe 1E+6";
                }
            }
            else
            {
                Ohms_1M_Border.BorderBrush = Deselected;
            }
            if (Range == 6)
            {
                Ohms_10M_Border.BorderBrush = Selected;
                if (Measurement_Selected == 4)
                {
                    RangeCommand = "RESistance:RANGe 10E+6";
                }
                else if (Measurement_Selected == 5)
                {
                    RangeCommand = "FRESistance:RANGe 10E+6";
                }
            }
            else
            {
                Ohms_10M_Border.BorderBrush = Deselected;
            }
            if (Range == 7)
            {
                Ohms_100M_Border.BorderBrush = Selected;
                if (Measurement_Selected == 4)
                {
                    RangeCommand = "RESistance:RANGe 100E+6";
                }
                else if (Measurement_Selected == 5)
                {
                    RangeCommand = "FRESistance:RANGe 100E+6";
                }
            }
            else
            {
                Ohms_100M_Border.BorderBrush = Deselected;
            }
            return RangeCommand;
        }

        //-----------------------------------------------------------------------

        //------------------------FREQ/CONT Range-----------------------------------------------

        private void FREQ_PER_Auto_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 6 || Measurement_Selected == 7)
            {
                SerialWriteQueue.Add(FREQ_PER_Range_Indicator(0));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Frequency/Period Range set to Auto.", 5);
            }
            else
            {
                insert_Log("Cannot set Frequency/Period Range when Frequency/Period Measurement is not selected.", 2);
            }
        }

        private void FREQ_PER_100mV_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 6 || Measurement_Selected == 7)
            {
                SerialWriteQueue.Add(FREQ_PER_Range_Indicator(1));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Frequency/Period Range set to 100mV.", 5);
            }
            else
            {
                insert_Log("Cannot set Frequency/Period Range when Frequency/Period Measurement is not selected.", 2);
            }
        }

        private void FREQ_PER_1V_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 6 || Measurement_Selected == 7)
            {
                SerialWriteQueue.Add(FREQ_PER_Range_Indicator(2));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Frequency/Period Range set to 1V.", 5);
            }
            else
            {
                insert_Log("Cannot set Frequency/Period Range when Frequency/Period Measurement is not selected.", 2);
            }
        }

        private void FREQ_PER_10V_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 6 || Measurement_Selected == 7)
            {
                SerialWriteQueue.Add(FREQ_PER_Range_Indicator(3));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Frequency/Period Range set to 10V.", 5);
            }
            else
            {
                insert_Log("Cannot set Frequency/Period Range when Frequency/Period Measurement is not selected.", 2);
            }
        }

        private void FREQ_PER_100V_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 6 || Measurement_Selected == 7)
            {
                SerialWriteQueue.Add(FREQ_PER_Range_Indicator(4));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Frequency/Period Range set to 100V.", 5);
            }
            else
            {
                insert_Log("Cannot set Frequency/Period Range when Frequency/Period Measurement is not selected.", 2);
            }
        }

        private void FREQ_PER_1000V_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 6 || Measurement_Selected == 7)
            {
                SerialWriteQueue.Add(FREQ_PER_Range_Indicator(5));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Frequency/Period Range set to 1000V.", 5);
            }
            else
            {
                insert_Log("Cannot set Frequency/Period Range when Frequency/Period Measurement is not selected.", 2);
            }
        }

        private string FREQ_PER_Range_Indicator(int Range)
        {
            string RangeCommand = "";
            if (Range == 0)
            {
                FREQ_PER_Auto_Border.BorderBrush = Selected;
                if (Measurement_Selected == 6)
                {
                    RangeCommand = "FREQuency:VOLTage:RANGe:AUTO ON";
                }
                else if (Measurement_Selected == 7)
                {
                    RangeCommand = "PERiod:VOLTage:RANGe:AUTO ON";
                }
            }
            else
            {
                FREQ_PER_Auto_Border.BorderBrush = Deselected;
            }
            if (Range == 1)
            {
                FREQ_PER_100mV_Border.BorderBrush = Selected;
                if (Measurement_Selected == 6)
                {
                    RangeCommand = "FREQuency:VOLTage:RANGe 0.1";
                }
                else if (Measurement_Selected == 7)
                {
                    RangeCommand = "PERiod:VOLTage:RANGe 0.1";
                }
            }
            else
            {
                FREQ_PER_100mV_Border.BorderBrush = Deselected;
            }
            if (Range == 2)
            {
                FREQ_PER_1V_Border.BorderBrush = Selected;
                if (Measurement_Selected == 6)
                {
                    RangeCommand = "FREQuency:VOLTage:RANGe 1";
                }
                else if (Measurement_Selected == 7)
                {
                    RangeCommand = "PERiod:VOLTage:RANGe 1";
                }
            }
            else
            {
                FREQ_PER_1V_Border.BorderBrush = Deselected;
            }
            if (Range == 3)
            {
                FREQ_PER_10V_Border.BorderBrush = Selected;
                if (Measurement_Selected == 6)
                {
                    RangeCommand = "FREQuency:VOLTage:RANGe 10";
                }
                else if (Measurement_Selected == 7)
                {
                    RangeCommand = "PERiod:VOLTage:RANGe 10";
                }
            }
            else
            {
                FREQ_PER_10V_Border.BorderBrush = Deselected;
            }
            if (Range == 4)
            {
                FREQ_PER_100V_Border.BorderBrush = Selected;
                if (Measurement_Selected == 6)
                {
                    RangeCommand = "FREQuency:VOLTage:RANGe 100";
                }
                else if (Measurement_Selected == 7)
                {
                    RangeCommand = "PERiod:VOLTage:RANGe 100";
                }
            }
            else
            {
                FREQ_PER_100V_Border.BorderBrush = Deselected;
            }
            if (Range == 5)
            {
                FREQ_PER_1000V_Border.BorderBrush = Selected;
                if (Measurement_Selected == 6)
                {
                    RangeCommand = "FREQuency:VOLTage:RANGe 1000";
                }
                else if (Measurement_Selected == 7)
                {
                    RangeCommand = "PERiod:VOLTage:RANGe 1000";
                }
            }
            else
            {
                FREQ_PER_1000V_Border.BorderBrush = Deselected;
            }
            return RangeCommand;
        }

        //------------------------------------FREQ/CONT-----------------------------------------------

        //----------------------------------N Digits------------------------------------------------------

        private void Res_Max_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0 || Measurement_Selected == 1 || Measurement_Selected == 2 || Measurement_Selected == 3 || Measurement_Selected == 4 || Measurement_Selected == 5)
            {
                SerialWriteQueue.Add(Resolution_Indicator(0));
                if (Measurement_Selected == 0 || Measurement_Selected == 1 || Measurement_Selected == 4 || Measurement_Selected == 5)
                {
                    NPLC_Indicator(0); //NPLC 0.02
                }
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Resolution set to Max (Less N Digits).", 5);
            }
            else
            {
                insert_Log("Resolution only valid for: DCV DCI ACV ACI 2/4 Wire Ω", 2);
            }
        }

        private void Res_Min_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0 || Measurement_Selected == 1 || Measurement_Selected == 2 || Measurement_Selected == 3 || Measurement_Selected == 4 || Measurement_Selected == 5)
            {
                SerialWriteQueue.Add(Resolution_Indicator(1));
                if (Measurement_Selected == 0 || Measurement_Selected == 1 || Measurement_Selected == 4 || Measurement_Selected == 5)
                {
                    NPLC_Indicator(4); //NPLC 100
                }
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Resolution set to Min (More N Digits).", 5);
            }
            else
            {
                insert_Log("Resolution only valid for: DCV DCI ACV ACI 2/4 Wire Ω", 2);
            }
        }

        private void Digit_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0 || Measurement_Selected == 1 || Measurement_Selected == 2 || Measurement_Selected == 3 || Measurement_Selected == 4 || Measurement_Selected == 5)
            {
                SerialWriteQueue.Add("RES Query");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Resolution Query Command Send.", 5);
            }
            else
            {
                insert_Log("Resolution only valid for: DCV DCI ACV ACI 2/4 Wire Ω", 2);
            }
        }

        private void Resolution_Set_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0 || Measurement_Selected == 1 || Measurement_Selected == 2 || Measurement_Selected == 3 || Measurement_Selected == 4 || Measurement_Selected == 5)
            {
                (bool isValid_Num, double Resolution_Value) = Text_Num(Resolution_Set_Text.Text, false, false);
                if (isValid_Num == true)
                {
                    if (Resolution_Value > 0)
                    {
                        string Resolution_command = Measurement_Resolution_Value(Resolution_Value);
                        if (Resolution_command != "null")
                        {
                            SerialWriteQueue.Add(Resolution_command);
                            lockControls();
                            isUserSendCommand = true;
                            Speedup_Interval();
                            insert_Log("Set Resolution Value Command Send. " + Resolution_command, 5);
                        }
                        else
                        {
                            insert_Log("Could not set specified Resolution value.", 2);
                        }
                    }
                    else
                    {
                        insert_Log("Set Resolution Value must be greater than 0.", 2);
                    }
                }
            }
            else
            {
                insert_Log("Resolution only valid for: DCV DCI ACV ACI 2/4 Wire Ω", 2);
            }
        }

        private string Measurement_Resolution_Value(double Resolution_value)
        {
            switch (Measurement_Selected)
            {
                case 0: //DCV
                    if (VDC_Auto_Border.BorderBrush.ToString() == "#FFFFFFFF")
                    {
                        return ("VOLTage:DC:RESolution " + (decimal)Resolution_value).Trim();
                    }
                    else
                    {
                        insert_Log("DCV: Cannot set Resolution value, Manual Range not set.", 2);
                        return "null";
                    }
                case 1: //DCI
                    if (ADC_Auto_Border.BorderBrush.ToString() == "#FFFFFFFF")
                    {
                        return ("CURRent:DC:RESolution " + (decimal)Resolution_value).Trim();
                    }
                    else
                    {
                        insert_Log("DCI: Cannot set Resolution value, Manual Range not set.", 2);
                        return "null";
                    }
                case 2: //ACV
                    if (VAC_Auto_Border.BorderBrush.ToString() == "#FFFFFFFF")
                    {
                        return ("VOLTage:AC:RESolution " + (decimal)Resolution_value).Trim();
                    }
                    else
                    {
                        insert_Log("ACV: Cannot set Resolution value, Manual Range not set.", 2);
                        return "null";
                    }
                case 3: //ACI
                    if (AAC_Auto_Border.BorderBrush.ToString() == "#FFFFFFFF")
                    {
                        return ("CURRent:AC:RESolution " + (decimal)Resolution_value).Trim();
                    }
                    else
                    {
                        insert_Log("ACI: Cannot set Resolution value, Manual Range not set.", 2);
                        return "null";
                    }
                case 4: //2 Ohms
                    if (Ohms_Auto_Border.BorderBrush.ToString() == "#FFFFFFFF")
                    {
                        return ("RESistance:RESolution " + (decimal)Resolution_value).Trim();
                    }
                    else
                    {
                        insert_Log("2W Ω: Cannot set Resolution value, Manual Range not set.", 2);
                        return "null";
                    }
                case 5: //4 Ohms
                    if (Ohms_Auto_Border.BorderBrush.ToString() == "#FFFFFFFF")
                    {
                        return ("FRESistance:RESolution " + (decimal)Resolution_value).Trim();
                    }
                    else
                    {
                        insert_Log("4W Ω: Cannot set Resolution value, Manual Range not set.", 2);
                        return "null";
                    }
                default:
                    return "null";
            }
        }

        private string Resolution_Indicator(int Select)
        {
            string NDIGIT_Select = "RESolution MIN";
            if (Select == 0)
            {
                Res_Max_Border.BorderBrush = Selected;
                NDIGIT_Select = "RESolution MAX";
            }
            else
            {
                Res_Max_Border.BorderBrush = Deselected;
            }
            if (Select == 1)
            {
                Res_Min_Border.BorderBrush = Selected;
                NDIGIT_Select = "RESolution MIN";
            }
            else
            {
                Res_Min_Border.BorderBrush = Deselected;
            }
            return NDIGIT_Select;
        }
        //----------------------------------N Digits------------------------------------------------------

        //----------------------------------NPLC------------------------------------------------------
        private void NPLC_002_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0 || Measurement_Selected == 1 || Measurement_Selected == 4 || Measurement_Selected == 5)
            {
                NPLC_Indicator(0);
                Resolution_Indicator(9); //Nothing Selected
                SerialWriteQueue.Add(Measurement_NPLC_Value(0.02));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("NPLC set to 0.002", 5);
            }
            else
            {
                insert_Log("NPLC only valid for: DCV DCI 2/4 Wire Ω", 2);
            }
        }

        private void NPLC_02_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0 || Measurement_Selected == 1 || Measurement_Selected == 4 || Measurement_Selected == 5)
            {
                NPLC_Indicator(1);
                Resolution_Indicator(9); //Nothing Selected
                SerialWriteQueue.Add(Measurement_NPLC_Value(0.2));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("NPLC set to 0.02", 5);
            }
            else
            {
                insert_Log("NPLC only valid for: DCV DCI 2/4 Wire Ω", 2);
            }
        }

        private void NPLC_1_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0 || Measurement_Selected == 1 || Measurement_Selected == 4 || Measurement_Selected == 5)
            {
                NPLC_Indicator(2);
                Resolution_Indicator(9); //Nothing Selected
                SerialWriteQueue.Add(Measurement_NPLC_Value(1));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("NPLC set to 1", 5);
            }
            else
            {
                insert_Log("NPLC only valid for: DCV DCI 2/4 Wire Ω", 2);
            }
        }

        private void NPLC_10_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0 || Measurement_Selected == 1 || Measurement_Selected == 4 || Measurement_Selected == 5)
            {
                NPLC_Indicator(3);
                Resolution_Indicator(9); //Nothing Selected
                SerialWriteQueue.Add(Measurement_NPLC_Value(10));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("NPLC set to 10", 5);
            }
            else
            {
                insert_Log("NPLC only valid for: DCV DCI 2/4 Wire Ω", 2);
            }
        }

        private void NPLC_100_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0 || Measurement_Selected == 1 || Measurement_Selected == 4 || Measurement_Selected == 5)
            {
                NPLC_Indicator(4);
                Resolution_Indicator(9); //Nothing Selected
                SerialWriteQueue.Add(Measurement_NPLC_Value(100));
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("NPLC set to 100", 5);
            }
            else
            {
                insert_Log("NPLC only valid for: DCV DCI 2/4 Wire Ω", 2);
            }
        }

        private void NPLC_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0 || Measurement_Selected == 1 || Measurement_Selected == 4 || Measurement_Selected == 5)
            {
                SerialWriteQueue.Add("NPLC Query");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("NPLC Query Command Send.", 5);
            }
            else
            {
                insert_Log("NPLC only valid for: DCV DCI 2/4 Wire Ω", 2);
            }
        }

        private void NPLC_Indicator(int Select)
        {
            if (Select == 0)
            {
                NPLC_002_Border.BorderBrush = Selected;
            }
            else
            {
                NPLC_002_Border.BorderBrush = Deselected;
            }
            if (Select == 1)
            {
                NPLC_02_Border.BorderBrush = Selected;
            }
            else
            {
                NPLC_02_Border.BorderBrush = Deselected;
            }
            if (Select == 2)
            {
                NPLC_1_Border.BorderBrush = Selected;
            }
            else
            {
                NPLC_1_Border.BorderBrush = Deselected;
            }
            if (Select == 3)
            {
                NPLC_10_Border.BorderBrush = Selected;
            }
            else
            {
                NPLC_10_Border.BorderBrush = Deselected;
            }
            if (Select == 4)
            {
                NPLC_100_Border.BorderBrush = Selected;
            }
            else
            {
                NPLC_100_Border.BorderBrush = Deselected;
            }
        }

        private string Measurement_NPLC_Value(double NPLC_value)
        {
            switch (Measurement_Selected)
            {
                case 0: //DCV
                    insert_Log("DCV: NPLC set to " + NPLC_value, 5);
                    return "VOLTage:DC:NPLCycles " + NPLC_value;
                case 1: //DCI
                    insert_Log("DCI: NPLC set to " + NPLC_value, 5);
                    return "CURRent:DC:NPLCycles " + NPLC_value;
                case 4: //2 Ohms
                    insert_Log("2W Ω: NPLC set to " + NPLC_value, 5);
                    return "RESistance:NPLCycles " + NPLC_value;
                case 5: //4 Ohms
                    insert_Log("4W Ω: NPLC set to " + NPLC_value, 5);
                    return "FRESistance:NPLCycles " + NPLC_value;
                default:
                    return "null";
            }
        }
        //----------------------------------NPLC------------------------------------------------------

        //----------------------------------Trigger------------------------------------------------------

        private void Trigger_Immediate_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add(Trigger_Indicator(0));
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Trigger set to Immediate.", 5);
        }

        private void Trigger_External_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add(Trigger_Indicator(1));
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Trigger set to External.", 5);
        }

        private void Trigger_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("TRIG Query");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Trigger Query Command Send.", 5);
        }

        private string Trigger_Indicator(int Select)
        {
            string Trigger_Select = "TRIGger:SOURce IMMediate";
            if (Select == 0)
            {
                Trigger_Immediate_Border.BorderBrush = Selected;
                Trigger_Select = "TRIGger:SOURce IMMediate";
            }
            else
            {
                Trigger_Immediate_Border.BorderBrush = Deselected;
            }
            if (Select == 1)
            {
                Trigger_External_Border.BorderBrush = Selected;
                Trigger_Select = "TRIGger:SOURce EXTernal";
            }
            else
            {
                Trigger_External_Border.BorderBrush = Deselected;
            }
            return Trigger_Select;
        }

        private void Trig_Delay_0_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("TRIGger:DELay 0");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Trigger Delay set to 0 Command Send.", 5);
        }

        private void Trig_Delay_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("TRIGger:DELay?");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Trigger Delay Value Query Command Send.", 5);
        }

        private void Trig_Delay_ON_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("TRIGger:DELay:AUTO ON");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Trigger Auto Delay ON Command Send.", 5);
        }

        private void Trig_Delay_ON_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("TRIGger:DELay:AUTO?");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Trigger Auto Delay Status Query Command Send.", 5);
        }

        //----------------------------------Trigger------------------------------------------------------

        //----------------------------------Range Query--------------------------------------------------

        private void Range_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0 || Measurement_Selected == 1 || Measurement_Selected == 2 || Measurement_Selected == 3 || Measurement_Selected == 4 || Measurement_Selected == 5 || Measurement_Selected == 6 || Measurement_Selected == 7)
            {
                SerialWriteQueue.Add(Range_Query_Command());
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Range Query Command Send.", 5);
            }
            else
            {
                insert_Log("Range Query only valid for: DCV DCI ACV ACI 2/4 Wire Ω Freq Period", 2);
            }
        }

        private string Range_Query_Command()
        {
            switch (Measurement_Selected)
            {
                case 0: //DCV
                    return "VOLTage:DC:RANGe?";
                case 1: //DCI
                    return "CURRent:DC:RANGe?";
                case 2: //ACV
                    return "VOLTage:AC:RANGe?";
                case 3: //ACI
                    return "CURRent:AC:RANGe?";
                case 4: //2 OHMS
                    return "RESistance:RANGe?";
                case 5: //4 OHMS
                    return "FRESistance:RANGe?";
                case 6: //FREQ
                    return "FREQuency:VOLTage:RANGe?";
                case 7: //PER
                    return "PERiod:VOLTage:RANGe?";
                default:
                    return "VOLTage:DC:RANGe?";
            }
        }

        private void Range_Auto_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0 || Measurement_Selected == 1 || Measurement_Selected == 2 || Measurement_Selected == 3 || Measurement_Selected == 4 || Measurement_Selected == 5 || Measurement_Selected == 6 || Measurement_Selected == 7)
            {
                SerialWriteQueue.Add(Range_Auto_Query_Command());
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Auto Range Query Command Send.", 5);
            }
            else
            {
                insert_Log("Auto Range Query only valid for: DCV DCI ACV ACI 2/4 Wire Ω Freq Period", 2);
            }
        }

        private string Range_Auto_Query_Command()
        {
            switch (Measurement_Selected)
            {
                case 0: //DCV
                    return "VOLTage:DC:RANGe:AUTO?";
                case 1: //DCI
                    return "CURRent:DC:RANGe:AUTO?";
                case 2: //ACV
                    return "VOLTage:AC:RANGe:AUTO?";
                case 3: //ACI
                    return "CURRent:AC:RANGe:AUTO?";
                case 4: //2 OHMS
                    return "RESistance:RANGe:AUTO?";
                case 5: //4 OHMS
                    return "FRESistance:RANGe:AUTO?";
                case 6: //FREQ
                    return "FREQuency:VOLTage:RANGe:AUTO?";
                case 7: //PER
                    return "PERiod:VOLTage:RANGe:AUTO?";
                default:
                    return "VOLTage:DC:RANGe:AUTO?";
            }
        }


        //-----------------------------------------------------------------------------------------------

        //--------------------------------------------Meas Queries-------------------------------

        private void Meas_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("FUNCtion?");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Currently Selected Measurement Query Send.", 5);
        }

        private void Meas_Config_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("CONFigure?");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Measurement Config Query Send. <Measurement>: Range, Resolution.", 5);
        }

        //-----------------------------------------------------------------------------------------------

        //--------------------------------------------Null Function--------------------------------------

        private void Null_On_Button_Click(object sender, RoutedEventArgs e)
        {
            if (new int[] { 0, 1, 2, 3, 4, 5, 6, 7 }.Contains(Measurement_Selected))
            {
                SerialWriteQueue.Add("NULL ON");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Null On Math Function Command Send.", 5);
            }
            else
            {
                insert_Log("Null Math Function is only available for DCV, DCI, ACV, ACI, 2/4 Wire Ohms, Freq, Period.", 2);
            }
        }

        private void Null_Off_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            if (new int[] { 0, 1, 2, 3, 4, 5, 6, 7 }.Contains(Measurement_Selected))
            {
                SerialWriteQueue.Add("NULL OFF");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Null Off Math Function Command Send.", 5);
            }
            else
            {
                insert_Log("Null Math Function is only available for DCV, DCI, ACV, ACI, 2/4 Wire Ohms, Freq, Period.", 2);
            }
        }

        private void Null_Status_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            if (new int[] { 0, 1, 2, 3, 4, 5, 6, 7 }.Contains(Measurement_Selected))
            {
                SerialWriteQueue.Add("NULL?");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Null Math Function Status Command Send.", 5);
            }
            else
            {
                insert_Log("Null Math Function is only available for DCV, DCI, ACV, ACI, 2/4 Wire Ohms, Freq, Period.", 2);
            }
        }

        private void Null_Set_Button_Click(object sender, RoutedEventArgs e)
        {
            if (new int[] { 0, 1, 2, 3, 4, 5, 6, 7 }.Contains(Measurement_Selected))
            {
                (bool isValid_Num, double Null_Value) = Text_Num(Null_Value_Text_Input.Text, false, false);
                if (isValid_Num == true)
                {
                    this.Null_Value = Null_Value;
                    SerialWriteQueue.Add("NULL_SET");
                    lockControls();
                    isUserSendCommand = true;
                    Speedup_Interval();
                    insert_Log("Set Null Value Command Send. Null Value: " + (decimal)Null_Value, 5);
                }
                else
                {
                    insert_Log("Set Null Value must be a real number. Try again.", 2);
                }
            }
            else
            {
                insert_Log("Null Math Function is only available for DCV, DCI, ACV, ACI, 2/4 Wire Ohms, Freq, Period.", 2);
            }
        }

        private void Null_Query_Set_Value_Button_Click(object sender, RoutedEventArgs e)
        {
            if (new int[] { 0, 1, 2, 3, 4, 5, 6, 7 }.Contains(Measurement_Selected))
            {
                SerialWriteQueue.Add("NULL_SET_QUERY");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Query Null Value Command Send.", 5);
            }
            else
            {
                insert_Log("Null Math Function is only available for DCV, DCI, ACV, ACI, 2/4 Wire Ohms, Freq, Period.", 2);
            }
        }

        //------------------------------------------------------------------------------------------------

        //------------------------------------Auto Zero Query---------------------------------------------

        private void AZ_On_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0 || Measurement_Selected == 1 || Measurement_Selected == 4)
            {
                SerialWriteQueue.Add("ZERO:AUTO ON");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Auto Zero On Command Send.", 5);
            }
            else
            {
                insert_Log("Auto Zero only applies to DCV DCI 2W Ω", 2);
            }
        }

        private void AZ_Off_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0 || Measurement_Selected == 1 || Measurement_Selected == 4)
            {
                SerialWriteQueue.Add("ZERO:AUTO OFF");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Auto Zero Off Command Send.", 5);
            }
            else
            {
                insert_Log("Auto Zero only applies to DCV DCI 2W Ω", 2);
            }
        }

        private void AZ_Once_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0 || Measurement_Selected == 1 || Measurement_Selected == 4)
            {
                SerialWriteQueue.Add("ZERO:AUTO ONCE");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Auto Zero Once Command Send.", 5);
            }
            else
            {
                insert_Log("Auto Zero only applies to DCV DCI 2W Ω", 2);
            }
        }

        private void AZ_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0 || Measurement_Selected == 1 || Measurement_Selected == 4)
            {
                SerialWriteQueue.Add("ZERO:AUTO?");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Auto Zero Query Command Send.", 5);
            }
            else
            {
                insert_Log("Auto Zero only applies to DCV DCI 2W Ω", 2);
            }
        }

        //------------------------------------------------------------------------------------------------

        //------------------------------------Frequency Aperture---------------------------------------------

        private void FREQ_APER_001_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 6)
            {
                SerialWriteQueue.Add("FREQuency:APERture 0.01");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Frequency Aperture set to 0.01", 5);
            }
            else
            {
                insert_Log("Cannot set Frequency Aperture when Frequency Measurement not selected.", 2);
            }
        }

        private void FREQ_APER_01_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 6)
            {
                SerialWriteQueue.Add("FREQuency:APERture 0.1");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Frequency Aperture set to 0.1", 5);
            }
            else
            {
                insert_Log("Cannot set Frequency Aperture when Frequency Measurement not selected.", 2);
            }
        }

        private void FREQ_APER_1_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 6)
            {
                SerialWriteQueue.Add("FREQuency:APERture 1");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Frequency Aperture set to 1", 5);
            }
            else
            {
                insert_Log("Cannot set Frequency Aperture when Frequency Measurement not selected.", 2);
            }
        }

        private void FREQ_APER_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 6)
            {
                SerialWriteQueue.Add("FREQuency:APERture?");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Frequency Aperture Query Command Send.", 5);
            }
            else
            {
                insert_Log("Frequency Aperture Query command cannot be send when Frequency Measurement not selected.", 2);
            }
        }

        //------------------------------------------------------------------------------------------------

        //------------------------------------Frequency Aperture---------------------------------------------

        private void PER_APER_001_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 7)
            {
                SerialWriteQueue.Add("PERiod:APERture 0.01");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Period Aperture set to 0.01", 5);
            }
            else
            {
                insert_Log("Cannot set Period Aperture when Period Measurement not selected.", 2);
            }
        }

        private void PER_APER_01_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 7)
            {
                SerialWriteQueue.Add("PERiod:APERture 0.1");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Period Aperture set to 0.1", 5);
            }
            else
            {
                insert_Log("Cannot set Period Aperture when Period Measurement not selected.", 2);
            }
        }

        private void PER_APER_1_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 7)
            {
                SerialWriteQueue.Add("PERiod:APERture 1");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Period Aperture set to 1", 5);
            }
            else
            {
                insert_Log("Cannot set Period Aperture when Period Measurement not selected.", 2);
            }
        }

        private void PER_APER_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 7)
            {
                SerialWriteQueue.Add("PERiod:APERture?");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Period Aperture Query Command Send.", 5);
            }
            else
            {
                insert_Log("Period Aperture Query command cannot be send when Period Measurement not selected.", 2);
            }
        }

        //------------------------------------------------------------------------------------------------

        //------------------------------------DETector BANDwidth---------------------------------------------

        private void DET_BAND_3_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 2 || Measurement_Selected == 3)
            {
                SerialWriteQueue.Add("DETector:BANDwidth 3");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Detector Bandwidth set to 3", 5);
            }
            else
            {
                insert_Log("Cannot set Detector Bandwidth when ACV or ACI Measurement not selected.", 2);
            }
        }

        private void DET_BAND_20_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 2 || Measurement_Selected == 3)
            {
                SerialWriteQueue.Add("DETector:BANDwidth 20");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Detector Bandwidth set to 20", 5);
            }
            else
            {
                insert_Log("Cannot set Detector Bandwidth when ACV or ACI Measurement not selected.", 2);
            }
        }

        private void DET_BAND_200_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 2 || Measurement_Selected == 3)
            {
                SerialWriteQueue.Add("DETector:BANDwidth 200");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Detector Bandwidth set to 200", 5);
            }
            else
            {
                insert_Log("Cannot set Detector Bandwidth when ACV or ACI Measurement not selected.", 2);
            }
        }

        private void DET_BAND_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 2 || Measurement_Selected == 3)
            {
                SerialWriteQueue.Add("DETector:BANDwidth?");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Detector Bandwidth Query Command Send.", 5);
            }
            else
            {
                insert_Log("Detector Bandwidth Query command cannot be send when ACV or ACI Measurement not selected.", 2);
            }
        }

        //------------------------------------------------------------------------------------------------

        //------------------------------------Impedance---------------------------------------------

        private void Impedance_On_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0)
            {
                SerialWriteQueue.Add("INPut:IMPedance:AUTO ON");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Input Impedance set to On.", 5);
            }
            else
            {
                insert_Log("Cannot set Input Impedance when DCV Measurement not selected.", 2);
            }
        }

        private void Impedance_Off_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0)
            {
                SerialWriteQueue.Add("INPut:IMPedance:AUTO OFF");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Input Impedance set to Off.", 5);
            }
            else
            {
                insert_Log("Cannot set Input Impedance when DCV Measurement not selected.", 2);
            }
        }


        private void Impedance_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Measurement_Selected == 0)
            {
                SerialWriteQueue.Add("INPut:IMPedance:AUTO?");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Input Impedance Query Command Send.", 5);
            }
            else
            {
                insert_Log("Input Impedance Query command cannot be send when DCV Measurement not selected.", 2);
            }
        }

        //------------------------------------------------------------------------------------------------

        //------------------------------------Display---------------------------------------------

        private void Disp_On_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("DISPlay ON");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Display set to On.", 5);
        }

        private void Disp_Off_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("DISPlay OFF");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Display set to Off.", 5);
        }

        private void Disp_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("DISPlay?");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Display Query Command Send.", 5);
        }

        private void Disp_MSG_Set_Button_Click(object sender, RoutedEventArgs e)
        {
            Regex Characters_Allowed = new Regex("^[a-zA-Z0-9]*$");
            string Display_Message = Display_Set_Text.Text.Trim();
            if (Characters_Allowed.IsMatch(Display_Message))
            {
                SerialWriteQueue.Add("DISPlay:TEXT " + "'" + Display_Message + "'");
                lockControls();
                isUserSendCommand = true;
                Speedup_Interval();
                insert_Log("Display Text set.", 5);
            }
            else
            {
                insert_Log("Front Panel Display Text Not Set. Message must be alphanumeric.", 2);
            }
        }

        private void Disp_MSG_Clear_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("DISPlay:TEXT:CLEar");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Display Message Cleared.", 5);
        }

        //------------------------------------------------------------------------------------------------

        //------------------------------------Queries---------------------------------------------------

        private void ERROR_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("SYSTem:ERRor?");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Get a Single Error Query Command Send.", 5);
        }

        private void TERM_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("ROUTe:TERMinals?");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Input Terminal Query Command Send.", 5);
        }

        private void BEEP_Once_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("SYSTem:BEEPer");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Beep Once Command Send.", 5);
        }

        private void IDN_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("*IDN?");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Get Device Info Query Send.", 5);
        }

        private void STB_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("*STB?");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Status Byte Query Command Send.", 5);
        }

        private void SCPI_VER_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("SYSTem:VERSion?");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Get SCPI VErsion Query Command Send.", 5);
        }

        private void CLS_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("*CLS");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Device Clear Command Send.", 5);
        }

        //------------------------------------------------------------------------------------------------

        //------------------------------------------------Calibration Query-------------------------------

        private void CAL_NUM_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("CALibration:COUNt?");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Calibration Count Query Command Send.", 5);
        }

        private void CAL_SEC_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("CALibration:SECure:STATe?");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Calibration Secure State Query Command Send.", 5);
        }

        private void CAL_MSG_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("CALibration:STRing?");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Calibration Stored Message Query COmmand Send.", 5);
        }

        private void CAL_VAL_Query_Button_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("CALibration:VALue?");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
            insert_Log("Calibration Value Query Command Send.", 5);
        }

        //------------------------------------------------------------------------------------------------

        //----------------------------Speech Setup-------------------------------------------

        private void Speech_Continuous_Set_Button_Click(object sender, RoutedEventArgs e)
        {
            if (isSpeechActive == 1)
            {
                (bool isNum, double value) = isNumber(Speech_Continuous_Value.Text);
                if (isNum == true)
                {
                    if (value > 0)
                    {
                        Interlocked.Exchange(ref isSpeechContinuous, 1);
                        Speech_Measurement_Interval.Interval = (value * 60000);
                        Continuous_Selector(0);
                        insert_Log("Continuously voice measurement every " + value + " minutes.", 0);
                        Speech_Measurement_Interval.Start();
                    }
                    else
                    {
                        insert_Log("Continuous voice value must be a positive number.", 1);
                    }
                }
                else
                {
                    insert_Log("Continuous voice value must be a positive number.", 1);
                }
            }
            else
            {
                insert_Log("The Speech Synthesizer is not enabled. Enable it from Speech Menu.", 2);
            }
        }

        private void Speech_Continuous_Clear_Button_Click(object sender, RoutedEventArgs e)
        {
            if (isSpeechActive == 1)
            {
                Interlocked.Exchange(ref isSpeechContinuous, 0);
                Speech_Continuous_Value.Text = string.Empty;
                Continuous_Selector(1);
                insert_Log("Continuous voice measurement is cleared.", 0);
            }
            else
            {
                insert_Log("The Speech Synthesizer is not enabled. Enable it from Speech Menu.", 2);
            }

        }

        private void Continuous_Selector(int status)
        {
            if (status == 0)
            {
                Speech_Continuous_Set_Border.BorderBrush = Selected;
            }
            else
            {
                Speech_Continuous_Set_Border.BorderBrush = Deselected;
            }
            if (status == 1)
            {
                Speech_Continuous_Clear_Border.BorderBrush = Selected;
            }
            else
            {
                Speech_Continuous_Clear_Border.BorderBrush = Deselected;
            }
        }

        private void Speech_MIN_Button_Click(object sender, RoutedEventArgs e)
        {
            if (isSpeechActive == 1)
            {
                (bool isNum, double value) = isNumber(Speech_MIN_Value.Text);
                if (isNum == true)
                {
                    Interlocked.Exchange(ref Speech_min_value, value);
                    Interlocked.Exchange(ref isSpeechMIN, 1);
                    MIN_Selector(0);
                    insert_Log("Voice measurement less than " + value, 0);
                    Speech_MIN_Max.Start();
                }
                else
                {
                    insert_Log("MIN voice value must be a number.", 1);
                }
            }
            else
            {
                insert_Log("The Speech Synthesizer is not enabled. Enable it from Speech Menu.", 2);
            }
        }

        private void Speech_MIN_Clear_Button_Click(object sender, RoutedEventArgs e)
        {
            if (isSpeechActive == 1)
            {
                Interlocked.Exchange(ref isSpeechMIN, 0);
                Interlocked.Exchange(ref Speech_min_value, 0);
                Speech_MIN_Value.Text = string.Empty;
                MIN_Selector(1);
                insert_Log("MIN voice measurement is cleared.", 0);
            }
            else
            {
                insert_Log("The Speech Synthesizer is not enabled. Enable it from Speech Menu.", 2);
            }
        }

        private void MIN_Selector(int status)
        {
            if (status == 0)
            {
                Speech_MIN_Border.BorderBrush = Selected;
            }
            else
            {
                Speech_MIN_Border.BorderBrush = Deselected;
            }
            if (status == 1)
            {
                Speech_MIN_Clear_Border.BorderBrush = Selected;
            }
            else
            {
                Speech_MIN_Clear_Border.BorderBrush = Deselected;
            }
        }

        private void Speech_MAX_Button_Click(object sender, RoutedEventArgs e)
        {
            if (isSpeechActive == 1)
            {
                (bool isNum, double value) = isNumber(Speech_MAX_Value.Text);
                if (isNum == true)
                {
                    Interlocked.Exchange(ref Speech_max_value, value);
                    Interlocked.Exchange(ref isSpeechMAX, 1);
                    MAX_Selector(0);
                    insert_Log("Voice measurement greater than " + value, 0);
                    Speech_MIN_Max.Start();
                }
                else
                {
                    insert_Log("MAX voice value must be a number.", 1);
                }
            }
            else
            {
                insert_Log("The Speech Synthesizer is not enabled. Enable it from Speech Menu.", 2);
            }
        }

        private void Speech_MAX_Clear_Button_Click(object sender, RoutedEventArgs e)
        {
            if (isSpeechActive == 1)
            {
                Interlocked.Exchange(ref isSpeechMAX, 0);
                Interlocked.Exchange(ref Speech_max_value, 0);
                Speech_MAX_Value.Text = string.Empty;
                MAX_Selector(1);
                insert_Log("MAX voice measurement is cleared.", 0);
            }
            else
            {
                insert_Log("The Speech Synthesizer is not enabled. Enable it from Speech Menu.", 2);
            }
        }

        private void MAX_Selector(int status)
        {
            if (status == 0)
            {
                Speech_MAX_Border.BorderBrush = Selected;
            }
            else
            {
                Speech_MAX_Border.BorderBrush = Deselected;
            }
            if (status == 1)
            {
                Speech_MAX_Clear_Border.BorderBrush = Selected;
            }
            else
            {
                Speech_MAX_Clear_Border.BorderBrush = Deselected;
            }
        }

        //-----------------------------------------------------------------------

        //-----------------------------Update Speed Options------------------------------------------

        private void UpdateSpeed_Value_Set_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isNum, double value) = isNumber(UpdateSpeed_Value.Text);
            if (isNum == true)
            {
                if (value > 0)
                {
                    insert_Log("You may need to wait for " + (UpdateSpeed / 1000) + " seconds before your new update speed takes effect.", 2);
                    insert_Log("Update Speed set to " + value + " seconds Command Send.", 5);
                    value = value * 1000;
                    UpdateSpeed = value;
                    UpdateSpeed_Selector(0);
                    isUpdateSpeed_Changed = true;
                }
                else
                {
                    insert_Log("Update Speed must be number greater than 0. Minimum value can be 0.01 seconds.", 1);
                }
            }
            else
            {
                insert_Log("Update Speed must be number.", 1);
            }
        }

        private void UpdateSpeed_Default_Set_Button_Click(object sender, RoutedEventArgs e)
        {
            insert_Log("You may to wait for " + (UpdateSpeed / 1000) + " seconds before your new update speed takes effect.", 2);
            UpdateSpeed = 1000;
            insert_Log("Update Speed set to " + (UpdateSpeed / 1000) + " seconds Command Send.", 5);
            UpdateSpeed_Selector(1);
            isUpdateSpeed_Changed = true;
        }

        private void UpdateSpeed_Fast_Set_Button_Click(object sender, RoutedEventArgs e)
        {
            insert_Log("You may to wait for " + (UpdateSpeed / 1000) + " seconds before your new update speed takes effect.", 2);
            UpdateSpeed = 10;
            insert_Log("Update Speed set to " + (UpdateSpeed / 1000) + " seconds Command Send.", 5);
            UpdateSpeed_Selector(2);
            isUpdateSpeed_Changed = true;
        }

        private void UpdateSpeed_Selector(int status)
        {
            if (status == 0)
            {
                UpdateSpeed_Value_Set_Border.BorderBrush = Selected;
            }
            else
            {
                UpdateSpeed_Value_Set_Border.BorderBrush = Deselected;
            }
            if (status == 1)
            {
                UpdateSpeed_Default_Set_Border.BorderBrush = Selected;
            }
            else
            {
                UpdateSpeed_Default_Set_Border.BorderBrush = Deselected;
            }
            if (status == 2)
            {
                UpdateSpeed_Fast_Set_Border.BorderBrush = Selected;
            }
            else
            {
                UpdateSpeed_Fast_Set_Border.BorderBrush = Deselected;
            }
        }

        //-----------------------------------------------------------------------

        private void Measurement_Green_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Color_Checker(0);
            Measurement_Color("#FF00FF17");

        }

        private void Measurement_Blue_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Color_Checker(1);
            Measurement_Color("#FF00C0FF");
        }

        private void Measurement_Red_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Color_Checker(2);
            Measurement_Color("Red");
        }

        private void Measurement_Yellow_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Color_Checker(3);
            Measurement_Color("#FFFFFF00");
        }

        private void Measurement_Orange_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Color_Checker(4);
            Measurement_Color("DarkOrange");
        }

        private void Measurement_Pink_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Color_Checker(5);
            Measurement_Color("DeepPink");
        }

        private void Measurement_White_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Color_Checker(6);
            Measurement_Color("White");
        }

        private void Measurement_Black_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Color_Checker(7);
            Measurement_Color("Black");
        }

        private void Measurement_Color(string HexValue)
        {
            SolidColorBrush Color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(HexValue));
            Measurement_Value.Foreground = Color;
            Measurement_Scale.Foreground = Color;
            Measurement_Type.Foreground = Color;
        }

        private void Measurement_Color_Checker(int Check)
        {
            if (Check == 0)
            {
                Measurement_Green.IsChecked = true;
            }
            else
            {
                Measurement_Green.IsChecked = false;
            }
            if (Check == 1)
            {
                Measurement_Blue.IsChecked = true;
            }
            else
            {
                Measurement_Blue.IsChecked = false;
            }
            if (Check == 2)
            {
                Measurement_Red.IsChecked = true;
            }
            else
            {
                Measurement_Red.IsChecked = false;
            }
            if (Check == 3)
            {
                Measurement_Yellow.IsChecked = true;
            }
            else
            {
                Measurement_Yellow.IsChecked = false;
            }
            if (Check == 4)
            {
                Measurement_Orange.IsChecked = true;
            }
            else
            {
                Measurement_Orange.IsChecked = false;
            }
            if (Check == 5)
            {
                Measurement_Pink.IsChecked = true;
            }
            else
            {
                Measurement_Pink.IsChecked = false;
            }
            if (Check == 6)
            {
                Measurement_White.IsChecked = true;
            }
            else
            {
                Measurement_White.IsChecked = false;
            }
            if (Check == 7)
            {
                Measurement_Black.IsChecked = true;
            }
            else
            {
                Measurement_Black.IsChecked = false;
            }
        }

        private void MIN_Green_Click(object sender, RoutedEventArgs e)
        {
            MIN_Color_Checker(0);
            MIN_Color("#FF00FF17");
        }

        private void MIN_Blue_Click(object sender, RoutedEventArgs e)
        {
            MIN_Color_Checker(1);
            MIN_Color("#FF00C0FF");
        }

        private void MIN_Red_Click(object sender, RoutedEventArgs e)
        {
            MIN_Color_Checker(2);
            MIN_Color("Red");
        }

        private void MIN_Yellow_Click(object sender, RoutedEventArgs e)
        {
            MIN_Color_Checker(3);
            MIN_Color("#FFFFFF00");
        }

        private void MIN_Orange_Click(object sender, RoutedEventArgs e)
        {
            MIN_Color_Checker(4);
            MIN_Color("DarkOrange");
        }

        private void MIN_Pink_Click(object sender, RoutedEventArgs e)
        {
            MIN_Color_Checker(5);
            MIN_Color("DeepPink");
        }

        private void MIN_White_Click(object sender, RoutedEventArgs e)
        {
            MIN_Color_Checker(6);
            MIN_Color("White");
        }

        private void MIN_Black_Click(object sender, RoutedEventArgs e)
        {
            MIN_Color_Checker(7);
            MIN_Color("Black");
        }

        private void MIN_Color(string HexValue)
        {
            SolidColorBrush Color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(HexValue));
            MIN_Value.Foreground = Color;
            MIN_Scale.Foreground = Color;
            MIN_Type.Foreground = Color;
            MIN_Label.Foreground = Color;
        }

        private void MIN_Color_Checker(int Check)
        {
            if (Check == 0)
            {
                MIN_Green.IsChecked = true;
            }
            else
            {
                MIN_Green.IsChecked = false;
            }
            if (Check == 1)
            {
                MIN_Blue.IsChecked = true;
            }
            else
            {
                MIN_Blue.IsChecked = false;
            }
            if (Check == 2)
            {
                MIN_Red.IsChecked = true;
            }
            else
            {
                MIN_Red.IsChecked = false;
            }
            if (Check == 3)
            {
                MIN_Yellow.IsChecked = true;
            }
            else
            {
                MIN_Yellow.IsChecked = false;
            }
            if (Check == 4)
            {
                MIN_Orange.IsChecked = true;
            }
            else
            {
                MIN_Orange.IsChecked = false;
            }
            if (Check == 5)
            {
                MIN_Pink.IsChecked = true;
            }
            else
            {
                MIN_Pink.IsChecked = false;
            }
            if (Check == 6)
            {
                MIN_White.IsChecked = true;
            }
            else
            {
                MIN_White.IsChecked = false;
            }
            if (Check == 7)
            {
                MIN_Black.IsChecked = true;
            }
            else
            {
                MIN_Black.IsChecked = false;
            }
        }

        private void MAX_Green_Click(object sender, RoutedEventArgs e)
        {
            MAX_Color_Checker(0);
            MAX_Color("#FF00FF17");
        }

        private void MAX_Blue_Click(object sender, RoutedEventArgs e)
        {
            MAX_Color_Checker(1);
            MAX_Color("#FF00C0FF");
        }

        private void MAX_Red_Click(object sender, RoutedEventArgs e)
        {
            MAX_Color_Checker(2);
            MAX_Color("Red");
        }

        private void MAX_Yellow_Click(object sender, RoutedEventArgs e)
        {
            MAX_Color_Checker(3);
            MAX_Color("#FFFFFF00");
        }

        private void MAX_Orange_Click(object sender, RoutedEventArgs e)
        {
            MAX_Color_Checker(4);
            MAX_Color("DarkOrange");
        }

        private void MAX_Pink_Click(object sender, RoutedEventArgs e)
        {
            MAX_Color_Checker(5);
            MAX_Color("DeepPink");
        }

        private void MAX_White_Click(object sender, RoutedEventArgs e)
        {
            MAX_Color_Checker(6);
            MAX_Color("White");
        }

        private void MAX_Black_Click(object sender, RoutedEventArgs e)
        {
            MAX_Color_Checker(7);
            MAX_Color("Black");
        }

        private void MAX_Color(string HexValue)
        {
            SolidColorBrush Color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(HexValue));
            MAX_Value.Foreground = Color;
            MAX_Scale.Foreground = Color;
            MAX_Type.Foreground = Color;
            MAX_Label.Foreground = Color;
        }

        private void MAX_Color_Checker(int Check)
        {
            if (Check == 0)
            {
                MAX_Green.IsChecked = true;
            }
            else
            {
                MAX_Green.IsChecked = false;
            }
            if (Check == 1)
            {
                MAX_Blue.IsChecked = true;
            }
            else
            {
                MAX_Blue.IsChecked = false;
            }
            if (Check == 2)
            {
                MAX_Red.IsChecked = true;
            }
            else
            {
                MAX_Red.IsChecked = false;
            }
            if (Check == 3)
            {
                MAX_Yellow.IsChecked = true;
            }
            else
            {
                MAX_Yellow.IsChecked = false;
            }
            if (Check == 4)
            {
                MAX_Orange.IsChecked = true;
            }
            else
            {
                MAX_Orange.IsChecked = false;
            }
            if (Check == 5)
            {
                MAX_Pink.IsChecked = true;
            }
            else
            {
                MAX_Pink.IsChecked = false;
            }
            if (Check == 6)
            {
                MAX_White.IsChecked = true;
            }
            else
            {
                MAX_White.IsChecked = false;
            }
            if (Check == 7)
            {
                MAX_Black.IsChecked = true;
            }
            else
            {
                MAX_Black.IsChecked = false;
            }
        }

        private void AVG_Green_Click(object sender, RoutedEventArgs e)
        {
            AVG_Color_Checker(0);
            AVG_Color("#FF00FF17");
        }

        private void AVG_Blue_Click(object sender, RoutedEventArgs e)
        {
            AVG_Color_Checker(1);
            AVG_Color("#FF00C0FF");
        }

        private void AVG_Red_Click(object sender, RoutedEventArgs e)
        {
            AVG_Color_Checker(2);
            AVG_Color("Red");
        }

        private void AVG_Yellow_Click(object sender, RoutedEventArgs e)
        {
            AVG_Color_Checker(3);
            AVG_Color("#FFFFFF00");
        }

        private void AVG_Orange_Click(object sender, RoutedEventArgs e)
        {
            AVG_Color_Checker(4);
            AVG_Color("DarkOrange");
        }

        private void AVG_Pink_Click(object sender, RoutedEventArgs e)
        {
            AVG_Color_Checker(5);
            AVG_Color("DeepPink");
        }

        private void AVG_White_Click(object sender, RoutedEventArgs e)
        {
            AVG_Color_Checker(6);
            AVG_Color("White");
        }

        private void AVG_Black_Click(object sender, RoutedEventArgs e)
        {
            AVG_Color_Checker(7);
            AVG_Color("Black");
        }

        private void AVG_Color(string HexValue)
        {
            SolidColorBrush Color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(HexValue));
            AVG_Value.Foreground = Color;
            AVG_Scale.Foreground = Color;
            AVG_Type.Foreground = Color;
            AVG_Label.Foreground = Color;
        }

        private void AVG_Color_Checker(int Check)
        {
            if (Check == 0)
            {
                AVG_Green.IsChecked = true;
            }
            else
            {
                AVG_Green.IsChecked = false;
            }
            if (Check == 1)
            {
                AVG_Blue.IsChecked = true;
            }
            else
            {
                AVG_Blue.IsChecked = false;
            }
            if (Check == 2)
            {
                AVG_Red.IsChecked = true;
            }
            else
            {
                AVG_Red.IsChecked = false;
            }
            if (Check == 3)
            {
                AVG_Yellow.IsChecked = true;
            }
            else
            {
                AVG_Yellow.IsChecked = false;
            }
            if (Check == 4)
            {
                AVG_Orange.IsChecked = true;
            }
            else
            {
                AVG_Orange.IsChecked = false;
            }
            if (Check == 5)
            {
                AVG_Pink.IsChecked = true;
            }
            else
            {
                AVG_Pink.IsChecked = false;
            }
            if (Check == 6)
            {
                AVG_White.IsChecked = true;
            }
            else
            {
                AVG_White.IsChecked = false;
            }
            if (Check == 7)
            {
                AVG_Black.IsChecked = true;
            }
            else
            {
                AVG_Black.IsChecked = false;
            }
        }

        private void Background_White_Click(object sender, RoutedEventArgs e)
        {
            Background_Color_Checker(0);
            Background_Color("White");
        }

        private void Background_Black_Click(object sender, RoutedEventArgs e)
        {
            Background_Color_Checker(1);
            Background_Color("Black");
        }

        private void Background_Color(string HexValue)
        {
            SolidColorBrush Color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(HexValue));
            DisplayPanel_Background.Background = Color;
        }

        private void Background_Color_Checker(int Check)
        {
            if (Check == 0)
            {
                Background_White.IsChecked = true;
            }
            else
            {
                Background_White.IsChecked = false;
            }
            if (Check == 1)
            {
                Background_Black.IsChecked = true;
            }
            else
            {
                Background_Black.IsChecked = false;
            }
        }

        private void Randomize_Display_Colors(object sender, RoutedEventArgs e)
        {
            Random RGB_Value = new Random();
            int Value_Red = RGB_Value.Next(0, 255);
            int Value_Green = RGB_Value.Next(0, 255);
            int Value_Blue = RGB_Value.Next(0, 255);

            Set_Measurement_Color(Value_Red, Value_Green, Value_Blue);
            Set_MIN_Color(Value_Red, Value_Green, Value_Blue);
            Set_MAX_Color(Value_Red, Value_Green, Value_Blue);
            Set_AVG_Color(Value_Red, Value_Green, Value_Blue);

            insert_Log(Value_Red + "," + Value_Green + "," + Value_Blue + "," + "Measurement_Colors_Selected_RGB", 4);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            DataTimer.Stop();
            DataTimer.Dispose();
        }

        private void FSI_Display_Click(object sender, RoutedEventArgs e)
        {
            PSI_Display.IsChecked = false;
            FSI_Display.IsChecked = true;
            Partial_SI_Prefix = false;
            Full_SI_Prefix = true;
        }

        private void PSI_Display_Click(object sender, RoutedEventArgs e)
        {
            PSI_Display.IsChecked = true;
            FSI_Display.IsChecked = false;
            Partial_SI_Prefix = true;
            Full_SI_Prefix = false;
        }

        private void Calculate_AVG_Click(object sender, RoutedEventArgs e)
        {
            if (Calculate_AVG.IsChecked == true)
            {
                Interlocked.Exchange(ref AVG_Calculate, 1);
                insert_Log("Average will be calculated.", 0);
            }
            else
            {
                Interlocked.Exchange(ref AVG_Calculate, 0);
                insert_Log("Average will not be calculated.", 2);
            }
        }

        private void AVG_Res_2_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref avg_resolution, 2);
            insert_Log("Average's resolution set to " + avg_resolution + ".", 0);
            AVG_Res_Selected();
        }

        private void AVG_Res_3_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref avg_resolution, 3);
            insert_Log("Average's resolution set to " + avg_resolution + ".", 0);
            AVG_Res_Selected();
        }

        private void AVG_Res_4_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref avg_resolution, 4);
            insert_Log("Average's resolution set to " + avg_resolution + ".", 0);
            AVG_Res_Selected();
        }

        private void AVG_Res_5_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref avg_resolution, 5);
            insert_Log("Average's resolution set to " + avg_resolution + ".", 0);
            AVG_Res_Selected();
        }

        private void AVG_Res_6_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref avg_resolution, 6);
            insert_Log("Average's resolution set to " + avg_resolution + ".", 0);
            AVG_Res_Selected();
        }

        private void AVG_Res_Selected()
        {
            if (avg_resolution == 2)
            {
                AVG_Res_2.IsChecked = true;
            }
            else
            {
                AVG_Res_2.IsChecked = false;
            }
            if (avg_resolution == 3)
            {
                AVG_Res_3.IsChecked = true;
            }
            else
            {
                AVG_Res_3.IsChecked = false;
            }
            if (avg_resolution == 4)
            {
                AVG_Res_4.IsChecked = true;
            }
            else
            {
                AVG_Res_4.IsChecked = false;
            }
            if (avg_resolution == 5)
            {
                AVG_Res_5.IsChecked = true;
            }
            else
            {
                AVG_Res_5.IsChecked = false;
            }
            if (avg_resolution == 6)
            {
                AVG_Res_6.IsChecked = true;
            }
            else
            {
                AVG_Res_6.IsChecked = false;
            }
        }

        private void Factor_50_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref avg_factor, 50);
            insert_Log("Average's factor set to " + avg_factor + ".", 0);
            AVG_Fac_Selected();
        }

        private void Factor_100_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref avg_factor, 100);
            insert_Log("Average's factor set to " + avg_factor + ".", 0);
            AVG_Fac_Selected();
        }

        private void Factor_200_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref avg_factor, 200);
            insert_Log("Average's factor set to " + avg_factor + ".", 0);
            AVG_Fac_Selected();
        }

        private void Factor_400_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref avg_factor, 400);
            insert_Log("Average's factor set to " + avg_factor + ".", 0);
            AVG_Fac_Selected();
        }

        private void Factor_800_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref avg_factor, 800);
            insert_Log("Average's factor set to " + avg_factor + ".", 0);
            AVG_Fac_Selected();
        }

        private void Factor_1000_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref avg_factor, 1000);
            insert_Log("Average's factor set to " + avg_factor + ".", 0);
            AVG_Fac_Selected();
        }

        private void AVG_Fac_Selected()
        {
            if (avg_factor == 50)
            {
                Factor_50.IsChecked = true;
            }
            else
            {
                Factor_50.IsChecked = false;
            }
            if (avg_factor == 100)
            {
                Factor_100.IsChecked = true;
            }
            else
            {
                Factor_100.IsChecked = false;
            }
            if (avg_factor == 200)
            {
                Factor_200.IsChecked = true;
            }
            else
            {
                Factor_200.IsChecked = false;
            }
            if (avg_factor == 400)
            {
                Factor_400.IsChecked = true;
            }
            else
            {
                Factor_400.IsChecked = false;
            }
            if (avg_factor == 800)
            {
                Factor_800.IsChecked = true;
            }
            else
            {
                Factor_800.IsChecked = false;
            }
            if (avg_factor == 1000)
            {
                Factor_1000.IsChecked = true;
            }
            else
            {
                Factor_1000.IsChecked = false;
            }
        }

        private void Voice_Precision_0_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Speech_Value_Precision, 0);
            insert_Log("Speech's precision is set to " + Speech_Value_Precision + ".", 0);
            Voice_Precision_Selected();
        }

        private void Voice_Precision_1_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Speech_Value_Precision, 1);
            insert_Log("Speech's precision is set to " + Speech_Value_Precision + ".", 0);
            Voice_Precision_Selected();
        }

        private void Voice_Precision_2_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Speech_Value_Precision, 2);
            insert_Log("Speech's precision is set to " + Speech_Value_Precision + ".", 0);
            Voice_Precision_Selected();
        }

        private void Voice_Precision_3_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Speech_Value_Precision, 3);
            insert_Log("Speech's precision is set to " + Speech_Value_Precision + ".", 0);
            Voice_Precision_Selected();
        }

        private void Voice_Precision_4_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Speech_Value_Precision, 4);
            insert_Log("Speech's precision is set to " + Speech_Value_Precision + ".", 0);
            Voice_Precision_Selected();
        }

        private void Voice_Precision_5_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Speech_Value_Precision, 5);
            insert_Log("Speech's precision is set to " + Speech_Value_Precision + ".", 0);
            Voice_Precision_Selected();
        }

        private void Voice_Precision_6_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Speech_Value_Precision, 6);
            insert_Log("Speech's precision is set to " + Speech_Value_Precision + ".", 0);
            Voice_Precision_Selected();
        }

        private void Voice_Precision_7_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Speech_Value_Precision, 7);
            insert_Log("Speech's precision is set to " + Speech_Value_Precision + ".", 0);
            Voice_Precision_Selected();
        }

        private void Voice_Precision_Selected()
        {
            if (Speech_Value_Precision == 0)
            {
                Voice_Precision_0.IsChecked = true;
            }
            else
            {
                Voice_Precision_0.IsChecked = false;
            }
            if (Speech_Value_Precision == 1)
            {
                Voice_Precision_1.IsChecked = true;
            }
            else
            {
                Voice_Precision_1.IsChecked = false;
            }
            if (Speech_Value_Precision == 2)
            {
                Voice_Precision_2.IsChecked = true;
            }
            else
            {
                Voice_Precision_2.IsChecked = false;
            }
            if (Speech_Value_Precision == 3)
            {
                Voice_Precision_3.IsChecked = true;
            }
            else
            {
                Voice_Precision_3.IsChecked = false;
            }
            if (Speech_Value_Precision == 4)
            {
                Voice_Precision_4.IsChecked = true;
            }
            else
            {
                Voice_Precision_4.IsChecked = false;
            }
            if (Speech_Value_Precision == 5)
            {
                Voice_Precision_5.IsChecked = true;
            }
            else
            {
                Voice_Precision_5.IsChecked = false;
            }
            if (Speech_Value_Precision == 6)
            {
                Voice_Precision_6.IsChecked = true;
            }
            else
            {
                Voice_Precision_6.IsChecked = false;
            }
            if (Speech_Value_Precision == 7)
            {
                Voice_Precision_7.IsChecked = true;
            }
            else
            {
                Voice_Precision_7.IsChecked = false;
            }
        }

        //converts a string into a number
        private (bool, double) Text_Num(string text, bool allowNegative, bool isInteger)
        {
            if (isInteger == true)
            {
                bool isValid = int.TryParse(text, out int value);
                if (isValid == true)
                {
                    if (allowNegative == false)
                    {
                        if (value < 0)
                        {
                            return (false, 0);
                        }
                        else
                        {
                            return (true, value);
                        }
                    }
                    else
                    {
                        return (true, value);
                    }
                }
                else
                {
                    return (false, 0);
                }
            }
            else
            {
                bool isValid = double.TryParse(text, out double value);
                if (isValid == true)
                {
                    if (allowNegative == false)
                    {
                        if (value < 0)
                        {
                            return (false, 0);
                        }
                        else
                        {
                            return (true, value);
                        }
                    }
                    else
                    {
                        return (true, value);
                    }
                }
                else
                {
                    return (false, 0);
                }
            }
        }

        private void Main_Window_Closed(object sender, EventArgs e)
        {
            try
            {
                if (Serial_COM_Info.isConnected == true)
                {
                    HP34401A.Close();
                    HP34401A.Dispose();
                }
            }
            catch (Exception)
            {

            }
        }

        private void Local_Exit_Click(object sender, RoutedEventArgs e)
        {
            SerialWriteQueue.Add("LOCAL_EXIT");
            lockControls();
            isUserSendCommand = true;
            Speedup_Interval();
        }

        private void Load_Main_Window_Settings()
        {
            try
            {
                List<String> Config_Lines = new List<string>();
                string Software_Location = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + @"\" + "Settings.txt";
                string[] Config_Parts;
                using (var readFile = new StreamReader(Software_Location))
                {
                    Config_Parts = readFile.ReadLine().Split(',');
                    Set_Measurement_Color(int.Parse(Config_Parts[0]), int.Parse(Config_Parts[1]), int.Parse(Config_Parts[2]));
                    Config_Parts = readFile.ReadLine().Split(',');
                    Set_MIN_Color(int.Parse(Config_Parts[0]), int.Parse(Config_Parts[1]), int.Parse(Config_Parts[2]));
                    Config_Parts = readFile.ReadLine().Split(',');
                    Set_MAX_Color(int.Parse(Config_Parts[0]), int.Parse(Config_Parts[1]), int.Parse(Config_Parts[2]));
                    Config_Parts = readFile.ReadLine().Split(',');
                    Set_AVG_Color(int.Parse(Config_Parts[0]), int.Parse(Config_Parts[1]), int.Parse(Config_Parts[2]));
                    Config_Parts = readFile.ReadLine().Split(',');
                    Initial_Set_Measurement_Prefix(Config_Parts[0].ToUpper().Trim());
                    insert_Log("Settings.txt file loaded.", 0);
                }

            }
            catch (Exception Ex)
            {
                insert_Log(Ex.Message, 2);
                insert_Log("Could not load Settings.txt file, try again.", 2);
            }
        }

        private void Set_Measurement_Color(int Red, int Green, int Blue)
        {
            Measurement_Color_Checker(9);
            if ((Red <= 255 & Red >= 0) & (Green <= 255 & Green >= 0) & (Blue <= 255 & Blue >= 0))
            {
                Measurement_Value.Foreground = new SolidColorBrush(Color.FromArgb(255, (byte)(Red), (byte)(Green), (byte)(Blue)));
                Measurement_Scale.Foreground = new SolidColorBrush(Color.FromArgb(255, (byte)(Red), (byte)(Green), (byte)(Blue)));
                Measurement_Type.Foreground = new SolidColorBrush(Color.FromArgb(255, (byte)(Red), (byte)(Green), (byte)(Blue)));
            }
            else
            {
                insert_Log("Measurement_Value_Color: RGB Values must be between 0 to 255, try again.", 2);
            }
        }

        private void Set_MIN_Color(int Red, int Green, int Blue)
        {
            MIN_Color_Checker(9);
            if ((Red <= 255 & Red >= 0) & (Green <= 255 & Green >= 0) & (Blue <= 255 & Blue >= 0))
            {
                MIN_Value.Foreground = new SolidColorBrush(Color.FromArgb(255, (byte)(Red), (byte)(Green), (byte)(Blue)));
                MIN_Scale.Foreground = new SolidColorBrush(Color.FromArgb(255, (byte)(Red), (byte)(Green), (byte)(Blue)));
                MIN_Type.Foreground = new SolidColorBrush(Color.FromArgb(255, (byte)(Red), (byte)(Green), (byte)(Blue)));
                MIN_Label.Foreground = new SolidColorBrush(Color.FromArgb(255, (byte)(Red), (byte)(Green), (byte)(Blue)));
            }
            else
            {
                insert_Log("MIN_Value_Color: RGB Values must be between 0 to 255, try again.", 2);
            }
        }

        private void Set_MAX_Color(int Red, int Green, int Blue)
        {
            MAX_Color_Checker(9);
            if ((Red <= 255 & Red >= 0) & (Green <= 255 & Green >= 0) & (Blue <= 255 & Blue >= 0))
            {
                MAX_Value.Foreground = new SolidColorBrush(Color.FromArgb(255, (byte)(Red), (byte)(Green), (byte)(Blue)));
                MAX_Scale.Foreground = new SolidColorBrush(Color.FromArgb(255, (byte)(Red), (byte)(Green), (byte)(Blue)));
                MAX_Type.Foreground = new SolidColorBrush(Color.FromArgb(255, (byte)(Red), (byte)(Green), (byte)(Blue)));
                MAX_Label.Foreground = new SolidColorBrush(Color.FromArgb(255, (byte)(Red), (byte)(Green), (byte)(Blue)));
            }
            else
            {
                insert_Log("MAX_Value_Color: RGB Values must be between 0 to 255, try again.", 2);
            }
        }

        private void Set_AVG_Color(int Red, int Green, int Blue)
        {
            AVG_Color_Checker(9);
            if ((Red <= 255 & Red >= 0) & (Green <= 255 & Green >= 0) & (Blue <= 255 & Blue >= 0))
            {
                AVG_Value.Foreground = new SolidColorBrush(Color.FromArgb(255, (byte)(Red), (byte)(Green), (byte)(Blue)));
                AVG_Scale.Foreground = new SolidColorBrush(Color.FromArgb(255, (byte)(Red), (byte)(Green), (byte)(Blue)));
                AVG_Type.Foreground = new SolidColorBrush(Color.FromArgb(255, (byte)(Red), (byte)(Green), (byte)(Blue)));
                AVG_Label.Foreground = new SolidColorBrush(Color.FromArgb(255, (byte)(Red), (byte)(Green), (byte)(Blue)));
            }
            else
            {
                insert_Log("AVG_Value_Color: RGB Values must be between 0 to 255, try again.", 2);
            }
        }

        private void Initial_Set_Measurement_Prefix(string Partial_Prefix)
        {
            if (Partial_Prefix == "TRUE")
            {
                PSI_Display.IsChecked = true;
                FSI_Display.IsChecked = false;
                Partial_SI_Prefix = true;
                Full_SI_Prefix = false;
            }
            else if (Partial_Prefix == "FALSE")
            {
                PSI_Display.IsChecked = false;
                FSI_Display.IsChecked = true;
                Partial_SI_Prefix = false;
                Full_SI_Prefix = true;
            }
            else
            {
                PSI_Display.IsChecked = true;
                FSI_Display.IsChecked = false;
                Partial_SI_Prefix = true;
                Full_SI_Prefix = false;
            }
        }
    }
}
