using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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
using Ivi.Visa;
using Ivi.Visa.FormattedIO;

namespace HP_34401A
{
    /// <summary>
    /// Interaction logic for GPIB_Select_Window.xaml
    /// </summary>
    public partial class GPIB_Select_Window : Window
    {

        //Codes for Info Log Color Palette
        int Success_Code = 0;
        int Error_Code = 1;
        int Warning_Code = 2;
        int Config_Code = 3;
        int Message_Code = 4;

        //List of Active GPIB Address
        List<string> GPIBList = new List<string>();

        //GPIB Port Information, updated by GUI
        string GPIB_Address = "";

        //Save Data Directory
        string folder_Directory;

        public GPIB_Select_Window()
        {
            InitializeComponent();
            getSoftwarePath();
            insert_Log("Choose the correct GPIB Address from the list.", Message_Code);
            insert_Log("Click the Connect button when you are ready.", Message_Code);
            Load_GPIB_Config();
            GetVisaInfo();
            Get_GPIB_Address();
        }

        private void GetVisaInfo()
        {
            try
            {
                Version VisaNetSharedComponentsVersion = typeof(GlobalResourceManager).Assembly.GetName().Version;
                insert_Log("Visa.NET info: " + VisaNetSharedComponentsVersion, Success_Code);

                FileVersionInfo VisaSharedComponentsInfo;
                VisaSharedComponentsInfo = FileVersionInfo.GetVersionInfo(System.IO.Path.Combine(Environment.SystemDirectory, "visaConfMgr.dll"));
                insert_Log("VISA Shared Components Version: " + VisaSharedComponentsInfo.ProductVersion, Success_Code);
            }
            catch (Exception Ex)
            {
                insert_Log(Ex.Message, Error_Code);
                insert_Log("Error related to VISA.NET.", Error_Code);
            }
        }

        private void Get_GPIB_Address()
        {
            try
            {
                GPIBList = GlobalResourceManager.Find("GPIB?*").ToList();
                foreach (string GPIB in GPIBList)
                {
                    updateList(GPIB);
                }
            }
            catch (Exception Ex) 
            {
                insert_Log(Ex.Message, Error_Code);
                insert_Log("Failed to get List of GPIB Devices.", Error_Code);
            }
        }

        private void updateList(string data)
        {
            ListBoxItem GPIB_item = new ListBoxItem();
            GPIB_item.Content = data;
            GPIB_List.Items.Add(GPIB_item);
        }

        private void GPIB_Refresh_Click(object sender, RoutedEventArgs e)
        {
            GPIB_List.Items.Clear();
            Get_GPIB_Address();
        }

        private void GPIB_List_MouseDoubleClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string GPIB_Address_Selected = GPIB_List.SelectedItem.ToString().Split(new string[] { ": " }, StringSplitOptions.None).Last();
                GPIB_Port.Text = GPIB_Address_Selected;
                GPIB_Address = GPIB_Address_Selected;
                if (Is_GPIB_Address_In_Use() == true)
                {
                    insert_Log("GPIB Address is open and ready for communication.", Success_Code);
                }
                else
                {
                    insert_Log("GPIB Address is not open.", Error_Code);
                }
            }
            catch (Exception Ex)
            {
                insert_Log(Ex.Message, Error_Code);
            }
        }

        private bool Is_GPIB_Address_In_Use()
        {
            try
            {
                using (IVisaSession Instrument = GlobalResourceManager.Open(GPIB_Address, AccessModes.ExclusiveLock, 2000))
                {
                    Instrument.UnlockResource();
                    Instrument.Dispose();
                }
                return true;
            }
            catch (Exception Ex)
            {
                insert_Log(Ex.Message, Error_Code);
                return false;
            }
        }

        private (bool, string) GPIB_Query(string Query_Command)
        {
            try
            {
                using (IVisaSession Instrument = GlobalResourceManager.Open(GPIB_Address, AccessModes.None, 2000))
                {
                    if (Instrument is IMessageBasedSession session)
                    {
                        session.TerminationCharacterEnabled = true;

                        session.FormattedIO.WriteLine(Query_Command);
                        string Message_Data = session.FormattedIO.ReadLine();

                        Instrument.Dispose();

                        return (true, Message_Data);
                    }
                    else
                    {
                        insert_Log("Query not possible at this moment.", Error_Code);
                        return (false, "Query Failed.");
                    }
                }
            }
            catch (Exception Ex)
            {
                insert_Log(Ex.Message, Error_Code);
                return (false, "Query Failed.");
            }
        }

        private void GPIB_Write(string Write_Command)
        {
            try
            {
                using (IVisaSession Instrument = GlobalResourceManager.Open(GPIB_Address, AccessModes.None, 2000))
                {
                    if (Instrument is IMessageBasedSession session)
                    {
                        session.TerminationCharacterEnabled = true;

                        session.FormattedIO.WriteLine(Write_Command);

                        Instrument.Dispose();
                    }
                    else
                    {
                        insert_Log("Write command not possible at this moment.", Error_Code);
                    }
                }
            }
            catch (Exception Ex)
            {
                insert_Log(Ex.Message, Error_Code);
            }
        }

        private void HP34401A_Reset_Button_Click(object sender, RoutedEventArgs e)
        {
            GPIB_Write("*RST");
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
            (bool Query_status, string Message) = GPIB_Query("*IDN?");
            if (Query_status == true)
            {
                insert_Log("Verify Successful: " + Message, Success_Code);
            }
            else
            {
                insert_Log("Verify failed.", Error_Code);
            }
        }

        private bool Connect_verify_34401A()
        {
            GPIB_Port.Text = GPIB_Address;
            (bool Query_status, string Message) = GPIB_Query("*IDN?");
            if (Query_status == true)
            {
                if (Message.Contains("34401A"))
                {
                    insert_Log("Verify Successful: " + Message, Success_Code);
                    return true;
                }
                else 
                {
                    return false;
                }
            }
            else 
            {
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
                if (Connect_verify_34401A() == true)
                {
                    if (true)
                    {
                        GPIB_Write("*RST");
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
                insert_Log("Log Data Directory cannot be created on the selected path.", Error_Code);
                insert_Log("Choose another path by clicking the select button.", Error_Code);
            }
        }

        private void Data_Updater()
        {
            GPIB_Address_Info.folder_Directory = folder_Directory;
            GPIB_Address_Info.GPIB_Address = GPIB_Address;
            GPIB_Address_Info.isConnected = true;
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

        private void Load_GPIB_Config()
        {
            try
            {
                string Software_Location = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + @"\" + "Settings_GPIB.txt";
                using (var readFile = new StreamReader(Software_Location))
                {
                    string GPIB_Config = readFile.ReadLine().Trim();
                    Process_Config_File_Data(GPIB_Config);
                    insert_Log("GPIB Settings loaded.", Success_Code);
                }
            }
            catch (Exception Ex)
            {
                insert_Log(Ex.Message, Error_Code);
                insert_Log("Loading GPIB Settings file failed.", Error_Code);
            }
        }

        private void Process_Config_File_Data(string GPIB_Config_Data)
        {
            string[] GPIB_Config_Parts = GPIB_Config_Data.Split(',');
            string GPIB_Address_Name = GPIB_Config_Parts[0];
            GPIB_Port.Text = GPIB_Address_Name;
            GPIB_Address = GPIB_Address_Name;
        }

        private void GPIB_Config_Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string GPIB_Address = GPIB_Port.Text.Trim();
                string Software_Location = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + @"\" + "Settings_GPIB.txt";

                string File_string = GPIB_Address + ",";
                File.WriteAllText(Software_Location, File_string);
                insert_Log("GPIB settings saved.", Success_Code);
            }
            catch (Exception Ex)
            {
                insert_Log(Ex.Message, Error_Code);
                insert_Log("Failed to save GPIB settings, try again.", Error_Code);
            }
        }
    }
}
