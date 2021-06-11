using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
    public class MeasurementData
    {
        public MeasurementData(int Measurement_Number, string Measurement_DateTime, string Measurement, string Measurement_Unit)
        {
            this.Measurement_Number = Measurement_Number;
            this.Measurement_DateTime = Measurement_DateTime;
            this.Measurement = Measurement;
            this.Measurement_Unit = Measurement_Unit;
        }
        public int Measurement_Number { get; set; }
        public string Measurement_DateTime { get; set; }
        public string Measurement { get; set; }
        public string Measurement_Unit { get; set; }
    }

    public partial class Measurement_Data_Table : Window
    {
        //Measurement Data is added to this Collection
        ObservableCollection<MeasurementData> Measurement_Data = new ObservableCollection<MeasurementData>();

        //Variable to store the total measurement data stored in the collection
        int Measurement_Counter = 0;

        //Predefined Colors
        SolidColorBrush Green = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF00FF17"));
        SolidColorBrush Blue = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF00C0FF"));
        SolidColorBrush Red = new SolidColorBrush((Color)ColorConverter.ConvertFromString("Red"));
        SolidColorBrush Yellow = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF00"));
        SolidColorBrush Orange = new SolidColorBrush((Color)ColorConverter.ConvertFromString("DarkOrange"));
        SolidColorBrush White = new SolidColorBrush(Colors.White);
        SolidColorBrush Gray = new SolidColorBrush(Colors.Gray);
        SolidColorBrush Black = new SolidColorBrush(Colors.Black);

        private DispatcherTimer Table_Data_Process;

        //Data is initially stored in this queue before being processed and stored in the Collection
        //Each of the element stored inside the queue contains the measurement data and the date time data
        public BlockingCollection<string> Table_Data_Queue = new BlockingCollection<string>();

        public Measurement_Data_Table(string Title)
        {
            InitializeComponent();
            this.Title = Title;
            Measurement_Table.ItemsSource = Measurement_Data;
            Initialize_Timers();

            //An InotifyCollection property when the Table's Content chnages, this will trigger an auto scroll to bottom event
            ((System.Collections.Specialized.INotifyCollectionChanged)Measurement_Table.Items).CollectionChanged += Measurement_Table_Scroll;
        }

        private void Initialize_Timers()
        {
            Table_Data_Process = new DispatcherTimer();
            Table_Data_Process.Tick += new EventHandler(Table_Data_Insert);
            Table_Data_Process.Interval = TimeSpan.FromSeconds(0.5);
            Table_Data_Process.Start();
        }

        private void Table_Data_Insert(object sender, EventArgs e)
        {
            try
            {
                while (Table_Data_Queue.Count > 0)
                {
                    string[] Data_Dequeue = Table_Data_Queue.Take().Split(',');
                    if (Data_Dequeue[1] == "+9.90000000E+37" || Data_Dequeue[1] == "-9.90000000E+37")
                    {
                        Data_Dequeue = null;
                    }
                    else
                    {
                        ++Measurement_Counter;
                        Measurement_Data.Add(new MeasurementData(Measurement_Counter, Data_Dequeue[0], ((decimal)double.Parse(Data_Dequeue[1], NumberStyles.Float)).ToString(), Data_Dequeue[2]));
                        Total_Measurement_Counter.Content = Measurement_Counter;
                    }
                }
            }
            catch (Exception)
            {

            }
        }

        //Table will automatically scroll to bottom when new data is added to the table
        private void Measurement_Table_Scroll(object sender, EventArgs e)
        {
            try
            {
                if (Table_AutoScroll.IsChecked == true)
                {
                    if (Measurement_Table.Items.Count > 0)
                    {
                        var border = VisualTreeHelper.GetChild(Measurement_Table, 0) as Decorator;
                        if (border != null)
                        {
                            var scroll = border.Child as ScrollViewer;
                            if (scroll != null) scroll.ScrollToEnd();
                        }
                    }
                }
            }
            catch (Exception)
            {

            }
        }

        //Exit Table Window
        private void Exit_Table_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        //Clears the Table
        private void ClearTable_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Measurement_Data.Clear();
                Measurement_Counter = 0;
                Total_Measurement_Counter.Content = Measurement_Counter.ToString();
            }
            catch (Exception)
            {

            }
        }

        //Save the data into a CSV file
        private void SaveTable_csv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv"
                };
                if (saveFileDialog.ShowDialog() == true)
                {
                    using (TextWriter datatotxt = new StreamWriter(saveFileDialog.FileName, false))
                    {
                        Measurement_Table.SelectAllCells();
                        Measurement_Table.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
                        ApplicationCommands.Copy.Execute(null, Measurement_Table);
                        Measurement_Table.UnselectAllCells();
                        datatotxt.Write((string)Clipboard.GetData(DataFormats.CommaSeparatedValue));
                    }
                }
            }
            catch (Exception)
            {

            }
        }

        //Save data into a text file
        private void SaveTable_text_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt"
                };
                if (saveFileDialog.ShowDialog() == true)
                {
                    using (TextWriter datatotxt = new StreamWriter(saveFileDialog.FileName, false))
                    {
                        Measurement_Table.SelectAllCells();
                        Measurement_Table.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
                        ApplicationCommands.Copy.Execute(null, Measurement_Table);
                        Measurement_Table.UnselectAllCells();
                        datatotxt.Write((string)Clipboard.GetData(DataFormats.CommaSeparatedValue));
                    }
                }
            }
            catch (Exception)
            {

            }
        }

        private void Table_12_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.FontSize = 12;
            Table_12.IsChecked = true;
            Table_14.IsChecked = false;
            Table_16.IsChecked = false;
            Table_18.IsChecked = false;
            TableContentFit(1);
            TableContentFit(2);
        }

        private void Table_14_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.FontSize = 14;
            Table_12.IsChecked = false;
            Table_14.IsChecked = true;
            Table_16.IsChecked = false;
            Table_18.IsChecked = false;
            TableContentFit(1);
            TableContentFit(2);
        }

        private void Table_16_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.FontSize = 16;
            Table_12.IsChecked = false;
            Table_14.IsChecked = false;
            Table_16.IsChecked = true;
            Table_18.IsChecked = false;
            TableContentFit(1);
            TableContentFit(2);
        }

        private void Table_18_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.FontSize = 18;
            Table_12.IsChecked = false;
            Table_14.IsChecked = false;
            Table_16.IsChecked = false;
            Table_18.IsChecked = true;
            TableContentFit(1);
            TableContentFit(2);
        }

        private void TableContentFit(int resizePer)
        {
            foreach (DataGridColumn column in Measurement_Table.Columns)
            {
                if (resizePer == 0)
                {
                    column.Width = new DataGridLength(1.0, DataGridLengthUnitType.SizeToHeader);
                }
                else if (resizePer == 1)
                {
                    column.Width = new DataGridLength(1.0, DataGridLengthUnitType.SizeToCells);
                }
                else
                {
                    column.Width = new DataGridLength(1.0, DataGridLengthUnitType.Auto);
                }
            }
        }

        private void resize_perHeader_Click(object sender, RoutedEventArgs e)
        {
            TableContentFit(0);
            resize_perHeader.IsChecked = true;
            resize_perColumn.IsChecked = false;
            resize_perBoth.IsChecked = false;
        }

        private void resize_perColumn_Click(object sender, RoutedEventArgs e)
        {
            TableContentFit(1);
            resize_perHeader.IsChecked = false;
            resize_perColumn.IsChecked = true;
            resize_perBoth.IsChecked = false;
        }

        private void resize_perBoth_Click(object sender, RoutedEventArgs e)
        {
            TableContentFit(2);
            resize_perHeader.IsChecked = false;
            resize_perColumn.IsChecked = false;
            resize_perBoth.IsChecked = true;
        }

        private void Arial_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.FontFamily = new FontFamily("Arial");
            Arial.IsChecked = true;
            Arial_Black.IsChecked = false;
            Courier_New.IsChecked = false;
            Comic_Sans_MS.IsChecked = false;
            Ink_Free.IsChecked = false;
            Segoe_UI.IsChecked = false;
            Segoe_UI_Black.IsChecked = false;
            Times_New_Roman.IsChecked = false;
        }

        private void Arial_Black_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.FontFamily = new FontFamily("Arial Black");
            Arial.IsChecked = false;
            Arial_Black.IsChecked = true;
            Courier_New.IsChecked = false;
            Comic_Sans_MS.IsChecked = false;
            Ink_Free.IsChecked = false;
            Segoe_UI.IsChecked = false;
            Segoe_UI_Black.IsChecked = false;
            Times_New_Roman.IsChecked = false;
        }

        private void Courier_New_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.FontFamily = new FontFamily("Courier New");
            Arial.IsChecked = false;
            Arial_Black.IsChecked = false;
            Courier_New.IsChecked = true;
            Comic_Sans_MS.IsChecked = false;
            Ink_Free.IsChecked = false;
            Segoe_UI.IsChecked = false;
            Segoe_UI_Black.IsChecked = false;
            Times_New_Roman.IsChecked = false;
        }

        private void Comic_Sans_MS_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.FontFamily = new FontFamily("Comic Sans MS");
            Arial.IsChecked = false;
            Arial_Black.IsChecked = false;
            Courier_New.IsChecked = false;
            Comic_Sans_MS.IsChecked = true;
            Ink_Free.IsChecked = false;
            Segoe_UI.IsChecked = false;
            Segoe_UI_Black.IsChecked = false;
            Times_New_Roman.IsChecked = false;
        }

        private void Ink_Free_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.FontFamily = new FontFamily("Ink Free");
            Arial.IsChecked = false;
            Arial_Black.IsChecked = false;
            Courier_New.IsChecked = false;
            Comic_Sans_MS.IsChecked = false;
            Ink_Free.IsChecked = true;
            Segoe_UI.IsChecked = false;
            Segoe_UI_Black.IsChecked = false;
            Times_New_Roman.IsChecked = false;
        }

        private void Segoe_UI_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.FontFamily = new FontFamily("Segoe UI");
            Arial.IsChecked = false;
            Arial_Black.IsChecked = false;
            Courier_New.IsChecked = false;
            Comic_Sans_MS.IsChecked = false;
            Ink_Free.IsChecked = false;
            Segoe_UI.IsChecked = true;
            Segoe_UI_Black.IsChecked = false;
            Times_New_Roman.IsChecked = false;
        }

        private void Segoe_UI_Black_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.FontFamily = new FontFamily("Segoe UI Black");
            Arial.IsChecked = false;
            Arial_Black.IsChecked = false;
            Courier_New.IsChecked = false;
            Comic_Sans_MS.IsChecked = false;
            Ink_Free.IsChecked = false;
            Segoe_UI.IsChecked = false;
            Segoe_UI_Black.IsChecked = true;
            Times_New_Roman.IsChecked = false;
        }

        private void Times_New_Roman_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.FontFamily = new FontFamily("Times New Roman");
            Arial.IsChecked = false;
            Arial_Black.IsChecked = false;
            Courier_New.IsChecked = false;
            Comic_Sans_MS.IsChecked = false;
            Ink_Free.IsChecked = false;
            Segoe_UI.IsChecked = false;
            Segoe_UI_Black.IsChecked = false;
            Times_New_Roman.IsChecked = true;
        }

        private void BackgroundGreen_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.RowBackground = Green;
            BackgroundGreen.IsChecked = true;
            BackgroundBlue.IsChecked = false;
            BackgroundRed.IsChecked = false;
            BackgroundYellow.IsChecked = false;
            BackgroundOrange.IsChecked = false;
            BackgroundWhite.IsChecked = false;
            BackgroundGray.IsChecked = false;
            BackgroundBlack.IsChecked = false;
            BackgroundGray.IsChecked = false;
        }

        private void BackgroundBlue_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.RowBackground = Blue;
            BackgroundGreen.IsChecked = false;
            BackgroundBlue.IsChecked = true;
            BackgroundRed.IsChecked = false;
            BackgroundYellow.IsChecked = false;
            BackgroundOrange.IsChecked = false;
            BackgroundWhite.IsChecked = false;
            BackgroundGray.IsChecked = false;
            BackgroundBlack.IsChecked = false;
        }

        private void BackgroundRed_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.RowBackground = Red;
            BackgroundGreen.IsChecked = false;
            BackgroundBlue.IsChecked = false;
            BackgroundRed.IsChecked = true;
            BackgroundYellow.IsChecked = false;
            BackgroundOrange.IsChecked = false;
            BackgroundWhite.IsChecked = false;
            BackgroundGray.IsChecked = false;
            BackgroundBlack.IsChecked = false;
        }

        private void BackgroundYellow_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.RowBackground = Yellow;
            BackgroundGreen.IsChecked = false;
            BackgroundBlue.IsChecked = false;
            BackgroundRed.IsChecked = false;
            BackgroundYellow.IsChecked = true;
            BackgroundOrange.IsChecked = false;
            BackgroundWhite.IsChecked = false;
            BackgroundGray.IsChecked = false;
            BackgroundBlack.IsChecked = false;
        }

        private void BackgroundOrange_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.RowBackground = Orange;
            BackgroundGreen.IsChecked = false;
            BackgroundBlue.IsChecked = false;
            BackgroundRed.IsChecked = false;
            BackgroundYellow.IsChecked = false;
            BackgroundOrange.IsChecked = true;
            BackgroundWhite.IsChecked = false;
            BackgroundGray.IsChecked = false;
            BackgroundBlack.IsChecked = false;
        }

        private void BackgroundWhite_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.RowBackground = White;
            BackgroundGreen.IsChecked = false;
            BackgroundBlue.IsChecked = false;
            BackgroundRed.IsChecked = false;
            BackgroundYellow.IsChecked = false;
            BackgroundOrange.IsChecked = false;
            BackgroundWhite.IsChecked = true;
            BackgroundGray.IsChecked = false;
            BackgroundBlack.IsChecked = false;
        }

        private void BackgroundGray_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.RowBackground = Gray;
            BackgroundGreen.IsChecked = false;
            BackgroundBlue.IsChecked = false;
            BackgroundRed.IsChecked = false;
            BackgroundYellow.IsChecked = false;
            BackgroundOrange.IsChecked = false;
            BackgroundWhite.IsChecked = false;
            BackgroundGray.IsChecked = true;
            BackgroundBlack.IsChecked = false;
        }

        private void BackgroundBlack_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.RowBackground = Black;
            BackgroundGreen.IsChecked = false;
            BackgroundBlue.IsChecked = false;
            BackgroundRed.IsChecked = false;
            BackgroundYellow.IsChecked = false;
            BackgroundOrange.IsChecked = false;
            BackgroundWhite.IsChecked = false;
            BackgroundGray.IsChecked = false;
            BackgroundBlack.IsChecked = true;
        }

        private void AltBackgroundGreen_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.AlternatingRowBackground = Green;
            AltBackgroundGreen.IsChecked = true;
            AltBackgroundBlue.IsChecked = false;
            AltBackgroundRed.IsChecked = false;
            AltBackgroundYellow.IsChecked = false;
            AltBackgroundOrange.IsChecked = false;
            AltBackgroundWhite.IsChecked = false;
            AltBackgroundGray.IsChecked = false;
            AltBackgroundBlack.IsChecked = false;

        }

        private void AltBackgroundBlue_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.AlternatingRowBackground = Blue;
            AltBackgroundGreen.IsChecked = false;
            AltBackgroundBlue.IsChecked = true;
            AltBackgroundRed.IsChecked = false;
            AltBackgroundYellow.IsChecked = false;
            AltBackgroundOrange.IsChecked = false;
            AltBackgroundWhite.IsChecked = false;
            AltBackgroundGray.IsChecked = false;
            AltBackgroundBlack.IsChecked = false;

        }

        private void AltBackgroundRed_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.AlternatingRowBackground = Red;
            AltBackgroundGreen.IsChecked = false;
            AltBackgroundBlue.IsChecked = false;
            AltBackgroundRed.IsChecked = true;
            AltBackgroundYellow.IsChecked = false;
            AltBackgroundOrange.IsChecked = false;
            AltBackgroundWhite.IsChecked = false;
            AltBackgroundGray.IsChecked = false;
            AltBackgroundBlack.IsChecked = false;

        }

        private void AltBackgroundYellow_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.AlternatingRowBackground = Yellow;
            AltBackgroundGreen.IsChecked = false;
            AltBackgroundBlue.IsChecked = false;
            AltBackgroundRed.IsChecked = false;
            AltBackgroundYellow.IsChecked = true;
            AltBackgroundOrange.IsChecked = false;
            AltBackgroundWhite.IsChecked = false;
            AltBackgroundGray.IsChecked = false;
            AltBackgroundBlack.IsChecked = false;

        }

        private void AltBackgroundOrange_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.AlternatingRowBackground = Orange;
            AltBackgroundGreen.IsChecked = false;
            AltBackgroundBlue.IsChecked = false;
            AltBackgroundRed.IsChecked = false;
            AltBackgroundYellow.IsChecked = false;
            AltBackgroundOrange.IsChecked = true;
            AltBackgroundWhite.IsChecked = false;
            AltBackgroundGray.IsChecked = false;
            AltBackgroundBlack.IsChecked = false;

        }

        private void AltBackgroundWhite_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.AlternatingRowBackground = White;
            AltBackgroundGreen.IsChecked = false;
            AltBackgroundBlue.IsChecked = false;
            AltBackgroundRed.IsChecked = false;
            AltBackgroundYellow.IsChecked = false;
            AltBackgroundOrange.IsChecked = false;
            AltBackgroundWhite.IsChecked = true;
            AltBackgroundGray.IsChecked = false;
            AltBackgroundBlack.IsChecked = false;

        }

        private void AltBackgroundGray_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.AlternatingRowBackground = Gray;
            AltBackgroundGreen.IsChecked = false;
            AltBackgroundBlue.IsChecked = false;
            AltBackgroundRed.IsChecked = false;
            AltBackgroundYellow.IsChecked = false;
            AltBackgroundOrange.IsChecked = false;
            AltBackgroundWhite.IsChecked = false;
            AltBackgroundGray.IsChecked = true;
            AltBackgroundBlack.IsChecked = false;

        }

        private void AltBackgroundBlack_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Table.AlternatingRowBackground = Black;
            AltBackgroundGreen.IsChecked = false;
            AltBackgroundBlue.IsChecked = false;
            AltBackgroundRed.IsChecked = false;
            AltBackgroundYellow.IsChecked = false;
            AltBackgroundOrange.IsChecked = false;
            AltBackgroundWhite.IsChecked = false;
            AltBackgroundGray.IsChecked = false;
            AltBackgroundBlack.IsChecked = true;

        }

        private void Window_Closed(object sender, EventArgs e)
        {
            try
            {
                Table_Data_Process.Stop();
                Table_Data_Process = null;
                Measurement_Data.Clear();
                Measurement_Data = null;
                Table_Data_Queue.Dispose();
                Table_Data_Queue = null;
            }
            catch (Exception)
            {

            }
        }
    }
}
