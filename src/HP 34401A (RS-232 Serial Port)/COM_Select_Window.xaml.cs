using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HP_34401A
{
    /// <summary>
    /// Interaction logic for COM_Select_Window.xaml
    /// </summary>
    public partial class COM_Select_Window : Window
    {
        //Codes for Info Log Color Palette
        int Success_Code = 0;
        int Error_Code = 1;
        int Warning_Code = 2;
        int Config_Code = 3;
        int Message_Code = 4;

        //List of COM Ports stored in this
        List<string> portList;

        //COM Port Information, updated by GUI
        string COM_Port_Name = "";
        int COM_BaudRate_Value = 9600;
        int COM_Parity_Value = 0;
        int COM_StopBits_Value = 1;
        int COM_DataBits_Value = 8;
        int COM_Handshake_Value = 0;
        int COM_WriteTimeout_Value = 4000;
        int COM_ReadTimeout_Value = 9000;
        bool COM_RtsEnable = false;
        bool COM_DtrEnable = false;

        //Save Data Directory
        string folder_Directory;

        public COM_Select_Window()
        {
            InitializeComponent();
            Get_COM_List();
            getSoftwarePath();
            insert_Log("Choose the correct COM port from the list.", Message_Code);
            insert_Log("Click the Connect button when you are ready.", Message_Code);
            Load_COM_Config();
        }

        private void getSoftwarePath()
        {
            try
            {
                folder_Directory = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + @"\" + "Log Data (HP34401A)";
                insert_Log("Test Data will be saved inside the software directory.", Config_Code);
                insert_Log(folder_Directory, Config_Code);
                insert_Log("Click the Select button to select another directory.", Config_Code);
            }
            catch (Exception)
            {
                insert_Log("Cannot get software directory path. Choose a new directory.", Error_Code);
            }
        }

        private int folderCreation(string folderPath)
        {
            try
            {
                Directory.CreateDirectory(folderPath);
                return (0);
            }
            catch (Exception)
            {
                insert_Log("Cannot create test data folder. Choose another file directory.", Error_Code);
                return (1);
            }
        }

        private bool COM_Config_Updater()
        {
            COM_Port_Name = COM_Port.Text.ToUpper().Trim();

            string BaudRate = COM_Bits.SelectedItem.ToString().Split(new string[] { ": " }, StringSplitOptions.None).Last();
            COM_BaudRate_Value = Int32.Parse(BaudRate);

            string DataBits = COM_DataBits.SelectedItem.ToString().Split(new string[] { ": " }, StringSplitOptions.None).Last();
            COM_DataBits_Value = Int32.Parse(DataBits);

            bool isNum = int.TryParse(COM_write_timeout.Text.Trim(), out int Value);
            if (isNum == true & Value > 0)
            {
                COM_WriteTimeout_Value = Value;
                COM_write_timeout.Text = Value.ToString();
            }
            else
            {
                COM_write_timeout.Text = "4000";
                insert_Log("Write Timeout must be a positive integer.", Error_Code);
                return false;
            }

            isNum = int.TryParse(COM_read_timeout.Text.Trim(), out Value);
            if (isNum == true & Value > 0)
            {
                COM_ReadTimeout_Value = Value;
                COM_read_timeout.Text = Value.ToString();
            }
            else
            {
                COM_read_timeout.Text = "9000";
                insert_Log("Read Timeout must be a positive integer.", Error_Code);
                return false;
            }

            string Parity = COM_Parity.SelectedItem.ToString().Split(new string[] { ": " }, StringSplitOptions.None).Last();
            switch (Parity)
            {
                case "Even":
                    COM_Parity_Value = 2;
                    break;
                case "Odd":
                    COM_Parity_Value = 1;
                    break;
                case "None":
                    COM_Parity_Value = 0;
                    break;
                case "Mark":
                    COM_Parity_Value = 3;
                    break;
                case "Space":
                    COM_Parity_Value = 4;
                    break;
                default:
                    COM_Parity_Value = 0;
                    break;
            }

            string StopBits = COM_Stop.SelectedItem.ToString().Split(new string[] { ": " }, StringSplitOptions.None).Last();
            switch (StopBits)
            {
                case "1":
                    COM_StopBits_Value = 1;
                    break;
                case "1.5":
                    COM_StopBits_Value = 3;
                    break;
                case "2":
                    COM_StopBits_Value = 2;
                    break;
                default:
                    COM_StopBits_Value = 1;
                    break;
            }

            string Flow = COM_Flow.SelectedItem.ToString().Split(new string[] { ": " }, StringSplitOptions.None).Last();
            switch (Flow)
            {
                case "Xon/Xoff":
                    COM_Handshake_Value = 1;
                    break;
                case "Hardware":
                    COM_Handshake_Value = 2;
                    break;
                case "None":
                    COM_Handshake_Value = 0;
                    break;
                default:
                    COM_Handshake_Value = 1;
                    break;
            }

            string rts = COM_rtsEnable.SelectedItem.ToString().Split(new string[] { ": " }, StringSplitOptions.None).Last();
            switch (rts)
            {
                case "True":
                    COM_RtsEnable = true;
                    break;
                case "False":
                    COM_RtsEnable = false;
                    break;
                default:
                    COM_RtsEnable = true;
                    break;
            }

            string dtr = COM_dtrEnable.SelectedItem.ToString().Split(new string[] { ": " }, StringSplitOptions.None).Last();
            switch (dtr)
            {
                case "True":
                    COM_DtrEnable = true;
                    break;
                case "False":
                    COM_DtrEnable = false;
                    break;
                default:
                    COM_DtrEnable = true;
                    break;
            }

            return true;
        }

        private void insert_Log(string Message, int Code)
        {
            SolidColorBrush Color = Brushes.Black;
            string Status = "";
            if (Code == Error_Code) //Error Message
            {
                Status = "[Error]";
                Color = Brushes.Red;
            }
            else if (Code == Success_Code) //Success Message
            {
                Status = "[Success]";
                Color = Brushes.Green;
            }
            else if (Code == Warning_Code) //Warning Message
            {
                Status = "[Warning]";
                Color = Brushes.Orange;
            }
            else if (Code == Config_Code) //Config Message
            {
                Status = "";
                Color = Brushes.Blue;
            }
            else if (Code == Message_Code)//Standard Message
            {
                Status = "";
                Color = Brushes.Black;
            }
            this.Dispatcher.Invoke(DispatcherPriority.Background, new ThreadStart(delegate
            {
                Info_Log.Inlines.Add(new Run(Status + " " + Message + "\n") { Foreground = Color });
                Info_Scroll.ScrollToBottom();
            }));
        }

        private void Get_COM_List()
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Caption like '%(COM%'"))
            {
                var portnames = SerialPort.GetPortNames();
                var ports = searcher.Get().Cast<ManagementBaseObject>().ToList().Select(p => p["Caption"].ToString());
                portList = portnames.Select(n => n + " - " + ports.FirstOrDefault(s => s.Contains('(' + n + ')'))).ToList();
                foreach (string p in portList)
                {
                    updateList(p);
                }
            }
        }

        private void updateList(string data)
        {
            ListBoxItem COM_itm = new ListBoxItem();
            COM_itm.Content = data;
            COM_List.Items.Add(COM_itm);
        }

        private void COM_Refresh_Click(object sender, RoutedEventArgs e)
        {
            COM_List.Items.Clear();
            Get_COM_List();
        }

        private void COM_List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                string temp = COM_List.SelectedItem.ToString().Split(new string[] { ": " }, StringSplitOptions.None).Last();
                string COM = temp.Substring(0, temp.IndexOf(" -"));
                COM_Port.Text = COM;
                COM_Open_Check();

            }
            catch (Exception)
            {
                insert_Log("Select a Valid COM Port.", Warning_Code);
            }
        }

        private bool COM_Open_Check()
        {
            try
            {
                using (var sp = new SerialPort(COM_Port.Text, 9600, System.IO.Ports.Parity.None, 8, System.IO.Ports.StopBits.One))
                {
                    sp.WriteTimeout = 500;
                    sp.ReadTimeout = 500;
                    sp.Handshake = Handshake.None;
                    sp.RtsEnable = true;
                    sp.Open();
                    System.Threading.Thread.Sleep(100);
                    sp.Close();
                    insert_Log(COM_Port.Text + " is open and ready for communication.", Success_Code);
                }
            }
            catch (Exception Ex)
            {
                COM_Port.Text = string.Empty;
                insert_Log(Ex.ToString(), Error_Code);
                insert_Log(COM_Port.Text + " is closed. Probably being used by a software.", Error_Code);
                insert_Log("Try another COM Port or check if COM is already used by another software.", Message_Code);
                return false;
            }
            return true;
        }

        private bool Set_COM_Open_Check()
        {
            try
            {
                using (var serial = new SerialPort(COM_Port_Name, COM_BaudRate_Value, (Parity)COM_Parity_Value, COM_DataBits_Value, (StopBits)COM_StopBits_Value))
                {
                    serial.WriteTimeout = COM_WriteTimeout_Value;
                    serial.ReadTimeout = COM_ReadTimeout_Value;
                    serial.RtsEnable = COM_RtsEnable;
                    serial.DtrEnable = COM_DtrEnable;
                    serial.Handshake = (Handshake)COM_Handshake_Value;
                    serial.Open();
                    System.Threading.Thread.Sleep(100);
                    serial.Close();
                    return true;
                }
            }
            catch (Exception)
            {
                COM_Port.Text = string.Empty;
                insert_Log(COM_Port.Text + " is closed. Probably being used by a software.", Error_Code);
                insert_Log("Try another COM Port or check if com is already used by another software.", Message_Code);
            }
            return true;
        }

        private (bool, string) Serial_Query_HP34401A(string command)
        {
            try
            {
                COM_Config_Updater();
                using (var serial = new SerialPort(COM_Port_Name, COM_BaudRate_Value, (Parity)COM_Parity_Value, COM_DataBits_Value, (StopBits)COM_StopBits_Value))
                {
                    serial.WriteTimeout = COM_WriteTimeout_Value;
                    serial.ReadTimeout = COM_ReadTimeout_Value;
                    serial.RtsEnable = COM_RtsEnable;
                    serial.DtrEnable = COM_DtrEnable;
                    serial.Handshake = (Handshake)COM_Handshake_Value;
                    serial.Open();
                    serial.WriteLine(command);
                    string data = serial.ReadLine();
                    serial.Close();
                    return (true, data);
                }
            }
            catch (Exception)
            {
                insert_Log("Serial Query Failed, check COM settings or connection.", Error_Code);
                return (false, "");
            }
        }

        private bool Serial_Write(string command)
        {
            try
            {
                COM_Config_Updater();
                using (var serial = new SerialPort(COM_Port_Name, COM_BaudRate_Value, (Parity)COM_Parity_Value, COM_DataBits_Value, (StopBits)COM_StopBits_Value))
                {
                    serial.WriteTimeout = COM_WriteTimeout_Value;
                    serial.ReadTimeout = COM_ReadTimeout_Value;
                    serial.RtsEnable = COM_RtsEnable;
                    serial.DtrEnable = COM_DtrEnable;
                    serial.Handshake = (Handshake)COM_Handshake_Value;
                    serial.Open();
                    serial.WriteLine(command);
                    serial.Close();
                    return true;
                }
            }
            catch (Exception)
            {
                insert_Log("Serial Write Failed, check COM settings or connection.", Error_Code);
                return false;
            }
        }

        private void HP34401A_Remote_Button_Click(object sender, RoutedEventArgs e)
        {
            Serial_Write("SYSTem:REMote");
            insert_Log("HP 34401A Remote Command Send.", Success_Code);
        }

        private void HP34401A_Reset_Button_Click(object sender, RoutedEventArgs e)
        {
            Serial_Write("*RST");
            insert_Log("Reset Command Send.", Success_Code);
        }

        private void Select_Directory_Click(object sender, RoutedEventArgs e)
        {
            var Choose_Directory = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog();
            if (Choose_Directory.ShowDialog() == true)
            {
                folder_Directory = Choose_Directory.SelectedPath + @"\" + "Log Data (HP34401A)";
            }
            insert_Log("Test Data will be saved here: " + folder_Directory, Config_Code);
        }

        private void Verify_34401A_Click(object sender, RoutedEventArgs e)
        {
            if (COM_Config_Updater() == true)
            {
                (bool check, string return_data) = Serial_Query_HP34401A("*IDN?");
                if (check == true)
                {
                    insert_Log(return_data, Success_Code);
                    if (return_data.Contains("34401A") == true)
                    {
                        insert_Log("Verify Successful.", Success_Code);
                    }
                    else
                    {
                        insert_Log("Verify Failed. Expected *IDN? query is HP34401A.", Error_Code);
                        insert_Log("Try Again.", Error_Code);
                    }
                }
            }
            else
            {
                insert_Log("COM Info is invalid. Correct any errors and try again.", Error_Code);
            }
        }

        private bool Connect_verify_34401A()
        {
            try
            {
                using (var serial = new SerialPort(COM_Port_Name, COM_BaudRate_Value, (Parity)COM_Parity_Value, COM_DataBits_Value, (StopBits)COM_StopBits_Value))
                {
                    serial.WriteTimeout = COM_WriteTimeout_Value;
                    serial.ReadTimeout = COM_ReadTimeout_Value;
                    serial.RtsEnable = COM_RtsEnable;
                    serial.DtrEnable = COM_DtrEnable;
                    serial.Handshake = (Handshake)COM_Handshake_Value;
                    serial.Open();
                    serial.WriteLine("*IDN?");
                    string data = serial.ReadLine();
                    serial.Close();
                    if (data.Contains("34401A") == true)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            catch (Exception)
            {
                insert_Log("Serial Query Failed, check COM settings or connection.", Error_Code);
                return false;
            }
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (folderCreation(folder_Directory) == 0)
            {
                folderCreation(folder_Directory + @"\" + "VDC");
                folderCreation(folder_Directory + @"\" + "ADC");
                folderCreation(folder_Directory + @"\" + "VAC");
                folderCreation(folder_Directory + @"\" + "AAC");
                folderCreation(folder_Directory + @"\" + "2WireOhms");
                folderCreation(folder_Directory + @"\" + "4WireOhms");
                folderCreation(folder_Directory + @"\" + "FREQ");
                folderCreation(folder_Directory + @"\" + "PER");
                folderCreation(folder_Directory + @"\" + "DIODE");
                folderCreation(folder_Directory + @"\" + "CONTINUITY");
                if (COM_Config_Updater() == true)
                {
                    if (Set_COM_Open_Check() == true)
                    {
                        if (Connect_verify_34401A() == true)
                        {
                            if (Serial_Write("*RST") == true)
                            {
                                Serial_Write("SYSTem:REMote");
                                Data_Updater();
                                insert_Log("Please wait....connecting soon", Success_Code);
                                this.Close();
                            }
                        }
                        else
                        {
                            insert_Log("Verify Failed. Try Again.", Error_Code);
                        }
                    }
                    else
                    {
                        insert_Log("COM Port is not open. Check if COM Port is in use.", Error_Code);
                        insert_Log("Connect Failed.", Error_Code);
                    }
                }
                else
                {
                    insert_Log("COM Info is invalid. Correct any errors and try again.", Error_Code);
                    insert_Log("Connect Failed.", Error_Code);
                }
            }
            else
            {
                insert_Log("Log Data Directory cannot be created on the selected path.", Error_Code);
                insert_Log("Choose another path by clicking the select button.", Error_Code);
            }
        }

        private void Data_Updater()
        {
            Serial_COM_Info.COM_Port = COM_Port_Name;
            Serial_COM_Info.COM_BaudRate = COM_BaudRate_Value;
            Serial_COM_Info.COM_Parity = COM_Parity_Value;
            Serial_COM_Info.COM_StopBits = COM_StopBits_Value;
            Serial_COM_Info.COM_DataBits = COM_DataBits_Value;
            Serial_COM_Info.COM_Handshake = COM_Handshake_Value;
            Serial_COM_Info.COM_WriteTimeout = COM_WriteTimeout_Value;
            Serial_COM_Info.COM_ReadTimeout = COM_ReadTimeout_Value;
            Serial_COM_Info.COM_RtsEnable = COM_RtsEnable;
            Serial_COM_Info.COM_DtrEnable = COM_DtrEnable;
            Serial_COM_Info.folder_Directory = folder_Directory;
            Serial_COM_Info.isConnected = true;
        }

        private void Info_Clear_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Info_Log.Inlines.Clear();
                Info_Log.Text = string.Empty;
            }
            catch (Exception)
            {

            }
        }

        private void Load_COM_Config()
        {
            try
            {
                string Software_Location = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + @"\" + "Serial_Config.txt";
                using (var readFile = new StreamReader(Software_Location))
                {
                    string COM_Config = readFile.ReadLine().Trim();
                    Process_Config_File_Data(COM_Config);
                    insert_Log("COM Settings loaded.", Success_Code);
                }
            }
            catch (Exception Ex)
            {
                insert_Log(Ex.Message, Error_Code);
                insert_Log("Loading COM Config file failed.", Error_Code);
            }
        }

        private void Process_Config_File_Data(string COM_Config_Data)
        {
            string[] COM_Config_Parts = COM_Config_Data.Split(',');

            string COM_Port_Name = COM_Config_Parts[0].ToUpper();
            int Bits_per_Seconds = int.Parse(COM_Config_Parts[1]);
            int Data_bits = int.Parse(COM_Config_Parts[2]);
            string Parity = COM_Config_Parts[3].ToUpper();
            int Stop_bits = int.Parse(COM_Config_Parts[4]);
            string Flow_control = COM_Config_Parts[5].ToUpper();
            int Write_Timeout = int.Parse(COM_Config_Parts[6]);
            int Read_Timeout = int.Parse(COM_Config_Parts[7]);
            string Request_to_Send = COM_Config_Parts[8].ToUpper();
            string Data_Terminal_Ready = COM_Config_Parts[9].ToUpper();

            if (COM_Port_Name.All(char.IsLetterOrDigit) && COM_Port_Name.Contains("COM") && COM_Port_Name.Length <= 6)
            {
                COM_Port.Text = COM_Port_Name;
            }
            else
            {
                insert_Log("Serial Config File: COM Port Name is invalid.", Error_Code);
                COM_Port.Text = "COM22";
            }

            if (new int[] { 300, 600, 1200, 2400, 4800, 9600, 14400, 19200, 38400, 57600, 115200, 128000 }.Contains(Bits_per_Seconds))
            {
                switch (Bits_per_Seconds)
                {
                    case 300:
                        COM_Bits.SelectedIndex = 0;
                        break;
                    case 600:
                        COM_Bits.SelectedIndex = 1;
                        break;
                    case 1200:
                        COM_Bits.SelectedIndex = 2;
                        break;
                    case 2400:
                        COM_Bits.SelectedIndex = 3;
                        break;
                    case 4800:
                        COM_Bits.SelectedIndex = 4;
                        break;
                    case 9600:
                        COM_Bits.SelectedIndex = 5;
                        break;
                    case 14400:
                        COM_Bits.SelectedIndex = 6;
                        break;
                    case 19200:
                        COM_Bits.SelectedIndex = 7;
                        break;
                    case 38400:
                        COM_Bits.SelectedIndex = 8;
                        break;
                    case 57600:
                        COM_Bits.SelectedIndex = 9;
                        break;
                    case 115200:
                        COM_Bits.SelectedIndex = 10;
                        break;
                    case 128000:
                        COM_Bits.SelectedIndex = 11;
                        break;
                    default:
                        COM_Bits.SelectedIndex = 5;
                        break;
                }
            }
            else
            {
                insert_Log("Serial Config File: Bits per Second is invalid.", Error_Code);
                COM_Bits.SelectedIndex = 5;
            }

            if (new int[] { 4, 5, 6, 7, 8 }.Contains(Data_bits))
            {
                switch (Data_bits)
                {
                    case 4:
                        COM_DataBits.SelectedIndex = 0;
                        break;
                    case 5:
                        COM_DataBits.SelectedIndex = 1;
                        break;
                    case 6:
                        COM_DataBits.SelectedIndex = 2;
                        break;
                    case 7:
                        COM_DataBits.SelectedIndex = 3;
                        break;
                    case 8:
                        COM_DataBits.SelectedIndex = 4;
                        break;
                    default:
                        COM_DataBits.SelectedIndex = 4;
                        break;
                }
            }
            else
            {
                insert_Log("Serial Config File: Data Bits is invalid.", Error_Code);
                COM_DataBits.SelectedIndex = 4;
            }

            if (new string[] { "EVEN", "ODD", "NONE", "MARK", "SPACE" }.Contains(Parity))
            {
                switch (Parity)
                {
                    case "EVEN":
                        COM_Parity.SelectedIndex = 0;
                        break;
                    case "ODD":
                        COM_Parity.SelectedIndex = 1;
                        break;
                    case "NONE":
                        COM_Parity.SelectedIndex = 2;
                        break;
                    case "MARK":
                        COM_Parity.SelectedIndex = 3;
                        break;
                    case "SPACE":
                        COM_Parity.SelectedIndex = 4;
                        break;
                    default:
                        COM_Parity.SelectedIndex = 2;
                        break;
                }
            }
            else
            {
                insert_Log("Serial Config File:  is invalid.", Error_Code);
                COM_Parity.SelectedIndex = 2;
            }

            if (new int[] { 1, 2, 3 }.Contains(Stop_bits))
            {
                switch (Stop_bits)
                {
                    case 1:
                        COM_Stop.SelectedIndex = 0;
                        break;
                    case 2:
                        COM_Stop.SelectedIndex = 2;
                        break;
                    case 3:
                        COM_Stop.SelectedIndex = 1;
                        break;
                    default:
                        COM_Stop.SelectedIndex = 0;
                        break;
                }
            }
            else
            {
                insert_Log("Serial Config File: Stop bits is invalid.", Error_Code);
                COM_Stop.SelectedIndex = 0;
            }

            if (new string[] { "XON/XOFF", "HARDWARE", "NONE" }.Contains(Flow_control))
            {
                switch (Flow_control)
                {
                    case "XON/XOFF":
                        COM_Flow.SelectedIndex = 0;
                        break;
                    case "HARDWARE":
                        COM_Flow.SelectedIndex = 1;
                        break;
                    case "NONE":
                        COM_Flow.SelectedIndex = 2;
                        break;
                    default:
                        COM_Flow.SelectedIndex = 2;
                        break;
                }
            }
            else
            {
                insert_Log("Serial Config File: Flow control is invalid.", Error_Code);
                COM_Flow.SelectedIndex = 2;
            }

            if (Write_Timeout >= 4000)
            {
                COM_write_timeout.Text = Write_Timeout.ToString();
            }
            else
            {
                insert_Log("Serial Config File: Write Timeout is invalid, must be 4000 or greater.", Error_Code);
            }

            if (Read_Timeout >= 9000)
            {
                COM_read_timeout.Text = Read_Timeout.ToString();
            }
            else
            {
                insert_Log("Serial Config File: Read Timeout is invalid, must be 9000 or greater.", Error_Code);
            }

            if (new string[] { "TRUE", "FALSE" }.Contains(Request_to_Send))
            {
                switch (Request_to_Send)
                {
                    case "TRUE":
                        COM_rtsEnable.SelectedIndex = 0;
                        break;
                    case "FALSE":
                        COM_rtsEnable.SelectedIndex = 1;
                        break;
                    default:
                        COM_rtsEnable.SelectedIndex = 0;
                        break;
                }
            }
            else
            {
                insert_Log("Serial Config File: Request to Send is invalid.", Error_Code);
                COM_rtsEnable.SelectedIndex = 0;
            }

            if (new string[] { "TRUE", "FALSE" }.Contains(Data_Terminal_Ready))
            {
                switch (Data_Terminal_Ready)
                {
                    case "TRUE":
                        COM_dtrEnable.SelectedIndex = 0;
                        break;
                    case "FALSE":
                        COM_dtrEnable.SelectedIndex = 1;
                        break;
                    default:
                        COM_dtrEnable.SelectedIndex = 0;
                        break;
                }
            }
            else
            {
                insert_Log("Serial Config File: Data Terminal Ready is invalid.", Error_Code);
                COM_dtrEnable.SelectedIndex = 0;
            }
        }

        private void COM_Config_Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string COM_Port_Number = COM_Port.Text.ToUpper().Trim();
                string BaudRate = COM_Bits.SelectedItem.ToString().Split(new string[] { ": " }, StringSplitOptions.None).Last();
                string DataBits = COM_DataBits.SelectedItem.ToString().Split(new string[] { ": " }, StringSplitOptions.None).Last();
                string Parity = COM_Parity.SelectedItem.ToString().Split(new string[] { ": " }, StringSplitOptions.None).Last();
                string StopBits = COM_Stop.SelectedItem.ToString().Split(new string[] { ": " }, StringSplitOptions.None).Last();
                if (StopBits == "1.5") 
                {
                    StopBits = "3";
                }

                string Flow = COM_Flow.SelectedItem.ToString().Split(new string[] { ": " }, StringSplitOptions.None).Last();
                int Write_Timeout = int.Parse(COM_write_timeout.Text.Trim());
                int Read_Timeout = int.Parse(COM_read_timeout.Text.Trim());
                string rts = COM_rtsEnable.SelectedItem.ToString().Split(new string[] { ": " }, StringSplitOptions.None).Last();
                string dtr = COM_dtrEnable.SelectedItem.ToString().Split(new string[] { ": " }, StringSplitOptions.None).Last();
                string Software_Location = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + @"\" + "Serial_Config.txt";

                string File_string = COM_Port_Number + "," + BaudRate + "," + DataBits + "," + Parity.ToUpper() + "," + StopBits + "," + Flow.ToUpper() + "," + Write_Timeout + "," + Read_Timeout + "," + rts.ToUpper() + "," + dtr.ToUpper();
                File.WriteAllText(Software_Location, File_string);
                insert_Log("COM settings saved.", Success_Code);
            }
            catch (Exception Ex)
            {
                insert_Log(Ex.Message, Error_Code);
                insert_Log("Failed to save COM config, try again.", Error_Code);
            }
        }
    }
}
