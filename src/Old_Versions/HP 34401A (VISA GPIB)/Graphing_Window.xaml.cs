using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using MathNet.Numerics.Statistics;
using Microsoft.Win32;
using ScottPlot;

namespace HP_34401A
{

    public partial class Graphing_Window : Window
    {
        //Set Window Title, helps determine which instrument owns this Graph Window
        string Graph_Owner;

        //These timers periodically check for any data inserted into Data_Queue, and processs it and insert it into the graph
        private System.Timers.Timer DataProcess;
        private DispatcherTimer GraphRender;

        //When this is set to true, the graph will be reset
        public bool Graph_Reset = false;

        //Auto clear the output log after a specific amount of items inside the log has been reached.
        int Auto_Clear_Output_Log_Count = 40; //This integer variable is thread safe, interlocked.exchange is used.

        //Data is initially stored in this queue before being processed and stored in the measurement array and date time array.
        //Each of the element stored inside the queue contains the measurement data and the date time data of when the measurement was captured.
        public BlockingCollection<string> Data_Queue = new BlockingCollection<string>();

        //Arrays are fixed sized, this variable determines the initial Array size. Its value will increase as Arrays get filled with measurement data
        static int Max_Allowed_Samples = 1_000_000;

        //All the processed measuremnt data is stored in this array and displayed on the GUI window.
        double[] Measurement_Data = new double[Max_Allowed_Samples];

        //All measurement data recieved by the GUI Window comes with data time data, which is stored in this array
        DateTime[] Measurement_DateTime = new DateTime[Max_Allowed_Samples];

        // A counter that is incremented when a measurement is processed. Show how many measuremnet is displayed on the GUI window.
        int Measurement_Count = 0; //For testing set

        // Measurement Units
        public string Measurement_Unit; //Ω VDC VAC ADC AVC
        public string Graph_Y_Axis_Label;

        //Measurement Plot, processed data is plotted onto the graph
        ScottPlot.Plottable.SignalPlot Measurement_Plot;

        //Mouser Tracker Horizontal & Vertical Line
        ScottPlot.Plottable.HLine Mouse_HLine;
        ScottPlot.Plottable.VLine Mouse_VLine;
        ScottPlot.Plottable.Text MouseCoordinates;
        ScottPlot.Plottable.Text MouseMeasurementData;
        bool ShowMouseTracker = false;
        bool ShowMouseCoordinates = false;
        bool ShowMouseMeasurementData = false;

        //Add Draggable Markers
        ScottPlot.Plottable.HLine Drag_HorizontalMarker_1;
        ScottPlot.Plottable.Annotation HorizontalMarker_1_Annotation;
        ScottPlot.Plottable.HLine Drag_HorizontalMarker_2;
        ScottPlot.Plottable.Annotation HorizontalMarker_2_Annotation;
        ScottPlot.Plottable.VLine Drag_VerticalMarker_1;
        ScottPlot.Plottable.Annotation VerticalMarker_1_Annotation;
        ScottPlot.Plottable.VLine Drag_VerticalMarker_2;
        ScottPlot.Plottable.Annotation VerticalMarker_2_Annotation;

        //Information Tab Variables
        int Total_Samples = 0;
        int Invalid_Samples = 0;
        int Positive_Samples = 0;
        int Negative_Samples = 0;
        double Latest_Sample = 0;
        double Max_Recorded_Sample = -999999999999999;
        double Min_Recorded_Sample = 999999999999999;
        //Variables related to the moving average
        double Moving_Average = 0;
        int Moving_average_count = 0;
        int Moving_average_factor = 50;
        int Moving_average_resolution = 8;

        public Graphing_Window(string Measurement_Unit, string Graph_Y_Axis_Label, string Graph_Owner)
        {
            InitializeComponent();
            Graph_RightClick_Menu();
            this.Measurement_Unit = Measurement_Unit;
            this.Graph_Y_Axis_Label = Graph_Y_Axis_Label;
            this.Title = Graph_Owner + " Graphing Module";
            this.Graph_Owner = Graph_Owner;
            Add_Main_Plot(Graph_Y_Axis_Label);
            Initialize_Timers();
        }

        private void Initialize_Timers()
        {
            DataProcess = new System.Timers.Timer(500);
            DataProcess.Elapsed += Data_Insert_Graph;
            DataProcess.AutoReset = false;
            DataProcess.Enabled = true;

            GraphRender = new DispatcherTimer();
            GraphRender.Tick += new EventHandler(Graph_Renderer);
            GraphRender.Interval = TimeSpan.FromSeconds(1);
            GraphRender.Start();
        }

        private void Data_Insert_Graph(object sender, EventArgs e)
        {

            if (Graph_Reset == true)
            {
                Measurement_Count = 0;

                Total_Samples = 0;
                Invalid_Samples = 0;
                Positive_Samples = 0;
                Negative_Samples = 0;
                Latest_Sample = 0;
                Moving_Average = 0;
                Max_Recorded_Sample = -999999999999999;
                Min_Recorded_Sample = 999999999999999;
                Moving_Average = 0;
                Moving_average_count = 0;

                Max_Allowed_Samples = 1_000_000;
                Array.Resize(ref Measurement_Data, Max_Allowed_Samples);
                Array.Resize(ref Measurement_DateTime, Max_Allowed_Samples);
                Measurement_Plot.Ys = Measurement_Data;

                Update_Measurement_Unit();
                Thread.Sleep(1500);
                while (Data_Queue.TryTake(out _)) { }

                Insert_Log("Graph has been reset.", 0);

                Graph_Reset = false;
            }

            while (Data_Queue.Count > 0)
            {
                if (Measurement_Count >= Max_Allowed_Samples)
                {
                    Max_Allowed_Samples = Max_Allowed_Samples + 1_000_000;
                    Array.Resize(ref Measurement_Data, Max_Allowed_Samples);
                    Array.Resize(ref Measurement_DateTime, Max_Allowed_Samples);
                    Measurement_Plot.Ys = Measurement_Data;
                    Insert_Log("Graph Data Array has been resized to allow for more data.", 0);
                }

                try
                {
                    string[] Data_Dequeue = Data_Queue.Take().Split(',');
                    if (Data_Dequeue[1].Contains("E+37"))
                    {
                        Data_Dequeue = null;
                        ++Invalid_Samples;
                    }
                    else 
                    {
                        bool isValidMeasurement = double.TryParse(Data_Dequeue[1], out double Measurement);

                        if (isValidMeasurement == true)
                        {
                            Measurement_DateTime[Measurement_Count] = DateTime.ParseExact(Data_Dequeue[0], "yyyy-MM-dd h:mm:ss.fff tt", null);
                            Measurement_Data[Measurement_Count] = Measurement;
                            Measurement_Plot.MaxRenderIndex = Measurement_Count;
                            Measurement_Count += 1;
                            ++Total_Samples;
                            Latest_Sample = Measurement;
                            if (Measurement >= 0)
                            {
                                ++Positive_Samples;
                            }
                            else
                            {
                                ++Negative_Samples;
                            }
                            if (Measurement > Max_Recorded_Sample)
                            {
                                Max_Recorded_Sample = Measurement;
                            }
                            if (Measurement < Min_Recorded_Sample)
                            {
                                Min_Recorded_Sample = Measurement;
                            }
                            Calculate_Moving_Average(Measurement);
                        }
                        else
                        {
                            ++Invalid_Samples;
                        }
                    }
                }
                catch (Exception Ex)
                {
                    Insert_Log(Ex.Message, 1);
                }
            }

            DataProcess.Enabled = true;
        }

        private void Calculate_Moving_Average(double measurement)
        {
            Moving_average_count += 1;
            Moving_Average = Moving_Average + (measurement - Moving_Average) / Math.Min(Moving_average_count, Moving_average_factor);
        }

        private void Graph_Renderer(object sender, EventArgs e)
        {
            try
            {
                if (Measurement_Count > 0)
                {
                    Total_Samples_Label.Content = Total_Samples;
                    Invalid_Samples_Label.Content = Invalid_Samples;
                    Positive_Samples_Label.Content = Positive_Samples;
                    Negative_Samples_Label.Content = Negative_Samples;
                    Latest_Sample_Label.Content = (decimal)Latest_Sample;
                    Max_Recorded_Sample_Label.Content = (decimal)Max_Recorded_Sample;
                    Min_Recorded_Sample_Label.Content = (decimal)Min_Recorded_Sample;
                    Moving_Average_Label.Content = (decimal)Math.Round(Moving_Average, Moving_average_resolution);

                    if (Auto_Axis_Enable.IsChecked == true)
                    {
                        Graph.Plot.AxisAuto();
                    }

                    Graph.Render();
                }
            }
            catch (Exception Ex) 
            {
                Insert_Log(Ex.Message, 1);
                Insert_Log("Graph Renderer Failed. Don't worry, trying again.", 1);
            }
        }

        private void Add_Main_Plot(string Graph_Y_Axis_Label)
        {
            Measurement_Plot = Graph.Plot.AddSignal(Measurement_Data, label: Graph_Y_Axis_Label);
            Graph.Plot.XLabel("N Samples");
            Graph.Plot.YLabel(Graph_Y_Axis_Label);
            Measurement_Plot.MaxRenderIndex = 0;
            Update_Measurement_Unit();
        }

        private void Update_Measurement_Unit()
        {
            this.Dispatcher.Invoke(DispatcherPriority.ContextIdle, new ThreadStart(delegate
            {
                Max_Recorded_Sample_Label_Unit.Content = Measurement_Unit;
                Min_Recorded_Sample_Label_Unit.Content = Measurement_Unit;
                Latest_Sample_Label_Unit.Content = Measurement_Unit;
                Moving_Average_Label_Unit.Content = Measurement_Unit;
                Graph.Plot.YLabel(Graph_Y_Axis_Label);
                Measurement_Plot.Label = Measurement_Unit;
            }));
        }

        private void Graph_RightClick_Menu()
        {
            Graph.RightClicked -= Graph.DefaultRightClickEvent;
            Graph.RightClicked += Graph_RightClick_Menu_Options;
        }

        private void Graph_RightClick_Menu_Options(object sender, EventArgs e)
        {
            MenuItem addSaveImageMenuItem = new MenuItem() { Header = "Save Image" };
            addSaveImageMenuItem.Click += Right_ClickSave_Graph_Image;
            MenuItem addCopyImageMenuItem = new MenuItem() { Header = "Copy Image" };
            addCopyImageMenuItem.Click += RightClick_Copy_Graph_Image;

            ContextMenu rightClickMenu = new ContextMenu();
            rightClickMenu.Items.Add(addSaveImageMenuItem);
            rightClickMenu.Items.Add(addCopyImageMenuItem);
            rightClickMenu.IsOpen = true;
        }

        private void Right_ClickSave_Graph_Image(object sender, EventArgs e)
        {
            Save_Graph_to_Image();
        }

        private void Save_Graph_to_Image()
        {
            try
            {
                var Save_Image_Window = new SaveFileDialog
                {
                    FileName = "Graph Plot_" + Measurement_Unit + "_" + DateTime.Now.ToString("yyyy-MM-dd h-mm-ss tt") + ".png",
                    Filter = "PNG Files (*.png)|*.png;*.png" +
                      "|JPG Files (*.jpg, *.jpeg)|*.jpg;*.jpeg" +
                      "|BMP Files (*.bmp)|*.bmp;*.bmp" +
                      "|All files (*.*)|*.*"
                };

                if (Save_Image_Window.ShowDialog() is true)
                {
                    Graph.Plot.SaveFig(Save_Image_Window.FileName);
                }
            }
            catch (Exception Ex)
            {
                Insert_Log("Could not save Graph Plot Image.", 1);
                Insert_Log(Ex.Message, 1);
            }
        }

        private void RightClick_Copy_Graph_Image(object sender, RoutedEventArgs e)
        {
            System.Drawing.Bitmap Graph_Image = Graph.Plot.Render(); ;

            MemoryStream Image_Memory = new MemoryStream();
            Graph_Image.Save(Image_Memory, System.Drawing.Imaging.ImageFormat.Png);

            BitmapImage Graph_Bitmap = new BitmapImage();
            Graph_Bitmap.BeginInit();
            Graph_Bitmap.StreamSource = new MemoryStream(Image_Memory.ToArray());
            Graph_Bitmap.EndInit();

            Clipboard.SetImage(Graph_Bitmap);

            Graph_Image.Dispose();
            Image_Memory.Dispose();
            Graph_Bitmap.Freeze();

        }

        //--------------------------- Menu Code Start --------------------------------

        private void Save_Data_Text_File_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var Save_Data_Text_Window = new SaveFileDialog
                {
                    FileName = "Graph Plot Data_" + Measurement_Unit + "_" + DateTime.Now.ToString("yyyy-MM-dd h-mm-ss tt") + ".txt",
                    Filter = "Normal text Files (*.txt)|*.txt;*.txt" +
                      "|All files (*.*)|*.*"
                };

                if (Save_Data_Text_Window.ShowDialog() is true)
                {
                    using (TextWriter datatotxt = new StreamWriter(Save_Data_Text_Window.FileName, false))
                    {
                        for (int i = 0; i < Measurement_Count; i++)
                        {
                            datatotxt.WriteLine(Measurement_DateTime[i].ToString("yyyy-MM-dd h:mm:ss.fff tt") + "," + Measurement_Data[i]);
                        }
                    }
                }
            }
            catch (Exception Ex)
            {
                Insert_Log("Could not save Graph Plot Data to text file.", 1);
                Insert_Log(Ex.Message, 1);
            }
        }

        private void Save_Data_CSV_File_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var Save_Data_Text_Window = new SaveFileDialog
                {
                    FileName = "Graph Plot Data_" + Measurement_Unit + "_" + DateTime.Now.ToString("yyyy-MM-dd h-mm-ss tt") + ".csv",
                    Filter = "CSV Files (*.csv)|*.csv;*.csv" +
                      "|All files (*.*)|*.*"
                };

                if (Save_Data_Text_Window.ShowDialog() is true)
                {
                    using (TextWriter datatotxt = new StreamWriter(Save_Data_Text_Window.FileName, false))
                    {
                        for (int i = 0; i < Measurement_Count; i++)
                        {
                            datatotxt.WriteLine(Measurement_DateTime[i].ToString("yyyy-MM-dd h:mm:ss.fff tt") + "," + Measurement_Data[i]);
                        }
                    }
                }
            }
            catch (Exception Ex)
            {
                Insert_Log("Could not save Graph Plot Data to csv file.", 1);
                Insert_Log(Ex.Message, 1);
            }
        }

        private void Save_Graph_Image_Click(object sender, RoutedEventArgs e)
        {
            Save_Graph_to_Image();
        }

        private void Exit_Graph_Window_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Force_Auto_Axis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.AxisAuto();
            Graph.Render();
            Insert_Log("Graph's Force Auto-Axis method called.", 0);
        }

        private void Auto_Axis_Enable_Click(object sender, RoutedEventArgs e)
        {
            if (Auto_Axis_Enable.IsChecked == true)
            {
                Insert_Log("Graph's Auto-Axis feature is enabled.", 0);
            }
            else
            {
                Insert_Log("Graph's Auto-Axis feature is disabled.", 2);
            }
        }

        private void Title_Text_Button_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Title(Title_Set_Text.Text);
            Graph.Render();
            Insert_Log("Graph's Title Label changed to " + Title_Set_Text.Text, 0);
            Title_Set_Text.Text = string.Empty;
        }

        private void X_Axis_Text_Button_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.Label(X_Axis_Set_Text.Text);
            Graph.Render();
            Insert_Log("Graph's X-Axis Label changed to " + X_Axis_Set_Text.Text, 0);
            X_Axis_Set_Text.Text = string.Empty;
        }

        private void Y_Axis_Text_Button_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.YAxis.Label(Y_Axis_Set_Text.Text);
            Graph.Render();
            Insert_Log("Graph's Y-Axis Label changed to " + Y_Axis_Set_Text.Text, 0);
            Y_Axis_Set_Text.Text = string.Empty;
        }

        private void X_Axis_Show_Click(object sender, RoutedEventArgs e)
        {
            if (X_Axis_Show.IsChecked == true)
            {
                Graph.Plot.XAxis.Ticks(true);
                Graph.Render();
                Insert_Log("Graph's X-Axis Ticks have been enabled.", 0);
            }
            else
            {
                Graph.Plot.XAxis.Ticks(false);
                Graph.Render();
                Insert_Log("Graph's X-Axis Ticks have been disabled.", 0);
            }
        }

        private void X_Axis_Default_Tick_Click(object sender, RoutedEventArgs e)
        {
            if (X_Axis_Default_Tick.IsChecked == true)
            {
                Graph.Plot.XAxis.ManualTickSpacing(0);
                Graph.Render();
                Insert_Log("Graph's X-Axis Ticks have been set to default.", 0);
            }
        }

        private void X_Axis_Custom_Set_Tick_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isValid, double Value) = Text_Num(X_Axis_Custom_Set_Tick.Text, false, true);
            if (isValid == true)
            {
                X_Axis_Default_Tick.IsChecked = false;
                Graph.Plot.XAxis.ManualTickSpacing(Value);
                Graph.Render();
                Insert_Log("Graph's X-Axis Ticks have been set to " + Value, 0);
                X_Axis_Custom_Set_Tick.Text = string.Empty;
            }
            else
            {
                X_Axis_Custom_Set_Tick.Text = string.Empty;
                Insert_Log("Graph's X-Axis Custom Tick value must be a number. No text or other characters are allowed.", 1);
            }

        }

        private void X_Axis_Tick_Rotation_0_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.TickLabelStyle(rotation: 0);
            Graph.Render();
            Insert_Log("Graph's X-Axis Ticks rotated to 0°", 0);
            X_Axis_Tick_Rotation_0.IsChecked = true;
            X_Axis_Tick_Rotation_45.IsChecked = false;
        }

        private void X_Axis_Tick_Rotation_45_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.TickLabelStyle(rotation: 45);
            Graph.Render();
            Insert_Log("Graph's X-Axis Ticks rotated to 45°", 0);
            X_Axis_Tick_Rotation_0.IsChecked = false;
            X_Axis_Tick_Rotation_45.IsChecked = true;
        }

        private void X_Axis_Minor_Grid_Click(object sender, RoutedEventArgs e)
        {
            if (X_Axis_Minor_Grid.IsChecked == true)
            {
                Graph.Plot.XAxis.MinorGrid(true);
                Graph.Render();
                Insert_Log("Graph's X-Axis Minor Grid is enabled.", 0);
            }
            else
            {
                Graph.Plot.XAxis.MinorGrid(false);
                Graph.Render();
                Insert_Log("Graph's X-Axis Minor Grid is disabled.", 0);
            }
        }

        private void X_Axis_Multiplier_Notation_Click(object sender, RoutedEventArgs e)
        {
            if (X_Axis_Multiplier_Notation.IsChecked == true)
            {
                Graph.Plot.XAxis.TickLabelNotation(multiplier: true);
                Graph.Render();
                Insert_Log("Graph's X-Axis Multiplier Notation is Enabled.", 0);
            }
            else
            {
                Graph.Plot.XAxis.TickLabelNotation(multiplier: false);
                Graph.Render();
                Insert_Log("Graph's X-Axis Multiplier Notation is Disabled.", 0);
            }
        }

        private void X_Axis_Tick_Ruler_Mode_Click(object sender, RoutedEventArgs e)
        {
            if (X_Axis_Tick_Ruler_Mode.IsChecked == true)
            {
                Graph.Plot.XAxis.RulerMode(true);
                Graph.Render();
                Insert_Log("Graph's X-Axis Ruler Mode is Enabled.", 0);
            }
            else
            {
                Graph.Plot.XAxis.RulerMode(false);
                Graph.Render();
                Insert_Log("Graph's X-Axis Ruler Mode is Disabled.", 0);
            }
        }

        private void Y_Axis_Show_Click(object sender, RoutedEventArgs e)
        {
            if (Y_Axis_Show.IsChecked == true)
            {
                Graph.Plot.YAxis.Ticks(true);
                Graph.Render();
                Insert_Log("Graph's Y-Axis Ticks have been enabled.", 0);
            }
            else
            {
                Graph.Plot.YAxis.Ticks(false);
                Graph.Render();
                Insert_Log("Graph's Y-Axis Ticks have been disabled.", 0);
            }
        }

        private void Y_Axis_Default_Tick_Click(object sender, RoutedEventArgs e)
        {
            if (Y_Axis_Default_Tick.IsChecked == true)
            {
                Graph.Plot.YAxis.ManualTickSpacing(0);
                Graph.Render();
                Insert_Log("Graph's Y-Axis Ticks have been set to default.", 0);
            }
        }

        private void Y_Axis_Custom_Set_Tick_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isValid, double Value) = Text_Num(Y_Axis_Custom_Set_Tick.Text, false, true);
            if (isValid == true)
            {
                Y_Axis_Default_Tick.IsChecked = false;
                Graph.Plot.YAxis.ManualTickSpacing(Value);
                Graph.Render();
                Insert_Log("Graph's Y-Axis Ticks have been set to " + Value, 0);
                Y_Axis_Custom_Set_Tick.Text = string.Empty;
            }
            else
            {
                Y_Axis_Custom_Set_Tick.Text = string.Empty;
                Insert_Log("Graph's Y-Axis Custom Tick value must be a number. No text or other characters are allowed.", 1);
            }
        }

        private void Y_Axis_Tick_Rotation_0_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.YAxis.TickLabelStyle(rotation: 0);
            Graph.Render();
            Insert_Log("Graph's Y-Axis Ticks rotated to 0°", 0);
            Y_Axis_Tick_Rotation_0.IsChecked = true;
            Y_Axis_Tick_Rotation_45.IsChecked = false;
        }

        private void Y_Axis_Tick_Rotation_45_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.YAxis.TickLabelStyle(rotation: 45);
            Graph.Render();
            Insert_Log("Graph's Y-Axis Ticks rotated to 45°", 0);
            Y_Axis_Tick_Rotation_0.IsChecked = false;
            Y_Axis_Tick_Rotation_45.IsChecked = true;
        }

        private void Y_Axis_Minor_Grid_Click(object sender, RoutedEventArgs e)
        {
            if (Y_Axis_Minor_Grid.IsChecked == true)
            {
                Graph.Plot.YAxis.MinorGrid(true);
                Graph.Render();
                Insert_Log("Graph's Y-Axis Minor Grid is enabled.", 0);
            }
            else
            {
                Graph.Plot.YAxis.MinorGrid(false);
                Graph.Render();
                Insert_Log("Graph's Y-Axis Minor Grid is disabled.", 0);
            }
        }

        private void Y_Axis_Multiplier_Notation_Click(object sender, RoutedEventArgs e)
        {
            if (Y_Axis_Multiplier_Notation.IsChecked == true)
            {
                Graph.Plot.YAxis.TickLabelNotation(multiplier: true);
                Graph.Render();
                Insert_Log("Graph's Y-Axis Multiplier Notation is Enabled.", 0);
            }
            else
            {
                Graph.Plot.YAxis.TickLabelNotation(multiplier: false);
                Graph.Render();
                Insert_Log("Graph's Y-Axis Multiplier Notation is Disabled.", 0);
            }
        }

        private void Y_Axis_Tick_Ruler_Mode_Click(object sender, RoutedEventArgs e)
        {
            if (Y_Axis_Tick_Ruler_Mode.IsChecked == true)
            {
                Graph.Plot.YAxis.RulerMode(true);
                Graph.Render();
                Insert_Log("Graph's Y-Axis Ruler Mode is Enabled.", 0);
            }
            else
            {
                Graph.Plot.YAxis.RulerMode(false);
                Graph.Render();
                Insert_Log("Graph's Y-Axis Ruler Mode is Disabled.", 0);
            }
        }

        //Font Size
        private void Font_Size_12_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.TickLabelStyle(fontSize: 12);
            Graph.Plot.YAxis.TickLabelStyle(fontSize: 12);
            Graph.Render();
            Insert_Log("Graph's Axis Font Size chnaged to 12.", 0);
            Font_12.IsChecked = true;
            Font_14.IsChecked = false;
            Font_16.IsChecked = false;
            Font_18.IsChecked = false;
        }

        private void Font_Size_14_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.TickLabelStyle(fontSize: 14);
            Graph.Plot.YAxis.TickLabelStyle(fontSize: 14);
            Graph.Render();
            Insert_Log("Graph's Axis Font Size chnaged to 14.", 0);
            Font_12.IsChecked = false;
            Font_14.IsChecked = true;
            Font_16.IsChecked = false;
            Font_18.IsChecked = false;
        }

        private void Font_Size_16_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.TickLabelStyle(fontSize: 16);
            Graph.Plot.YAxis.TickLabelStyle(fontSize: 16);
            Graph.Render();
            Insert_Log("Graph's Axis Font Size chnaged to 16.", 0);
            Font_12.IsChecked = false;
            Font_14.IsChecked = false;
            Font_16.IsChecked = true;
            Font_18.IsChecked = false;
        }

        private void Font_Size_18_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.TickLabelStyle(fontSize: 18);
            Graph.Plot.YAxis.TickLabelStyle(fontSize: 18);
            Graph.Render();
            Insert_Log("Graph's Axis Font Size chnaged to 18.", 0);
            Font_12.IsChecked = false;
            Font_14.IsChecked = false;
            Font_16.IsChecked = false;
            Font_18.IsChecked = true;
        }

        private void Graph_Vertical_Grid_Click(object sender, RoutedEventArgs e)
        {
            if (Graph_Vertical_Grid.IsChecked == true)
            {
                Graph.Plot.XAxis.Grid(true);
                Graph.Render();
                Insert_Log("Graph's Vertical Grid is Enabled.", 0);
            }
            else
            {
                Graph.Plot.XAxis.Grid(false);
                Graph.Render();
                Insert_Log("Graph's Vertical Grid is Disabled.", 0);
            }
        }

        private void Graph_Horizontal_Grid_Click(object sender, RoutedEventArgs e)
        {
            if (Graph_Horizontal_Grid.IsChecked == true)
            {
                Graph.Plot.YAxis.Grid(true);
                Graph.Render();
                Insert_Log("Graph's Horizontal Grid is Enabled.", 0);
            }
            else
            {
                Graph.Plot.YAxis.Grid(false);
                Graph.Render();
                Insert_Log("Graph's Horizontal Grid is Disabled.", 0);
            }
        }

        private void Grid_Style_Default_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Grid(lineStyle: LineStyle.Solid);
            Graph.Render();
            Insert_Log("Graph's Grid Style set to Default.", 0);
            Grid_Style_Default.IsChecked = true;
            Grid_Style_Dotted.IsChecked = false;
            Grid_Style_Dashed.IsChecked = false;
            Grid_Style_Dot_Dash.IsChecked = false;
        }

        private void Grid_Style_Dotted_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Grid(lineStyle: LineStyle.Dot);
            Graph.Render();
            Insert_Log("Graph's Grid Style set to Dotted.", 0);
            Grid_Style_Default.IsChecked = false;
            Grid_Style_Dotted.IsChecked = true;
            Grid_Style_Dashed.IsChecked = false;
            Grid_Style_Dot_Dash.IsChecked = false;
        }

        private void Grid_Style_Dashed_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Grid(lineStyle: LineStyle.Dash);
            Graph.Render();
            Insert_Log("Graph's Grid Style set to Dashed.", 0);
            Grid_Style_Default.IsChecked = false;
            Grid_Style_Dotted.IsChecked = false;
            Grid_Style_Dashed.IsChecked = true;
            Grid_Style_Dot_Dash.IsChecked = false;
        }

        private void Grid_Style_Dot_Dash_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Grid(lineStyle: LineStyle.DashDot);
            Graph.Render();
            Insert_Log("Graph's Grid Style set to Dot Dashed.", 0);
            Grid_Style_Default.IsChecked = false;
            Grid_Style_Dotted.IsChecked = false;
            Grid_Style_Dashed.IsChecked = false;
            Grid_Style_Dot_Dash.IsChecked = true;
        }

        private void Show_legend_Click(object sender, RoutedEventArgs e)
        {
            if (Show_legend.IsChecked == true)
            {
                Graph.Plot.Legend(true);
                Graph.Render();
                Insert_Log("Graph's Legend is Enabled.", 0);
            }
            else
            {
                Graph.Plot.Legend(false);
                Graph.Render();
                Insert_Log("Graph's Legend is Disabled.", 0);
            }
        }

        private void Legend_TopLeft_Click(object sender, RoutedEventArgs e)
        {
            Show_legend.IsChecked = true;
            Graph.Plot.Legend(location: Alignment.UpperLeft);
            Graph.Render();
            Insert_Log("Graph's Legend is now located at Top Left Side.", 0);
            Legend_TopLeft.IsChecked = true;
            Legend_TopRight.IsChecked = false;
            Legend_BottomLeft.IsChecked = false;
            Legend_BottomRight.IsChecked = false;
        }

        private void Legend_TopRight_Click(object sender, RoutedEventArgs e)
        {
            Show_legend.IsChecked = true;
            Graph.Plot.Legend(location: Alignment.UpperRight);
            Graph.Render();
            Insert_Log("Graph's Legend is now located at Top Right Side.", 0);
            Legend_TopLeft.IsChecked = false;
            Legend_TopRight.IsChecked = true;
            Legend_BottomLeft.IsChecked = false;
            Legend_BottomRight.IsChecked = false;
        }

        private void Legend_BottomLeft_Click(object sender, RoutedEventArgs e)
        {
            Show_legend.IsChecked = true;
            Graph.Plot.Legend(location: Alignment.LowerLeft);
            Graph.Render();
            Insert_Log("Graph's Legend is now located at Bottom Left Side.", 0);
            Legend_TopLeft.IsChecked = false;
            Legend_TopRight.IsChecked = false;
            Legend_BottomLeft.IsChecked = true;
            Legend_BottomRight.IsChecked = false;
        }

        private void Legend_BottomRight_Click(object sender, RoutedEventArgs e)
        {
            Show_legend.IsChecked = true;
            Graph.Plot.Legend(location: Alignment.LowerRight);
            Graph.Render();
            Insert_Log("Graph's Legend is now located at Bottom Left Side.", 0);
            Legend_TopLeft.IsChecked = false;
            Legend_TopRight.IsChecked = false;
            Legend_BottomLeft.IsChecked = false;
            Legend_BottomRight.IsChecked = true;
        }

        private void AVG_Factor_50_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Moving_average_factor, 50);
            Insert_Log("Exponential Moving Average's factor set to " + Moving_average_factor + ".", 0);
            AVG_Factor_Selected();
        }

        private void AVG_Factor_100_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Moving_average_factor, 100);
            Insert_Log("Exponential Moving Average's factor set to " + Moving_average_factor + ".", 0);
            AVG_Factor_Selected();
        }

        private void AVG_Factor_200_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Moving_average_factor, 200);
            Insert_Log("Exponential Moving Average's factor set to " + Moving_average_factor + ".", 0);
            AVG_Factor_Selected();
        }

        private void AVG_Factor_400_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Moving_average_factor, 400);
            Insert_Log("Exponential Moving Average's factor set to " + Moving_average_factor + ".", 0);
            AVG_Factor_Selected();
        }

        private void AVG_Factor_800_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Moving_average_factor, 800);
            Insert_Log("Exponential Moving Average's factor set to " + Moving_average_factor + ".", 0);
            AVG_Factor_Selected();
        }

        private void AVG_Factor_1000_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Moving_average_factor, 1000);
            Insert_Log("Exponential Moving Average's factor set to " + Moving_average_factor + ".", 0);
            AVG_Factor_Selected();
        }

        private void AVG_Factor_Selected()
        {
            if (Moving_average_factor == 50)
            {
                AVG_Factor_50.IsChecked = true;
            }
            else
            {
                AVG_Factor_50.IsChecked = false;
            }
            if (Moving_average_factor == 100)
            {
                AVG_Factor_100.IsChecked = true;
            }
            else
            {
                AVG_Factor_100.IsChecked = false;
            }
            if (Moving_average_factor == 200)
            {
                AVG_Factor_200.IsChecked = true;
            }
            else
            {
                AVG_Factor_200.IsChecked = false;
            }
            if (Moving_average_factor == 400)
            {
                AVG_Factor_400.IsChecked = true;
            }
            else
            {
                AVG_Factor_400.IsChecked = false;
            }
            if (Moving_average_factor == 800)
            {
                AVG_Factor_800.IsChecked = true;
            }
            else
            {
                AVG_Factor_800.IsChecked = false;
            }
            if (Moving_average_factor == 1000)
            {
                AVG_Factor_1000.IsChecked = true;
            }
            else
            {
                AVG_Factor_1000.IsChecked = false;
            }
        }

        private void AVG_Resolution_2_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Moving_average_resolution, 2);
            Insert_Log("Exponential Moving Average's resolution set to " + Moving_average_resolution + ".", 0);
            AVG_Res_Selected();
        }

        private void AVG_Resolution_3_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Moving_average_resolution, 3);
            Insert_Log("Exponential Moving Average's resolution set to " + Moving_average_resolution + ".", 0);
            AVG_Res_Selected();
        }

        private void AVG_Resolution_4_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Moving_average_resolution, 4);
            Insert_Log("Exponential Moving Average's resolution set to " + Moving_average_resolution + ".", 0);
            AVG_Res_Selected();
        }

        private void AVG_Resolution_5_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Moving_average_resolution, 5);
            Insert_Log("Exponential Moving Average's resolution set to " + Moving_average_resolution + ".", 0);
            AVG_Res_Selected();
        }

        private void AVG_Resolution_6_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Moving_average_resolution, 6);
            Insert_Log("Exponential Moving Average's resolution set to " + Moving_average_resolution + ".", 0);
            AVG_Res_Selected();
        }

        private void AVG_Resolution_7_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Moving_average_resolution, 7);
            Insert_Log("Exponential Moving Average's resolution set to " + Moving_average_resolution + ".", 0);
            AVG_Res_Selected();
        }

        private void AVG_Resolution_8_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Moving_average_resolution, 8);
            Insert_Log("Exponential Moving Average's resolution set to " + Moving_average_resolution + ".", 0);
            AVG_Res_Selected();
        }

        private void AVG_Res_Selected()
        {
            if (Moving_average_resolution == 2)
            {
                AVG_Resolution_2.IsChecked = true;
            }
            else
            {
                AVG_Resolution_2.IsChecked = false;
            }
            if (Moving_average_resolution == 3)
            {
                AVG_Resolution_3.IsChecked = true;
            }
            else
            {
                AVG_Resolution_3.IsChecked = false;
            }
            if (Moving_average_resolution == 4)
            {
                AVG_Resolution_4.IsChecked = true;
            }
            else
            {
                AVG_Resolution_4.IsChecked = false;
            }
            if (Moving_average_resolution == 5)
            {
                AVG_Resolution_5.IsChecked = true;
            }
            else
            {
                AVG_Resolution_5.IsChecked = false;
            }
            if (Moving_average_resolution == 6)
            {
                AVG_Resolution_6.IsChecked = true;
            }
            else
            {
                AVG_Resolution_6.IsChecked = false;
            }
            if (Moving_average_resolution == 7)
            {
                AVG_Resolution_7.IsChecked = true;
            }
            else
            {
                AVG_Resolution_7.IsChecked = false;
            }
            if (Moving_average_resolution == 8)
            {
                AVG_Resolution_8.IsChecked = true;
            }
            else
            {
                AVG_Resolution_8.IsChecked = false;
            }
        }

        private void RefreshRate_1ms_Click(object sender, RoutedEventArgs e)
        {
            RefreshRate_Select(9);
            GraphRender.Interval = TimeSpan.FromMilliseconds(1);
            DataProcess.Interval = 0.05;
            Insert_Log("Graph's Refresh Rate is set to 1ms.", 0);
        }

        private void RefreshRate_5ms_Click(object sender, RoutedEventArgs e)
        {
            RefreshRate_Select(8);
            GraphRender.Interval = TimeSpan.FromMilliseconds(5);
            DataProcess.Interval = 2;
            Insert_Log("Graph's Refresh Rate is set to 5ms.", 0);
        }

        private void RefreshRate_10ms_Click(object sender, RoutedEventArgs e)
        {
            RefreshRate_Select(0);
            GraphRender.Interval = TimeSpan.FromMilliseconds(10);
            DataProcess.Interval = 5;
            Insert_Log("Graph's Refresh Rate is set to 10ms.", 0);
        }

        private void RefreshRate_50ms_Click(object sender, RoutedEventArgs e)
        {
            RefreshRate_Select(1);
            GraphRender.Interval = TimeSpan.FromMilliseconds(50);
            DataProcess.Interval = 25;
            Insert_Log("Graph's Refresh Rate is set to 50ms.", 0);
        }

        private void RefreshRate_100ms_Click(object sender, RoutedEventArgs e)
        {
            RefreshRate_Select(2);
            GraphRender.Interval = TimeSpan.FromMilliseconds(100);
            DataProcess.Interval = 50;
            Insert_Log("Graph's Refresh Rate is set to 100ms.", 0);
        }

        private void RefreshRate_200ms_Click(object sender, RoutedEventArgs e)
        {
            RefreshRate_Select(3);
            GraphRender.Interval = TimeSpan.FromMilliseconds(200);
            DataProcess.Interval = 100;
            Insert_Log("Graph's Refresh Rate is set to 200ms.", 0);
        }

        private void RefreshRate_500ms_Click(object sender, RoutedEventArgs e)
        {
            RefreshRate_Select(4);
            GraphRender.Interval = TimeSpan.FromMilliseconds(500);
            DataProcess.Interval = 250;
            Insert_Log("Graph's Refresh Rate is set to 500ms.", 0);
        }

        private void RefreshRate_1s_Click(object sender, RoutedEventArgs e)
        {
            RefreshRate_Select(5);
            GraphRender.Interval = TimeSpan.FromSeconds(1);
            DataProcess.Interval = 500;
            Insert_Log("Graph's Refresh Rate is set to 1s.", 0);
        }

        private void RefreshRate_2s_Click(object sender, RoutedEventArgs e)
        {
            RefreshRate_Select(6);
            GraphRender.Interval = TimeSpan.FromSeconds(2);
            DataProcess.Interval = 1000;
            Insert_Log("Graph's Refresh Rate is set to 2s.", 0);
        }

        private void RefreshRate_5s_Click(object sender, RoutedEventArgs e)
        {
            RefreshRate_Select(7);
            GraphRender.Interval = TimeSpan.FromSeconds(5);
            DataProcess.Interval = 2500;
            Insert_Log("Graph's Refresh Rate is set to 5s.", 0);
        }

        private void RefreshRate_Select(int Selected)
        {
            if (Selected == 0)
            {
                RefreshRate_10ms.IsChecked = true;
            }
            else
            {
                RefreshRate_10ms.IsChecked = false;
            }
            if (Selected == 1)
            {
                RefreshRate_50ms.IsChecked = true;
            }
            else
            {
                RefreshRate_50ms.IsChecked = false;
            }
            if (Selected == 2)
            {
                RefreshRate_100ms.IsChecked = true;
            }
            else
            {
                RefreshRate_100ms.IsChecked = false;
            }
            if (Selected == 3)
            {
                RefreshRate_200ms.IsChecked = true;
            }
            else
            {
                RefreshRate_200ms.IsChecked = false;
            }
            if (Selected == 4)
            {
                RefreshRate_500ms.IsChecked = true;
            }
            else
            {
                RefreshRate_500ms.IsChecked = false;
            }
            if (Selected == 5)
            {
                RefreshRate_1s.IsChecked = true;
            }
            else
            {
                RefreshRate_1s.IsChecked = false;
            }
            if (Selected == 6)
            {
                RefreshRate_2s.IsChecked = true;
            }
            else
            {
                RefreshRate_2s.IsChecked = false;
            }
            if (Selected == 7)
            {
                RefreshRate_5s.IsChecked = true;
            }
            else
            {
                RefreshRate_5s.IsChecked = false;
            }
            if (Selected == 8)
            {
                RefreshRate_5ms.IsChecked = true;
            }
            else
            {
                RefreshRate_5ms.IsChecked = false;
            }
            if (Selected == 9)
            {
                RefreshRate_1ms.IsChecked = true;
            }
            else
            {
                RefreshRate_1ms.IsChecked = false;
            }
        }

        private void Reset_Graph_Click(object sender, RoutedEventArgs e)
        {
            Graph_Reset = true;
            Insert_Log("Graph Reset Command has been send.", 0);
        }

        //-----------------------Graph Colors-----------------------------------------------------

        //Measurements Plot Color
        private void Green_Measurements_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#FF00FF17");
            Graph.Render();
            Measurements_Color_Select(0);
        }

        private void Blue_Measurements_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#FF00C0FF");
            Graph.Render();
            Measurements_Color_Select(1);
        }

        private void Red_Measurements_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#FFFF0000");
            Graph.Render();
            Measurements_Color_Select(2);
        }

        private void Yellow_Measurements_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#FFFFFF00");
            Graph.Render();
            Measurements_Color_Select(3);
        }

        private void Orange_Measurements_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#FFFF8C00");
            Graph.Render();
            Measurements_Color_Select(4);
        }

        private void Black_Measurements_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#FF000000");
            Graph.Render();
            Measurements_Color_Select(5);
        }

        private void Pink_Measurements_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#FFFF1493");
            Graph.Render();
            Measurements_Color_Select(6);
        }

        private void Violet_Measurements_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#FF9400D3");
            Graph.Render();
            Measurements_Color_Select(7);
        }

        private void White_Measurements_Click(object sender, RoutedEventArgs e)
        {
            Measurement_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#FFFFFFFF");
            Graph.Render();
            Measurements_Color_Select(8);
        }

        private void Measurements_Color_Select(int Selected)
        {
            if (Selected == 0)
            {
                Green_Measurements.IsChecked = true;
            }
            else
            {
                Green_Measurements.IsChecked = false;
            }
            if (Selected == 1)
            {
                Blue_Measurements.IsChecked = true;
            }
            else
            {
                Blue_Measurements.IsChecked = false;
            }
            if (Selected == 2)
            {
                Red_Measurements.IsChecked = true;
            }
            else
            {
                Red_Measurements.IsChecked = false;
            }
            if (Selected == 3)
            {
                Yellow_Measurements.IsChecked = true;
            }
            else
            {
                Yellow_Measurements.IsChecked = false;
            }
            if (Selected == 4)
            {
                Orange_Measurements.IsChecked = true;
            }
            else
            {
                Orange_Measurements.IsChecked = false;
            }
            if (Selected == 5)
            {
                Black_Measurements.IsChecked = true;
            }
            else
            {
                Black_Measurements.IsChecked = false;
            }
            if (Selected == 6)
            {
                Pink_Measurements.IsChecked = true;
            }
            else
            {
                Pink_Measurements.IsChecked = false;
            }
            if (Selected == 7)
            {
                Violet_Measurements.IsChecked = true;
            }
            else
            {
                Violet_Measurements.IsChecked = false;
            }
            if (Selected == 8)
            {
                White_Measurements.IsChecked = true;
            }
            else
            {
                White_Measurements.IsChecked = false;
            }
        }

        //Background Color

        private void Green_Background_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(dataBackground: System.Drawing.ColorTranslator.FromHtml("#FFAAFFB2"));
            Graph.Render();
            Background_Color_Select(0);
        }

        private void Blue_Background_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(dataBackground: System.Drawing.ColorTranslator.FromHtml("#FFA1E7FF"));
            Graph.Render();
            Background_Color_Select(1);
        }

        private void Red_Background_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(dataBackground: System.Drawing.ColorTranslator.FromHtml("#FFFF8989"));
            Graph.Render();
            Background_Color_Select(2);
        }

        private void Yellow_Background_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(dataBackground: System.Drawing.ColorTranslator.FromHtml("#FFFFFF93"));
            Graph.Render();
            Background_Color_Select(3);
        }

        private void Orange_Background_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(dataBackground: System.Drawing.ColorTranslator.FromHtml("#FFFFCB8C"));
            Graph.Render();
            Background_Color_Select(4);
        }

        private void Black_Background_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(dataBackground: System.Drawing.ColorTranslator.FromHtml("#FF6E6E6E"));
            Graph.Render();
            Background_Color_Select(5);
        }

        private void Pink_Background_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(dataBackground: System.Drawing.ColorTranslator.FromHtml("#FFFF9ED2"));
            Graph.Render();
            Background_Color_Select(6);
        }

        private void Violet_Background_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(dataBackground: System.Drawing.ColorTranslator.FromHtml("#FFE6ACFF"));
            Graph.Render();
            Background_Color_Select(7);
        }

        private void White_Background_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(dataBackground: System.Drawing.ColorTranslator.FromHtml("#FFFFFFFF"));
            Graph.Render();
            Background_Color_Select(8);
        }

        private void Background_Color_Select(int Selected)
        {
            if (Selected == 0)
            {
                Green_Background.IsChecked = true;
            }
            else
            {
                Green_Background.IsChecked = false;
            }
            if (Selected == 1)
            {
                Blue_Background.IsChecked = true;
            }
            else
            {
                Blue_Background.IsChecked = false;
            }
            if (Selected == 2)
            {
                Red_Background.IsChecked = true;
            }
            else
            {
                Red_Background.IsChecked = false;
            }
            if (Selected == 3)
            {
                Yellow_Background.IsChecked = true;
            }
            else
            {
                Yellow_Background.IsChecked = false;
            }
            if (Selected == 4)
            {
                Orange_Background.IsChecked = true;
            }
            else
            {
                Orange_Background.IsChecked = false;
            }
            if (Selected == 5)
            {
                Black_Background.IsChecked = true;
            }
            else
            {
                Black_Background.IsChecked = false;
            }
            if (Selected == 6)
            {
                Pink_Background.IsChecked = true;
            }
            else
            {
                Pink_Background.IsChecked = false;
            }
            if (Selected == 7)
            {
                Violet_Background.IsChecked = true;
            }
            else
            {
                Violet_Background.IsChecked = false;
            }
            if (Selected == 8)
            {
                White_Background.IsChecked = true;
            }
            else
            {
                White_Background.IsChecked = false;
            }
        }


        //ForeGround Color
        private void Green_Foreground_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(figureBackground: System.Drawing.ColorTranslator.FromHtml("#FFAAFFB2"));
            Graph.Render();
            Foreground_Color_Select(0);
        }

        private void Blue_Foreground_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(figureBackground: System.Drawing.ColorTranslator.FromHtml("#FFA1E7FF"));
            Graph.Render();
            Foreground_Color_Select(1);
        }

        private void Red_Foreground_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(figureBackground: System.Drawing.ColorTranslator.FromHtml("#FFFF8989"));
            Graph.Render();
            Foreground_Color_Select(2);
        }

        private void Yellow_Foreground_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(figureBackground: System.Drawing.ColorTranslator.FromHtml("#FFFFFF93"));
            Graph.Render();
            Foreground_Color_Select(3);
        }

        private void Orange_Foreground_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(figureBackground: System.Drawing.ColorTranslator.FromHtml("#FFFFCB8C"));
            Graph.Render();
            Foreground_Color_Select(4);
        }

        private void Black_Foreground_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(figureBackground: System.Drawing.ColorTranslator.FromHtml("#FF6E6E6E"));
            Graph.Render();
            Foreground_Color_Select(5);
        }

        private void Pink_Foreground_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(figureBackground: System.Drawing.ColorTranslator.FromHtml("#FFFF9ED2"));
            Graph.Render();
            Foreground_Color_Select(6);
        }

        private void Violet_Foreground_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(figureBackground: System.Drawing.ColorTranslator.FromHtml("#FFE6ACFF"));
            Graph.Render();
            Foreground_Color_Select(7);
        }

        private void White_Foreground_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(figureBackground: System.Drawing.ColorTranslator.FromHtml("#FFFFFFFF"));
            Graph.Render();
            Foreground_Color_Select(8);
        }

        private void Foreground_Color_Select(int Selected)
        {
            if (Selected == 0)
            {
                Green_Foreground.IsChecked = true;
            }
            else
            {
                Green_Foreground.IsChecked = false;
            }
            if (Selected == 1)
            {
                Blue_Foreground.IsChecked = true;
            }
            else
            {
                Blue_Foreground.IsChecked = false;
            }
            if (Selected == 2)
            {
                Red_Foreground.IsChecked = true;
            }
            else
            {
                Red_Foreground.IsChecked = false;
            }
            if (Selected == 3)
            {
                Yellow_Foreground.IsChecked = true;
            }
            else
            {
                Yellow_Foreground.IsChecked = false;
            }
            if (Selected == 4)
            {
                Orange_Foreground.IsChecked = true;
            }
            else
            {
                Orange_Foreground.IsChecked = false;
            }
            if (Selected == 5)
            {
                Black_Foreground.IsChecked = true;
            }
            else
            {
                Black_Foreground.IsChecked = false;
            }
            if (Selected == 6)
            {
                Pink_Foreground.IsChecked = true;
            }
            else
            {
                Pink_Foreground.IsChecked = false;
            }
            if (Selected == 7)
            {
                Violet_Foreground.IsChecked = true;
            }
            else
            {
                Violet_Foreground.IsChecked = false;
            }
            if (Selected == 8)
            {
                White_Foreground.IsChecked = true;
            }
            else
            {
                White_Foreground.IsChecked = false;
            }
        }

        //X-Axis Color
        private void Green_XAxis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FF00FF17"));
            Graph.Render();
            XAxis_Color_Select(0);
        }

        private void Blue_XAxis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FF00C0FF"));
            Graph.Render();
            XAxis_Color_Select(1);
        }

        private void Red_XAxis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FFFF0000"));
            Graph.Render();
            XAxis_Color_Select(2);
        }

        private void Yellow_XAxis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FFFFFF00"));
            Graph.Render();
            XAxis_Color_Select(3);
        }

        private void Orange_XAxis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FFFF8C00"));
            Graph.Render();
            XAxis_Color_Select(4);
        }

        private void Black_XAxis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FF000000"));
            Graph.Render();
            XAxis_Color_Select(5);
        }

        private void Pink_XAxis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FFFF1493"));
            Graph.Render();
            XAxis_Color_Select(6);
        }

        private void Violet_XAxis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FF9400D3"));
            Graph.Render();
            XAxis_Color_Select(7);
        }

        private void White_XAxis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FFFFFFFF"));
            Graph.Render();
            XAxis_Color_Select(8);
        }

        private void XAxis_Color_Select(int Selected)
        {
            if (Selected == 0)
            {
                Green_XAxis.IsChecked = true;
            }
            else
            {
                Green_XAxis.IsChecked = false;
            }
            if (Selected == 1)
            {
                Blue_XAxis.IsChecked = true;
            }
            else
            {
                Blue_XAxis.IsChecked = false;
            }
            if (Selected == 2)
            {
                Red_XAxis.IsChecked = true;
            }
            else
            {
                Red_XAxis.IsChecked = false;
            }
            if (Selected == 3)
            {
                Yellow_XAxis.IsChecked = true;
            }
            else
            {
                Yellow_XAxis.IsChecked = false;
            }
            if (Selected == 4)
            {
                Orange_XAxis.IsChecked = true;
            }
            else
            {
                Orange_XAxis.IsChecked = false;
            }
            if (Selected == 5)
            {
                Black_XAxis.IsChecked = true;
            }
            else
            {
                Black_XAxis.IsChecked = false;
            }
            if (Selected == 6)
            {
                Pink_XAxis.IsChecked = true;
            }
            else
            {
                Pink_XAxis.IsChecked = false;
            }
            if (Selected == 7)
            {
                Violet_XAxis.IsChecked = true;
            }
            else
            {
                Violet_XAxis.IsChecked = false;
            }
            if (Selected == 8)
            {
                White_XAxis.IsChecked = true;
            }
            else
            {
                White_XAxis.IsChecked = false;
            }
        }

        //Y-Axis Color
        private void Green_YAxis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.YAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FF00FF17"));
            Graph.Render();
            YAxis_Color_Select(0);
        }

        private void Blue_YAxis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.YAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FF00C0FF"));
            Graph.Render();
            YAxis_Color_Select(1);
        }

        private void Red_YAxis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.YAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FFFF0000"));
            Graph.Render();
            YAxis_Color_Select(2);
        }

        private void Yellow_YAxis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.YAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FFFFFF00"));
            Graph.Render();
            YAxis_Color_Select(3);
        }

        private void Orange_YAxis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.YAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FFFF8C00"));
            Graph.Render();
            YAxis_Color_Select(4);
        }

        private void Black_YAxis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.YAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FF000000"));
            Graph.Render();
            YAxis_Color_Select(5);
        }

        private void Pink_YAxis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.YAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FFFF1493"));
            Graph.Render();
            YAxis_Color_Select(6);
        }

        private void Violet_YAxis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.YAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FF9400D3"));
            Graph.Render();
            YAxis_Color_Select(7);
        }

        private void White_YAxis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.YAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FFFFFFFF"));
            Graph.Render();
            YAxis_Color_Select(8);
        }

        private void YAxis_Color_Select(int Selected)
        {
            if (Selected == 0)
            {
                Green_YAxis.IsChecked = true;
            }
            else
            {
                Green_YAxis.IsChecked = false;
            }
            if (Selected == 1)
            {
                Blue_YAxis.IsChecked = true;
            }
            else
            {
                Blue_YAxis.IsChecked = false;
            }
            if (Selected == 2)
            {
                Red_YAxis.IsChecked = true;
            }
            else
            {
                Red_YAxis.IsChecked = false;
            }
            if (Selected == 3)
            {
                Yellow_YAxis.IsChecked = true;
            }
            else
            {
                Yellow_YAxis.IsChecked = false;
            }
            if (Selected == 4)
            {
                Orange_YAxis.IsChecked = true;
            }
            else
            {
                Orange_YAxis.IsChecked = false;
            }
            if (Selected == 5)
            {
                Black_YAxis.IsChecked = true;
            }
            else
            {
                Black_YAxis.IsChecked = false;
            }
            if (Selected == 6)
            {
                Pink_YAxis.IsChecked = true;
            }
            else
            {
                Pink_YAxis.IsChecked = false;
            }
            if (Selected == 7)
            {
                Violet_YAxis.IsChecked = true;
            }
            else
            {
                Violet_YAxis.IsChecked = false;
            }
            if (Selected == 8)
            {
                White_YAxis.IsChecked = true;
            }
            else
            {
                White_YAxis.IsChecked = false;
            }
        }

        //Grid Color
        private void Gray_Grid_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Grid(color: System.Drawing.ColorTranslator.FromHtml("#59808080"));
            Graph.Render();
            Grid_Color_Select(0);
        }

        private void Green_Grid_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Grid(color: System.Drawing.ColorTranslator.FromHtml("#7F00FF17"));
            Graph.Render();
            Grid_Color_Select(1);
        }

        private void Blue_Grid_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Grid(color: System.Drawing.ColorTranslator.FromHtml("#7F00C0FF"));
            Graph.Render();
            Grid_Color_Select(2);
        }

        private void Red_Grid_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Grid(color: System.Drawing.ColorTranslator.FromHtml("#7FFF0000"));
            Graph.Render();
            Grid_Color_Select(3);
        }

        private void Yellow_Grid_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Grid(color: System.Drawing.ColorTranslator.FromHtml("#7FFFFF00"));
            Graph.Render();
            Grid_Color_Select(4);
        }

        private void Orange_Grid_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Grid(color: System.Drawing.ColorTranslator.FromHtml("#7FFF8C00"));
            Graph.Render();
            Grid_Color_Select(5);
        }

        private void Black_Grid_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Grid(color: System.Drawing.ColorTranslator.FromHtml("#7F000000"));
            Graph.Render();
            Grid_Color_Select(6);
        }

        private void Pink_Grid_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Grid(color: System.Drawing.ColorTranslator.FromHtml("#7FFF1493"));
            Graph.Render();
            Grid_Color_Select(7);
        }

        private void Violet_Grid_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Grid(color: System.Drawing.ColorTranslator.FromHtml("#7F9400D3"));
            Graph.Render();
            Grid_Color_Select(8);
        }

        private void White_Grid_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Grid(color: System.Drawing.ColorTranslator.FromHtml("#7FFFFFFF"));
            Graph.Render();
            Grid_Color_Select(9);
        }

        private void Grid_Color_Select(int Selected)
        {
            if (Selected == 0)
            {
                Gray_Grid.IsChecked = true;
            }
            else
            {
                Gray_Grid.IsChecked = false;
            }
            if (Selected == 1)
            {
                Green_Grid.IsChecked = true;
            }
            else
            {
                Green_Grid.IsChecked = false;
            }
            if (Selected == 2)
            {
                Blue_Grid.IsChecked = true;
            }
            else
            {
                Blue_Grid.IsChecked = false;
            }
            if (Selected == 3)
            {
                Red_Grid.IsChecked = true;
            }
            else
            {
                Red_Grid.IsChecked = false;
            }
            if (Selected == 4)
            {
                Yellow_Grid.IsChecked = true;
            }
            else
            {
                Yellow_Grid.IsChecked = false;
            }
            if (Selected == 5)
            {
                Orange_Grid.IsChecked = true;
            }
            else
            {
                Orange_Grid.IsChecked = false;
            }
            if (Selected == 6)
            {
                Black_Grid.IsChecked = true;
            }
            else
            {
                Black_Grid.IsChecked = false;
            }
            if (Selected == 7)
            {
                Pink_Grid.IsChecked = true;
            }
            else
            {
                Pink_Grid.IsChecked = false;
            }
            if (Selected == 8)
            {
                Violet_Grid.IsChecked = true;
            }
            else
            {
                Violet_Grid.IsChecked = false;
            }
            if (Selected == 9)
            {
                White_Grid.IsChecked = true;
            }
            else
            {
                White_Grid.IsChecked = false;
            }
        }

        //Themes
        private void Default_Theme_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(ScottPlot.Style.Default);
            Graph.Render();
            Theme_Select(0);
            Grid_Color_Select(99);
            YAxis_Color_Select(99);
            XAxis_Color_Select(99);
            Foreground_Color_Select(99);
            Background_Color_Select(99);
        }

        private void Black_Theme_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(ScottPlot.Style.Black);
            Graph.Plot.YAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FFFFFFFF"));
            Graph.Plot.XAxis.Color(color: System.Drawing.ColorTranslator.FromHtml("#FFFFFFFF"));
            Graph.Render();
            Theme_Select(1);
            Grid_Color_Select(99);
            YAxis_Color_Select(99);
            XAxis_Color_Select(99);
            Foreground_Color_Select(99);
            Background_Color_Select(99);
        }

        private void Blue_Theme_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(ScottPlot.Style.Blue1);
            Graph.Render();
            Theme_Select(2);
            Grid_Color_Select(99);
            YAxis_Color_Select(99);
            XAxis_Color_Select(99);
            Foreground_Color_Select(99);
            Background_Color_Select(99);
        }

        private void Gray_Theme_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(ScottPlot.Style.Gray1);
            Graph.Render();
            Theme_Select(3);
            Grid_Color_Select(99);
            YAxis_Color_Select(99);
            XAxis_Color_Select(99);
            Foreground_Color_Select(99);
            Background_Color_Select(99);
        }

        private void GrayBlack_Theme_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Style(ScottPlot.Style.Gray2);
            Graph.Render();
            Theme_Select(4);
            Grid_Color_Select(99);
            YAxis_Color_Select(99);
            XAxis_Color_Select(99);
            Foreground_Color_Select(99);
            Background_Color_Select(99);
        }

        private void Theme_Select(int Selected)
        {
            if (Selected == 0)
            {
                Default_Theme.IsChecked = true;
            }
            else
            {
                Default_Theme.IsChecked = false;
            }
            if (Selected == 1)
            {
                Black_Theme.IsChecked = true;
            }
            else
            {
                Black_Theme.IsChecked = false;
            }
            if (Selected == 2)
            {
                Blue_Theme.IsChecked = true;
            }
            else
            {
                Blue_Theme.IsChecked = false;
            }
            if (Selected == 3)
            {
                Gray_Theme.IsChecked = true;
            }
            else
            {
                Gray_Theme.IsChecked = false;
            }
            if (Selected == 4)
            {
                GrayBlack_Theme.IsChecked = true;
            }
            else
            {
                GrayBlack_Theme.IsChecked = false;
            }
        }

        //-----------------------Graph Colors End-----------------------------------------------------

        private void Draggable_Horizontal_Marker_1_Click(object sender, RoutedEventArgs e)
        {
            if (Draggable_Horizontal_Marker_1.IsChecked == true)
            {
                Drag_HorizontalMarker_1 = Graph.Plot.AddHorizontalLine(0, color: System.Drawing.ColorTranslator.FromHtml("#FF00950E"), style: LineStyle.Dash, label: "H Marker 1");
                HorizontalMarker_1_Annotation = Graph.Plot.AddAnnotation("H Marker 1: " + Math.Round(Drag_HorizontalMarker_1.Y, 4) + Measurement_Unit, 5, 5);
                HorizontalMarker_1_Annotation.Font.Size = 14;
                HorizontalMarker_1_Annotation.Shadow = false;
                HorizontalMarker_1_Annotation.BackgroundColor = System.Drawing.ColorTranslator.FromHtml("#00FFFFFF");
                HorizontalMarker_1_Annotation.BorderColor = System.Drawing.ColorTranslator.FromHtml("#00FFFFFF");
                HorizontalMarker_1_Annotation.Font.Color = System.Drawing.ColorTranslator.FromHtml("#FF00950E");
                Drag_HorizontalMarker_1.DragEnabled = true;
                Drag_HorizontalMarker_1.Dragged += Horizontal_Marker_1_Dragged_Event;
                Insert_Log("Horizontal Draggable Marker 1 has been added. You may now drag it around. Green color Dash Line.", 0);
            }
            else
            {
                Graph.Plot.Remove(plottable: Drag_HorizontalMarker_1);
                Graph.Plot.Remove(plottable: HorizontalMarker_1_Annotation);
                Insert_Log("Horizontal Draggable Marker 1 has been removed.", 0);
            }
        }

        private void Horizontal_Marker_1_Dragged_Event(object sender, EventArgs eventArgs)
        {
            HorizontalMarker_1_Annotation.Label = "H Marker 1: " + Math.Round(Drag_HorizontalMarker_1.Y, 4) + Measurement_Unit;
        }

        private void Draggable_Horizontal_Marker_2_Click(object sender, RoutedEventArgs e)
        {
            if (Draggable_Horizontal_Marker_2.IsChecked == true)
            {
                Drag_HorizontalMarker_2 = Graph.Plot.AddHorizontalLine(0, color: System.Drawing.ColorTranslator.FromHtml("#FFFF0000"), style: LineStyle.Dash, label: "H Marker 2");
                HorizontalMarker_2_Annotation = Graph.Plot.AddAnnotation("H Marker 2: " + Math.Round(Drag_HorizontalMarker_2.Y, 4) + Measurement_Unit, 5, 20);
                HorizontalMarker_2_Annotation.Font.Size = 14;
                HorizontalMarker_2_Annotation.Shadow = false;
                HorizontalMarker_2_Annotation.BackgroundColor = System.Drawing.ColorTranslator.FromHtml("#00FFFFFF");
                HorizontalMarker_2_Annotation.BorderColor = System.Drawing.ColorTranslator.FromHtml("#00FFFFFF");
                HorizontalMarker_2_Annotation.Font.Color = System.Drawing.ColorTranslator.FromHtml("#FFFF0000");
                Drag_HorizontalMarker_2.DragEnabled = true;
                Drag_HorizontalMarker_2.Dragged += Horizontal_Marker_2_Dragged_Event;
                Insert_Log("Horizontal Draggable Marker 2 has been added. You may now drag it around. Red color Dash Line.", 0);
            }
            else
            {
                Graph.Plot.Remove(plottable: Drag_HorizontalMarker_2);
                Graph.Plot.Remove(plottable: HorizontalMarker_2_Annotation);
                Insert_Log("Horizontal Draggable Marker 2 has been removed.", 0);
            }
        }

        private void Horizontal_Marker_2_Dragged_Event(object sender, EventArgs eventArgs)
        {
            HorizontalMarker_2_Annotation.Label = "H Marker 2: " + Math.Round(Drag_HorizontalMarker_2.Y, 4) + Measurement_Unit;
        }

        private void Add_H_Marker_Click(object sender, RoutedEventArgs e)
        {
            (bool isNum, double value) = Text_Num(H_Marker_Number.Text, true, false);
            if (isNum == true)
            {
                Graph.Plot.AddHorizontalLine(value, label: "H Marker " + Math.Round(value, 2));
                H_Marker_Number.Text = string.Empty;
            }
            else
            {
                H_Marker_Number.Text = string.Empty;
                Insert_Log("Adding a Fixed Horizontal marker failed. Marker value must be a real number.", 1);
            }
        }

        private void Draggable_Vertical_Marker_1_Click(object sender, RoutedEventArgs e)
        {
            if (Draggable_Vertical_Marker_1.IsChecked == true)
            {
                Drag_VerticalMarker_1 = Graph.Plot.AddVerticalLine(0, color: System.Drawing.ColorTranslator.FromHtml("#FF00950E"), style: LineStyle.Dash, label: "V Marker 1");
                VerticalMarker_1_Annotation = Graph.Plot.AddAnnotation("V Marker 1: " + Math.Round(Drag_VerticalMarker_1.X, 0), 5, 35);
                VerticalMarker_1_Annotation.Font.Size = 14;
                VerticalMarker_1_Annotation.Shadow = false;
                VerticalMarker_1_Annotation.BackgroundColor = System.Drawing.ColorTranslator.FromHtml("#00FFFFFF");
                VerticalMarker_1_Annotation.BorderColor = System.Drawing.ColorTranslator.FromHtml("#00FFFFFF");
                VerticalMarker_1_Annotation.Font.Color = System.Drawing.ColorTranslator.FromHtml("#FF00950E");
                Drag_VerticalMarker_1.DragEnabled = true;
                Drag_VerticalMarker_1.Dragged += Vertical_Marker_1_Dragged_Event;
                Insert_Log("Vertical Draggable Marker 1 has been added. You may now drag it around. Green color Dash Line.", 0);
            }
            else
            {
                Graph.Plot.Remove(plottable: Drag_VerticalMarker_1);
                Graph.Plot.Remove(plottable: VerticalMarker_1_Annotation);
                Insert_Log("Vertical Draggable Marker 1 has been removed.", 0);
            }
        }

        private void Vertical_Marker_1_Dragged_Event(object sender, EventArgs eventArgs)
        {
            int Value = (int)Drag_VerticalMarker_1.X;
            VerticalMarker_1_Annotation.Label = "V Marker 1: " + Value;
            if (Vertical_Markers_to_StartStop_text_field.IsChecked == true)
            {
                string value_text = Value.ToString();
                Start_Statistics_NSamples_TextBox.Text = value_text;
                Start_Math_NSamples_TextBox.Text = value_text;
                Start_Histogram_NSamples_TextBox.Text = value_text;
                Start_TimeDifference_NSamples_TextBox.Text = value_text;
            }
        }

        private void Draggable_Vertical_Marker_2_Click(object sender, RoutedEventArgs e)
        {
            if (Draggable_Vertical_Marker_2.IsChecked == true)
            {
                Drag_VerticalMarker_2 = Graph.Plot.AddVerticalLine(0, color: System.Drawing.ColorTranslator.FromHtml("#FFFF0000"), style: LineStyle.Dash, label: "V Marker 2");
                VerticalMarker_2_Annotation = Graph.Plot.AddAnnotation("V Marker 2: " + Math.Round(Drag_VerticalMarker_2.X, 0), 5, 50);
                VerticalMarker_2_Annotation.Font.Size = 14;
                VerticalMarker_2_Annotation.Shadow = false;
                VerticalMarker_2_Annotation.BackgroundColor = System.Drawing.ColorTranslator.FromHtml("#00FFFFFF");
                VerticalMarker_2_Annotation.BorderColor = System.Drawing.ColorTranslator.FromHtml("#00FFFFFF");
                VerticalMarker_2_Annotation.Font.Color = System.Drawing.ColorTranslator.FromHtml("#FFFF0000");
                Drag_VerticalMarker_2.DragEnabled = true;
                Drag_VerticalMarker_2.Dragged += Vertical_Marker_2_Dragged_Event;
                Insert_Log("Vertical Draggable Marker 2 has been added. You may now drag it around. Red color Dash Line.", 0);
            }
            else
            {
                Graph.Plot.Remove(plottable: Drag_VerticalMarker_2);
                Graph.Plot.Remove(plottable: VerticalMarker_2_Annotation);
                Insert_Log("Vertical Draggable Marker 2 has been removed.", 0);
            }
        }

        private void Vertical_Marker_2_Dragged_Event(object sender, EventArgs eventArgs)
        {
            int Value = (int)Drag_VerticalMarker_2.X;
            VerticalMarker_2_Annotation.Label = "V Marker 2: " + Value;
            if (Vertical_Markers_to_StartStop_text_field.IsChecked == true)
            {
                string value_text = Value.ToString();
                End_Statistics_NSamples_TextBox.Text = value_text;
                End_Math_NSamples_TextBox.Text = value_text;
                End_Histogram_NSamples_TextBox.Text = value_text;
                End_TimeDifference_NSamples_TextBox.Text = value_text;
            }
        }

        private void Add_V_Marker_Click(object sender, RoutedEventArgs e)
        {
            (bool isNum, double value) = Text_Num(V_Marker_Number.Text, true, false);
            if (isNum == true)
            {
                Graph.Plot.AddVerticalLine(value, label: "V Marker " + Math.Round(value, 2));
                V_Marker_Number.Text = string.Empty;
            }
            else
            {
                V_Marker_Number.Text = string.Empty;
                Insert_Log("Adding a Fixed Vertical marker failed. Marker value must be a real number.", 1);
            }
        }

        private void Clear_Horizontal_Markers_Click(object sender, RoutedEventArgs e)
        {
            Clear_All_Horizontal_Markers();
            Insert_Log("All Horizontal Markers have been cleared.", 0);
        }

        private void Clear_All_Horizontal_Markers()
        {
            System.Drawing.Color MouseTrackerColor = System.Drawing.Color.Red;
            LineStyle MouseTrackerStyle = LineStyle.DashDot;

            if (ShowMouseTracker == true)
            {
                MouseTrackerColor = Mouse_HLine.Color;
                MouseTrackerStyle = Mouse_HLine.LineStyle;
            }

            Graph.Plot.Clear(typeof(ScottPlot.Plottable.HLine));

            if (Draggable_Horizontal_Marker_1.IsChecked == true)
            {
                Graph.Plot.Remove(plottable: HorizontalMarker_1_Annotation);
                Draggable_Horizontal_Marker_1.IsChecked = false;
            }
            if (Draggable_Horizontal_Marker_2.IsChecked == true)
            {
                Graph.Plot.Remove(plottable: HorizontalMarker_2_Annotation);
                Draggable_Horizontal_Marker_2.IsChecked = false;
            }
            if (ShowMouseTracker == true)
            {
                Mouse_HLine = Graph.Plot.AddHorizontalLine(0, color: MouseTrackerColor, style: MouseTrackerStyle);
            }
        }

        private void Clear_Vertical_Markers_Click(object sender, RoutedEventArgs e)
        {
            Clear_All_Vertical_Markers();
            Insert_Log("All Vertical Markers have been cleared.", 0);
        }

        private void Clear_All_Vertical_Markers()
        {
            System.Drawing.Color MouseTrackerColor = System.Drawing.Color.Red;
            LineStyle MouseTrackerStyle = LineStyle.DashDot;

            if (ShowMouseTracker == true)
            {
                MouseTrackerColor = Mouse_VLine.Color;
                MouseTrackerStyle = Mouse_VLine.LineStyle;
            }

            Graph.Plot.Clear(typeof(ScottPlot.Plottable.VLine));

            if (Draggable_Vertical_Marker_1.IsChecked == true)
            {
                Graph.Plot.Remove(plottable: VerticalMarker_1_Annotation);
                Draggable_Vertical_Marker_1.IsChecked = false;
            }
            if (Draggable_Vertical_Marker_2.IsChecked == true)
            {
                Graph.Plot.Remove(plottable: VerticalMarker_2_Annotation);
                Draggable_Vertical_Marker_2.IsChecked = false;
            }
            if (ShowMouseTracker == true)
            {
                Mouse_VLine = Graph.Plot.AddVerticalLine(0, color: MouseTrackerColor, style: MouseTrackerStyle);
            }
        }

        private void Clear_All_Markers_Click(object sender, RoutedEventArgs e)
        {
            Clear_All_Horizontal_Markers();
            Clear_All_Vertical_Markers();
            Tracker_Color_Selector(2);
            Tracker_Style_Selector(1);
            Insert_Log("All Markers have been cleared.", 0);
        }

        private void Show_Tracker_Click(object sender, RoutedEventArgs e)
        {
            if (Show_Tracker.IsChecked == true)
            {
                Mouse_HLine = Graph.Plot.AddHorizontalLine(0, color: System.Drawing.Color.Red, style: LineStyle.DashDot);
                Mouse_VLine = Graph.Plot.AddVerticalLine(0, color: System.Drawing.Color.Red, style: LineStyle.DashDot);
                Tracker_Color_Selector(2);
                Tracker_Style_Selector(1);
                ShowMouseTracker = true;
                Auto_Axis_Enable.IsChecked = false;
                Tracker_Mouse_Status_Bar.Fill = Brushes.Green;
                Insert_Log("Mouser Tracker has been enabled. Auto Axis is disabled.", 0);
                Information_Tab.IsSelected = true;
            }
            else
            {
                ShowMouseTracker = false;
                Graph.Plot.Remove(plottable: Mouse_HLine);
                Graph.Plot.Remove(plottable: Mouse_VLine);
                if (ShowMouseCoordinates == true)
                {
                    ShowMouseCoordinates = false;
                    Show_XY_Coordinates_Tracker.IsChecked = false;
                    Graph.Plot.Remove(plottable: MouseCoordinates);
                }

                if (ShowMouseMeasurementData == true)
                {
                    ShowMouseMeasurementData = false;
                    Show_Measurement_Data_Tracker.IsChecked = false;
                    Graph.Plot.Remove(plottable: MouseMeasurementData);
                }
                Auto_Axis_Enable.IsChecked = true;
                Insert_Log("Mouser Tracker has been disabled. Auto Axis is enabled.", 0);
                Tracker_Mouse_Status_Bar.Fill = Brushes.Red;
                Sample_Label.Content = "null";
                Sample_Label_Unit.Content = "";
                Sample_DateTime_Label.Content = "null";
                Sample_Number_Label.Content = "null";
            }
        }

        private void Show_XY_Coordinates_Tracker_Click(object sender, RoutedEventArgs e)
        {
            if (Show_XY_Coordinates_Tracker.IsChecked == true & ShowMouseTracker == true)
            {
                (double X_MouseCoordinate, double Y_MouseCoordinate) = Graph.GetMouseCoordinates();
                MouseCoordinates = Graph.Plot.AddText("null, null", X_MouseCoordinate, Y_MouseCoordinate, color: Mouse_HLine.Color);
                MouseCoordinates.Alignment = Alignment.UpperRight;
                MouseCoordinates.FontSize = 12;
                ShowMouseCoordinates = true;
            }
            else
            {
                ShowMouseCoordinates = false;
                Show_XY_Coordinates_Tracker.IsChecked = false;
                Graph.Plot.Remove(plottable: MouseCoordinates);
            }
        }

        private void Show_Measurement_Data_Tracker_Click(object sender, RoutedEventArgs e)
        {
            if (Show_Measurement_Data_Tracker.IsChecked == true & ShowMouseTracker == true)
            {
                (double X_MouseCoordinate, double Y_MouseCoordinate) = Graph.GetMouseCoordinates();
                MouseMeasurementData = Graph.Plot.AddText("null, null", X_MouseCoordinate, Y_MouseCoordinate, color: Mouse_HLine.Color);
                MouseMeasurementData.Alignment = Alignment.LowerLeft;
                MouseMeasurementData.FontSize = 14;
                ShowMouseMeasurementData = true;
            }
            else
            {
                ShowMouseMeasurementData = false;
                Show_Measurement_Data_Tracker.IsChecked = false;
                Graph.Plot.Remove(plottable: MouseMeasurementData);
            }
        }

        private void Graph_MouseMove(object sender, MouseEventArgs e)
        {
            if (ShowMouseTracker == true)
            {
                (double X_MouseCoordinate, double Y_MouseCoordinate) = Graph.GetMouseCoordinates();
                int xIndex = (int)X_MouseCoordinate;

                Mouse_VLine.X = X_MouseCoordinate;
                Mouse_HLine.Y = Y_MouseCoordinate;

                bool xIndex_Valid = (xIndex >= 0 && xIndex < Measurement_Count);

                if (ShowMouseMeasurementData == true)
                {
                    if (xIndex_Valid)
                    {
                        MouseMeasurementData.Label = Measurement_DateTime[xIndex].ToString() + ", " + ((decimal)(Measurement_Data[xIndex])).ToString() + Measurement_Unit;
                    }
                    else
                    {
                        MouseMeasurementData.Label = "null";
                    }
                    MouseMeasurementData.X = X_MouseCoordinate;
                    MouseMeasurementData.Y = Y_MouseCoordinate;
                }

                if (ShowMouseCoordinates == true)
                {
                    MouseCoordinates.Label = xIndex + ", " + Math.Round(Y_MouseCoordinate, 6);
                    MouseCoordinates.X = X_MouseCoordinate;
                    MouseCoordinates.Y = Y_MouseCoordinate;
                }

                if (xIndex_Valid)
                {
                    Sample_Label.Content = ((decimal)(Measurement_Data[xIndex]));
                    Sample_Label_Unit.Content = Measurement_Unit;
                    Sample_DateTime_Label.Content = Measurement_DateTime[xIndex];
                    Sample_Number_Label.Content = xIndex;
                }
                else
                {
                    Sample_Label.Content = "null";
                    Sample_Label_Unit.Content = "";
                    Sample_DateTime_Label.Content = "null";
                    Sample_Number_Label.Content = "null";
                }

                Graph.Render();
            }
        }

        private void Set_Tracker_Color(string Color, string ColorName, int ColorNum)
        {
            if (ShowMouseTracker == true)
            {
                Mouse_VLine.Color = System.Drawing.ColorTranslator.FromHtml(Color);
                Mouse_HLine.Color = System.Drawing.ColorTranslator.FromHtml(Color);
                if (ShowMouseMeasurementData == true)
                {
                    MouseMeasurementData.Color = System.Drawing.ColorTranslator.FromHtml(Color);
                }

                if (ShowMouseCoordinates == true)
                {
                    MouseCoordinates.Color = System.Drawing.ColorTranslator.FromHtml(Color);
                }
                Graph.Render();
                Insert_Log("Mouse Tracker Color set to " + ColorName + ".", 0);
                Tracker_Color_Selector(ColorNum);
            }
            else
            {
                Tracker_Color_Selector(9);
                Insert_Log("Cannot change Mouse Tracker Color. Mouse Tracker is not enabled.", 1);
            }
        }

        private void Green_Tracker_Click(object sender, RoutedEventArgs e)
        {
            Set_Tracker_Color("#FF00FF17", "Green", 0);
        }

        private void Blue_Tracker_Click(object sender, RoutedEventArgs e)
        {
            Set_Tracker_Color("#FF00C0FF", "Blue", 1);
        }

        private void Red_Tracker_Click(object sender, RoutedEventArgs e)
        {
            Set_Tracker_Color("#FFFF0000", "Red", 2);
        }

        private void Yellow_Tracker_Click(object sender, RoutedEventArgs e)
        {
            Set_Tracker_Color("#FFFFFF00", "Yellow", 3);
        }

        private void Orange_Tracker_Click(object sender, RoutedEventArgs e)
        {
            Set_Tracker_Color("#FFFF8C00", "Orange", 4);
        }

        private void Black_Tracker_Click(object sender, RoutedEventArgs e)
        {
            Set_Tracker_Color("#FF000000", "Black", 5);
        }

        private void Pink_Tracker_Click(object sender, RoutedEventArgs e)
        {
            Set_Tracker_Color("#FFFF1493", "Pink", 6);
        }

        private void Violet_Tracker_Click(object sender, RoutedEventArgs e)
        {
            Set_Tracker_Color("#FF9400D3", "Violet", 7);
        }

        private void White_Tracker_Click(object sender, RoutedEventArgs e)
        {
            Set_Tracker_Color("#FFFFFFFF", "White", 8);
        }

        private void Tracker_Color_Selector(int Tracker_Color)
        {
            if (Tracker_Color == 0)
            {
                Green_Tracker.IsChecked = true;
            }
            else
            {
                Green_Tracker.IsChecked = false;
            }
            if (Tracker_Color == 1)
            {
                Blue_Tracker.IsChecked = true;
            }
            else
            {
                Blue_Tracker.IsChecked = false;
            }
            if (Tracker_Color == 2)
            {
                Red_Tracker.IsChecked = true;
            }
            else
            {
                Red_Tracker.IsChecked = false;
            }
            if (Tracker_Color == 3)
            {
                Yellow_Tracker.IsChecked = true;
            }
            else
            {
                Yellow_Tracker.IsChecked = false;
            }
            if (Tracker_Color == 4)
            {
                Orange_Tracker.IsChecked = true;
            }
            else
            {
                Orange_Tracker.IsChecked = false;
            }
            if (Tracker_Color == 5)
            {
                Black_Tracker.IsChecked = true;
            }
            else
            {
                Black_Tracker.IsChecked = false;
            }
            if (Tracker_Color == 6)
            {
                Pink_Tracker.IsChecked = true;
            }
            else
            {
                Pink_Tracker.IsChecked = false;
            }
            if (Tracker_Color == 7)
            {
                Violet_Tracker.IsChecked = true;
            }
            else
            {
                Violet_Tracker.IsChecked = false;
            }
            if (Tracker_Color == 8)
            {
                White_Tracker.IsChecked = true;
            }
            else
            {
                White_Tracker.IsChecked = false;
            }
        }

        private void Tracker_Style_Dotted_Click(object sender, RoutedEventArgs e)
        {
            if (ShowMouseTracker == true)
            {
                Mouse_VLine.LineStyle = LineStyle.Dot;
                Mouse_HLine.LineStyle = LineStyle.Dot;
                Insert_Log("Mouse Tracker Style set to Dotted.", 0);
                Tracker_Style_Selector(0);
            }
            else
            {
                Insert_Log("Cannot set Mouse Tracker Style. Mouse Tracker not enabled.", 2);
            }
        }

        private void Tracker_Style_Dot_Dash_Click(object sender, RoutedEventArgs e)
        {
            if (ShowMouseTracker == true)
            {
                Mouse_VLine.LineStyle = LineStyle.DashDot;
                Mouse_HLine.LineStyle = LineStyle.DashDot;
                Insert_Log("Mouse Tracker Style set to Dot Dash.", 0);
                Tracker_Style_Selector(1);
            }
            else
            {
                Insert_Log("Cannot set Mouse Tracker Style. Mouse Tracker not enabled.", 2);
            }
        }

        private void Tracker_Style_Solid_Click(object sender, RoutedEventArgs e)
        {
            if (ShowMouseTracker == true)
            {
                Mouse_VLine.LineStyle = LineStyle.Solid;
                Mouse_HLine.LineStyle = LineStyle.Solid;
                Insert_Log("Mouse Tracker Style set to Solid.", 0);
                Tracker_Style_Selector(2);
            }
            else
            {
                Insert_Log("Cannot set Mouse Tracker Style. Mouse Tracker not enabled.", 2);
            }
        }

        private void Tracker_Style_Selector(int Style)
        {
            if (Style == 0)
            {
                Tracker_Style_Dotted.IsChecked = true;
            }
            else
            {
                Tracker_Style_Dotted.IsChecked = false;
            }
            if (Style == 1)
            {
                Tracker_Style_Dot_Dash.IsChecked = true;
            }
            else
            {
                Tracker_Style_Dot_Dash.IsChecked = false;
            }
            if (Style == 2)
            {
                Tracker_Style_Solid.IsChecked = true;
            }
            else
            {
                Tracker_Style_Solid.IsChecked = false;
            }
        }

        private void Auto_Scroll_Click(object sender, RoutedEventArgs e)
        {
            if (Auto_Scroll.IsChecked == true)
            {
                Insert_Log("Output Log Auto Scroll Enabled.", 0);
            }
            else
            {
                Insert_Log("Output Log Auto Scroll Disabled.", 0);
            }
        }

        private void Auto_Clear_20_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Auto_Clear_Output_Log_Count, 20);
            Insert_Log("Output Log will be cleared after " + Auto_Clear_Output_Log_Count + " logs are inserted into it.", 0);
            Auto_Clear_20.IsChecked = true;
            Auto_Clear_40.IsChecked = false;
            Auto_Clear_80.IsChecked = false;
            Auto_Clear_100.IsChecked = false;
        }

        private void Auto_Clear_40_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Auto_Clear_Output_Log_Count, 40);
            Insert_Log("Output Log will be cleared after " + Auto_Clear_Output_Log_Count + " logs are inserted into it.", 0);
            Auto_Clear_20.IsChecked = false;
            Auto_Clear_40.IsChecked = true;
            Auto_Clear_80.IsChecked = false;
            Auto_Clear_100.IsChecked = false;
        }

        private void Auto_Clear_80_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Auto_Clear_Output_Log_Count, 80);
            Insert_Log("Output Log will be cleared after " + Auto_Clear_Output_Log_Count + " logs are inserted into it.", 0);
            Auto_Clear_20.IsChecked = false;
            Auto_Clear_40.IsChecked = false;
            Auto_Clear_80.IsChecked = true;
            Auto_Clear_100.IsChecked = false;
        }

        private void Auto_Clear_100_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Auto_Clear_Output_Log_Count, 100);
            Insert_Log("Output Log will be cleared after " + Auto_Clear_Output_Log_Count + " logs are inserted into it.", 0);
            Auto_Clear_20.IsChecked = false;
            Auto_Clear_40.IsChecked = false;
            Auto_Clear_80.IsChecked = false;
            Auto_Clear_100.IsChecked = true;
        }

        private void Save_Log_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var Save_Data_Text_Window = new SaveFileDialog
                {
                    FileName = "Graph Output Log_" + DateTime.Now.ToString("yyyy-MM-dd h-mm-ss tt") + ".txt",
                    Filter = "Normal text Files (*.txt)|*.txt;*.txt" +
                      "|All files (*.*)|*.*"
                };

                if (Save_Data_Text_Window.ShowDialog() is true)
                {
                    using (TextWriter datatotxt = new StreamWriter(Save_Data_Text_Window.FileName, false))
                    {
                        datatotxt.WriteLine(String.Join(String.Empty, Output_Log.Inlines.Select(line => line.ContentStart.GetTextInRun(LogicalDirection.Forward))).ToString());
                    }
                }
            }
            catch (Exception Ex)
            {
                Insert_Log("Could not save Graph Output Log to text file.", 1);
                Insert_Log(Ex.Message, 1);
            }
        }

        private void Clear_Log_Click(object sender, RoutedEventArgs e)
        {
            this.Dispatcher.Invoke(DispatcherPriority.Normal, new ThreadStart(delegate
            {
                Output_Log.Text = String.Empty;
                Output_Log.Inlines.Clear();
            }));
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            Insert_Log("Please email me if you encounter any bugs or need help.", 5);
            Insert_Log("Email: niravkp97@gmail.com", 5);
        }

        private void Credits_Click(object sender, RoutedEventArgs e)
        {
            Insert_Log("Graphing Module 1.0.0", 5);
            Insert_Log("Created by Nirav Patel.", 5);
            Insert_Log("Email: niravkp97@gmail.com", 5);
        }

        //--------------------------- Menu Code End --------------------------------

        //--------------------------- Statistics (All Samples)----------------------

        private void Mean_AllSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    int Measurement_Count_Copy = Measurement_Count;
                    double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                    Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);
                    double Mean = ArrayStatistics.Mean(Measurement_Data_Copy);
                    Insert_Log("[All Samples (" + 0 + ", " + (Measurement_Count_Copy - 1) + ")]" + "  Calculated Mean (Average): " + Mean + " " + Measurement_Unit, 3);
                    Measurement_Data_Copy = null;
                }
                catch (Exception Ex)
                {
                    Insert_Log(Ex.Message, 1);
                    Insert_Log("Could not calculate Mean (Average) for All Samples. Try again.", 1);
                }
            });
        }

        private void StdDeviation_AllSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    int Measurement_Count_Copy = Measurement_Count;
                    double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                    Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);
                    double StdDeviation = ArrayStatistics.StandardDeviation(Measurement_Data_Copy);
                    Insert_Log("[All Samples (" + 0 + ", " + (Measurement_Count_Copy - 1) + ")]" + "  Calculated Standard Deviation: " + StdDeviation + " " + Measurement_Unit, 3);
                    Measurement_Data_Copy = null;
                }
                catch (Exception Ex)
                {
                    Insert_Log(Ex.Message, 1);
                    Insert_Log("Could not calculate Standard Deviation for All Samples. Try again.", 1);
                }
            });
        }

        private void Max_AllSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    int Measurement_Count_Copy = Measurement_Count;
                    double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                    Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);
                    double Max = ArrayStatistics.Maximum(Measurement_Data_Copy);
                    Insert_Log("[All Samples (" + 0 + ", " + (Measurement_Count_Copy - 1) + ")]" + "  Found Maximum Sample: " + Max + " " + Measurement_Unit, 3);
                    Measurement_Data_Copy = null;
                }
                catch (Exception Ex)
                {
                    Insert_Log(Ex.Message, 1);
                    Insert_Log("Could not find Maximum Sample for All Samples. Try again.", 1);
                }
            });
        }

        private void Min_AllSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    int Measurement_Count_Copy = Measurement_Count;
                    double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                    Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);
                    double Min = ArrayStatistics.Minimum(Measurement_Data_Copy);
                    Insert_Log("[All Samples (" + 0 + ", " + (Measurement_Count_Copy - 1) + ")]" + "  Found Minimum Sample: " + Min + " " + Measurement_Unit, 3);
                    Measurement_Data_Copy = null;
                }
                catch (Exception Ex)
                {
                    Insert_Log(Ex.Message, 1);
                    Insert_Log("Could not find Minimum Sample for All Samples. Try again.", 1);
                }
            });
        }

        private void AbsMax_AllSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    int Measurement_Count_Copy = Measurement_Count;
                    double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                    Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);
                    double AbsMax = ArrayStatistics.MaximumAbsolute(Measurement_Data_Copy);
                    Insert_Log("[All Samples (" + 0 + ", " + (Measurement_Count_Copy - 1) + ")]" + "  Calculated Absolute Maximum: " + AbsMax + " " + Measurement_Unit, 3);
                    Measurement_Data_Copy = null;
                }
                catch (Exception Ex)
                {
                    Insert_Log(Ex.Message, 1);
                    Insert_Log("Could not calculate Absolute Maximum for All Samples. Try again.", 1);
                }
            });
        }

        private void AbsMin_AllSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    int Measurement_Count_Copy = Measurement_Count;
                    double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                    Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);
                    double AbsMin = ArrayStatistics.MinimumAbsolute(Measurement_Data_Copy);
                    Insert_Log("[All Samples (" + 0 + ", " + (Measurement_Count_Copy - 1) + ")]" + "  Calculated Absolute Minimum: " + AbsMin + " " + Measurement_Unit, 3);
                    Measurement_Data_Copy = null;
                }
                catch (Exception Ex)
                {
                    Insert_Log(Ex.Message, 1);
                    Insert_Log("Could not calculate Absolute Minimum for All Samples. Try again.", 1);
                }
            });
        }

        private void RMS_AllSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    int Measurement_Count_Copy = Measurement_Count;
                    double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                    Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);
                    double RMS = ArrayStatistics.RootMeanSquare(Measurement_Data_Copy);
                    Insert_Log("[All Samples (" + 0 + ", " + (Measurement_Count_Copy - 1) + ")]" + "  Calculated Root Mean Square: " + RMS + " " + Measurement_Unit, 3);
                    Measurement_Data_Copy = null;
                }
                catch (Exception Ex)
                {
                    Insert_Log(Ex.Message, 1);
                    Insert_Log("Could not calculate Root Mean Square for All Samples. Try again.", 1);
                }
            });
        }

        private void Variance_AllSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    int Measurement_Count_Copy = Measurement_Count;
                    double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                    Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);
                    double Variance = ArrayStatistics.Variance(Measurement_Data_Copy);
                    Insert_Log("[All Samples (" + 0 + ", " + (Measurement_Count_Copy - 1) + ")]" + "  Calculated Variance: " + Variance + " " + Measurement_Unit, 3);
                    Measurement_Data_Copy = null;
                }
                catch (Exception Ex)
                {
                    Insert_Log(Ex.Message, 1);
                    Insert_Log("Could not calculate Variance for All Samples. Try again.", 1);
                }
            });
        }

        private void GeometricMean_AllSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    int Measurement_Count_Copy = Measurement_Count;
                    double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                    Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);
                    double GeometricMean = ArrayStatistics.GeometricMean(Measurement_Data_Copy);
                    Insert_Log("[All Samples (" + 0 + ", " + (Measurement_Count_Copy - 1) + ")]" + "  Calculated Geometric Mean: " + GeometricMean + " " + Measurement_Unit, 3);
                    Measurement_Data_Copy = null;
                }
                catch (Exception Ex)
                {
                    Insert_Log(Ex.Message, 1);
                    Insert_Log("Could not calculate Geometric Mean for All Samples. Try again.", 1);
                }
            });
        }

        private void HarmonicMean_AllSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    int Measurement_Count_Copy = Measurement_Count;
                    double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                    Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);
                    double HarmonicMean = ArrayStatistics.HarmonicMean(Measurement_Data_Copy);
                    Insert_Log("[All Samples (" + 0 + ", " + (Measurement_Count_Copy - 1) + ")]" + "  Calculated Harmonic Mean: " + HarmonicMean + " " + Measurement_Unit, 3);
                    Measurement_Data_Copy = null;
                }
                catch (Exception Ex)
                {
                    Insert_Log(Ex.Message, 1);
                    Insert_Log("Could not calculate Harmonic Mean for All Samples. Try again.", 1);
                }
            });
        }

        private void PopulationVariance_AllSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    int Measurement_Count_Copy = Measurement_Count;
                    double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                    Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);
                    double PopulationVariance = ArrayStatistics.PopulationVariance(Measurement_Data_Copy);
                    Insert_Log("[All Samples (" + 0 + ", " + (Measurement_Count_Copy - 1) + ")]" + "  Calculated Population Variance: " + PopulationVariance + " " + Measurement_Unit, 3);
                    Measurement_Data_Copy = null;
                }
                catch (Exception Ex)
                {
                    Insert_Log(Ex.Message, 1);
                    Insert_Log("Could not calculate Population Variance for All Samples. Try again.", 1);
                }
            });
        }

        private void PopulationStdDeviation_AllSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    int Measurement_Count_Copy = Measurement_Count;
                    double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                    Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);
                    double PopulationStandardDeviation = ArrayStatistics.PopulationStandardDeviation(Measurement_Data_Copy);
                    Insert_Log("[All Samples (" + 0 + ", " + (Measurement_Count_Copy - 1) + ")]" + "  Calculated Population Standard Deviation: " + PopulationStandardDeviation + " " + Measurement_Unit, 3);
                    Measurement_Data_Copy = null;
                }
                catch (Exception Ex)
                {
                    Insert_Log(Ex.Message, 1);
                    Insert_Log("Could not calculate Population Standard Deviation for All Samples. Try again.", 1);
                }
            });
        }

        private void MeanStdDeviation_AllSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    int Measurement_Count_Copy = Measurement_Count;
                    double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                    Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);
                    (double ArithmeticSampleMean, double UnbiasedPopulationStandardDeviation) = ArrayStatistics.MeanStandardDeviation(Measurement_Data_Copy);
                    Insert_Log("[All Samples (" + 0 + ", " + (Measurement_Count_Copy - 1) + ")]" + "  Arithmetic Sample Mean: " + ArithmeticSampleMean + " " + Measurement_Unit + "  Unbiased Pop Std Deviation: " + UnbiasedPopulationStandardDeviation + " " + Measurement_Unit, 3);
                    Measurement_Data_Copy = null;
                }
                catch (Exception Ex)
                {
                    Insert_Log(Ex.Message, 1);
                    Insert_Log("Could not calculate Mean Standard Deviation for All Samples. Try again.", 1);
                }
            });
        }

        private void MeanVariance_AllSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    int Measurement_Count_Copy = Measurement_Count;
                    double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                    Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);
                    (double ArithmeticSampleMean, double UnbiasedPopulationVariance) = ArrayStatistics.MeanVariance(Measurement_Data_Copy);
                    Insert_Log("[All Samples (" + 0 + ", " + (Measurement_Count_Copy - 1) + ")]" + "  Arithmetic Sample Mean: " + ArithmeticSampleMean + " " + Measurement_Unit + "  Unbiased Pop Variance: " + UnbiasedPopulationVariance + " " + Measurement_Unit, 3);
                    Measurement_Data_Copy = null;
                }
                catch (Exception Ex)
                {
                    Insert_Log(Ex.Message, 1);
                    Insert_Log("Could not calculate Mean Variance for All Samples. Try again.", 1);
                }
            });
        }

        //--------------------------- Statistics (All Samples)----------------------

        //--------------------------- Statistics (N Samples)----------------------

        private (bool, int, int) StatisticsNsamples_Range()
        {
            (bool isValid_Start, double Start_Value) = Text_Num(Start_Statistics_NSamples_TextBox.Text, false, true);
            (bool isValid_End, double End_Value) = Text_Num(End_Statistics_NSamples_TextBox.Text, false, true);
            if (isValid_Start == true & isValid_End == true)
            {
                if (Start_Value < End_Value)
                {
                    if (End_Value < Measurement_Count)
                    {
                        return (true, (int)Start_Value, (int)End_Value);
                    }
                    else
                    {
                        Insert_Log("Statistics N Samples End Value must be less than or equal to Total N Samples Captured.", 1);
                        return (false, 0, 0);
                    }
                }
                else
                {
                    Insert_Log("Statistics N Samples Start Value must be less than End Value.", 1);
                    return (false, 0, 0);
                }
            }
            else
            {
                if (isValid_Start == false)
                {
                    Insert_Log("Statistics N Samples Start Value is invalid. Value must be an positive integer.", 1);
                    Start_Statistics_NSamples_TextBox.Text = String.Empty;
                }
                if (isValid_End == false)
                {
                    Insert_Log("Statistics N Samples End Value is invalid. Value must be an positive integer.", 1);
                    End_Statistics_NSamples_TextBox.Text = String.Empty;
                }
                return (false, 0, 0);
            }
        }

        private void Mean_NSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = StatisticsNsamples_Range();
            if (IsValidRange == true)
            {
                Task.Run(() =>
                {
                    try
                    {
                        double[] Measurement_Data_Copy = new double[(EndValue - StartValue) + 1];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, (EndValue - StartValue) + 1);
                        double Mean = ArrayStatistics.Mean(Measurement_Data_Copy);
                        Insert_Log("[N Samples (" + StartValue + ", " + EndValue + ")]" + "  Calculated Mean (Average): " + Mean + " " + Measurement_Unit, 3);
                        Measurement_Data_Copy = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Could not calculate Mean (Average) for N Samples. Try again.", 1);
                    }
                });
            }
            else
            {
                Insert_Log("Could not calculate Mean (Average) for N Samples. Try again.", 1);
            }
        }

        private void StdDeviation_NSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = StatisticsNsamples_Range();
            if (IsValidRange == true)
            {
                Task.Run(() =>
                {
                    try
                    {
                        double[] Measurement_Data_Copy = new double[(EndValue - StartValue) + 1];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, (EndValue - StartValue) + 1);
                        double StdDeviation = ArrayStatistics.StandardDeviation(Measurement_Data_Copy);
                        Insert_Log("[N Samples (" + StartValue + ", " + EndValue + ")]" + "  Calculated Standard Deviation: " + StdDeviation + " " + Measurement_Unit, 3);
                        Measurement_Data_Copy = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Could not calculate Standard Deviation for N Samples. Try again.", 1);
                    }
                });
            }
            else
            {
                Insert_Log("Could not calculate Standard Deviation for N Samples. Try again.", 1);
            }
        }

        private void Max_NSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = StatisticsNsamples_Range();
            if (IsValidRange == true)
            {
                Task.Run(() =>
                {
                    try
                    {
                        double[] Measurement_Data_Copy = new double[(EndValue - StartValue) + 1];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, (EndValue - StartValue) + 1);
                        double Max = ArrayStatistics.Maximum(Measurement_Data_Copy);
                        Insert_Log("[N Samples (" + StartValue + ", " + EndValue + ")]" + "  Found Maximum Sample: " + Max + " " + Measurement_Unit, 3);
                        Measurement_Data_Copy = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Could not find Maximum Sample for N Samples. Try again.", 1);
                    }
                });
            }
            else
            {
                Insert_Log("Could not find Maximum for N Samples. Try again.", 1);
            }
        }

        private void Min_NSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = StatisticsNsamples_Range();
            if (IsValidRange == true)
            {
                Task.Run(() =>
                {
                    try
                    {
                        double[] Measurement_Data_Copy = new double[(EndValue - StartValue) + 1];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, (EndValue - StartValue) + 1);
                        double Min = ArrayStatistics.Minimum(Measurement_Data_Copy);
                        Insert_Log("[N Samples (" + StartValue + ", " + EndValue + ")]" + "  Found Minimum Sample: " + Min + " " + Measurement_Unit, 3);
                        Measurement_Data_Copy = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Could not find Minimum Sample for N Samples. Try again.", 1);
                    }
                });
            }
            else
            {
                Insert_Log("Could not find Minimum for N Samples. Try again.", 1);
            }
        }

        private void AbsMax_NSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = StatisticsNsamples_Range();
            if (IsValidRange == true)
            {
                Task.Run(() =>
                {
                    try
                    {
                        double[] Measurement_Data_Copy = new double[(EndValue - StartValue) + 1];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, (EndValue - StartValue) + 1);
                        double AbsMax = ArrayStatistics.MaximumAbsolute(Measurement_Data_Copy);
                        Insert_Log("[N Samples (" + StartValue + ", " + EndValue + ")]" + "  Calculated Absolute Maximum: " + AbsMax + " " + Measurement_Unit, 3);
                        Measurement_Data_Copy = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Could not calculate Absolute Maximum for N Samples. Try again.", 1);
                    }
                });
            }
            else
            {
                Insert_Log("Could not calculate Absolute Maximum for N Samples. Try again.", 1);
            }
        }

        private void AbsMin_NSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = StatisticsNsamples_Range();
            if (IsValidRange == true)
            {
                Task.Run(() =>
                {
                    try
                    {
                        double[] Measurement_Data_Copy = new double[(EndValue - StartValue) + 1];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, (EndValue - StartValue) + 1);
                        double AbsMin = ArrayStatistics.MinimumAbsolute(Measurement_Data_Copy);
                        Insert_Log("[N Samples (" + StartValue + ", " + EndValue + ")]" + "  Calculated Absolute Minimum: " + AbsMin + " " + Measurement_Unit, 3);
                        Measurement_Data_Copy = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Could not calculate Absolute Minimum for N Samples. Try again.", 1);
                    }
                });
            }
            else
            {
                Insert_Log("Could not calculate Absolute Minimum for N Samples. Try again.", 1);
            }
        }

        private void RMS_NSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = StatisticsNsamples_Range();
            if (IsValidRange == true)
            {
                Task.Run(() =>
                {
                    try
                    {
                        double[] Measurement_Data_Copy = new double[(EndValue - StartValue) + 1];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, (EndValue - StartValue) + 1);
                        double RMS = ArrayStatistics.RootMeanSquare(Measurement_Data_Copy);
                        Insert_Log("[N Samples (" + StartValue + ", " + EndValue + ")]" + "  Calculated Root Mean Square: " + RMS + " " + Measurement_Unit, 3);
                        Measurement_Data_Copy = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Could not calculate Root Mean Square for N Samples. Try again.", 1);
                    }
                });
            }
            else
            {
                Insert_Log("Could not calculate Root Mean Square (RMS) for N Samples. Try again.", 1);
            }
        }

        private void Variance_NSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = StatisticsNsamples_Range();
            if (IsValidRange == true)
            {
                Task.Run(() =>
                {
                    try
                    {
                        double[] Measurement_Data_Copy = new double[(EndValue - StartValue) + 1];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, (EndValue - StartValue) + 1);
                        double Variance = ArrayStatistics.Variance(Measurement_Data_Copy);
                        Insert_Log("[N Samples (" + StartValue + ", " + EndValue + ")]" + "  Calculated Variance: " + Variance + " " + Measurement_Unit, 3);
                        Measurement_Data_Copy = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Could not calculate Variance for N Samples. Try again.", 1);
                    }
                });
            }
            else
            {
                Insert_Log("Could not calculate Variance for N Samples. Try again.", 1);
            }
        }

        private void GeometricMean_NSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = StatisticsNsamples_Range();
            if (IsValidRange == true)
            {
                Task.Run(() =>
                {
                    try
                    {
                        double[] Measurement_Data_Copy = new double[(EndValue - StartValue) + 1];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, (EndValue - StartValue) + 1);
                        double GeometricMean = ArrayStatistics.GeometricMean(Measurement_Data_Copy);
                        Insert_Log("[N Samples (" + StartValue + ", " + EndValue + ")]" + "  Calculated Geometric Mean: " + GeometricMean + " " + Measurement_Unit, 3);
                        Measurement_Data_Copy = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Could not calculate Geometric Mean for N Samples. Try again.", 1);
                    }
                });
            }
            else
            {
                Insert_Log("Could not calculate Geometric Mean for N Samples. Try again.", 1);
            }
        }

        private void HarmonicMean_NSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = StatisticsNsamples_Range();
            if (IsValidRange == true)
            {
                Task.Run(() =>
                {
                    try
                    {
                        double[] Measurement_Data_Copy = new double[(EndValue - StartValue) + 1];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, (EndValue - StartValue) + 1);
                        double HarmonicMean = ArrayStatistics.HarmonicMean(Measurement_Data_Copy);
                        Insert_Log("[N Samples (" + StartValue + ", " + EndValue + ")]" + "  Calculated Harmonic Mean: " + HarmonicMean + " " + Measurement_Unit, 3);
                        Measurement_Data_Copy = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Could not calculate Harmonic Mean for N Samples. Try again.", 1);
                    }
                });
            }
            else
            {
                Insert_Log("Could not calculate Harmonic Mean for N Samples. Try again.", 1);
            }
        }

        private void PopulationVariance_NSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = StatisticsNsamples_Range();
            if (IsValidRange == true)
            {
                Task.Run(() =>
                {
                    try
                    {
                        double[] Measurement_Data_Copy = new double[(EndValue - StartValue) + 1];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, (EndValue - StartValue) + 1);
                        double PopulationVariance = ArrayStatistics.PopulationVariance(Measurement_Data_Copy);
                        Insert_Log("[N Samples (" + StartValue + ", " + EndValue + ")]" + "  Calculated Population Variance: " + PopulationVariance + " " + Measurement_Unit, 3);
                        Measurement_Data_Copy = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Could not calculate Population Variance for N Samples. Try again.", 1);
                    }
                });
            }
            else
            {
                Insert_Log("Could not calculate Population Variance for N Samples. Try again.", 1);
            }
        }

        private void PopulationStdDeviation_NSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = StatisticsNsamples_Range();
            if (IsValidRange == true)
            {
                Task.Run(() =>
                {
                    try
                    {
                        double[] Measurement_Data_Copy = new double[(EndValue - StartValue) + 1];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, (EndValue - StartValue) + 1);
                        double PopulationStandardDeviation = ArrayStatistics.PopulationStandardDeviation(Measurement_Data_Copy);
                        Insert_Log("[N Samples (" + StartValue + ", " + EndValue + ")]" + "  Calculated Population Standard Deviation: " + PopulationStandardDeviation + " " + Measurement_Unit, 3);
                        Measurement_Data_Copy = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Could not calculate Population Standard Deviation for All Samples. Try again.", 1);
                    }
                });
            }
            else
            {
                Insert_Log("Could not calculate Population Standard Deviation for N Samples. Try again.", 1);
            }
        }

        private void MeanStdDeviation_NSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = StatisticsNsamples_Range();
            if (IsValidRange == true)
            {
                Task.Run(() =>
                {
                    try
                    {
                        double[] Measurement_Data_Copy = new double[(EndValue - StartValue) + 1];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, (EndValue - StartValue) + 1);
                        (double ArithmeticSampleMean, double UnbiasedPopulationStandardDeviation) = ArrayStatistics.MeanStandardDeviation(Measurement_Data_Copy);
                        Insert_Log("[N Samples (" + StartValue + ", " + EndValue + ")]" + "  Arithmetic Sample Mean: " + ArithmeticSampleMean + " " + Measurement_Unit + "  Unbiased Pop Std Deviation: " + UnbiasedPopulationStandardDeviation + " " + Measurement_Unit, 3);
                        Measurement_Data_Copy = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Could not calculate Mean Standard Deviation for N Samples. Try again.", 1);
                    }
                });
            }
            else
            {
                Insert_Log("Could not calculate Mean Standard Deviation for N Samples. Try again.", 1);
            }
        }

        private void MeanVariance_NSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = StatisticsNsamples_Range();
            if (IsValidRange == true)
            {
                Task.Run(() =>
                {
                    try
                    {
                        double[] Measurement_Data_Copy = new double[(EndValue - StartValue) + 1];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, (EndValue - StartValue) + 1);
                        (double ArithmeticSampleMean, double UnbiasedPopulationVariance) = ArrayStatistics.MeanVariance(Measurement_Data_Copy);
                        Insert_Log("[N Samples (" + StartValue + ", " + EndValue + ")]" + "  Arithmetic Sample Mean: " + ArithmeticSampleMean + " " + Measurement_Unit + "  Unbiased Pop Variance: " + UnbiasedPopulationVariance + " " + Measurement_Unit, 3);
                        Measurement_Data_Copy = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Could not calculate Mean Variance for N Samples. Try again.", 1);
                    }
                });
            }
            else
            {
                Insert_Log("Could not calculate Mean Variance for N Samples. Try again.", 1);
            }
        }

        //--------------------------- Statistics (N Samples)----------------------

        //--------------------------- Math (All Samples)----------------------

        private void Addition_Button_Math_AllSamples_Click(object sender, RoutedEventArgs e)
        {
            (bool isValid, double Value) = Text_Num(Addition_TextBox_Math_AllSamples.Text, true, false);
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValid == true & isValidGraphColor == true)
            {
                if (Addition_AllSamples_Samples_Value.IsSelected == true)
                {
                    string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                    string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                    Task.Run(() =>
                    {
                        try
                        {
                            int Measurement_Count_Copy = Measurement_Count;
                            double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                            Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                            DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                            Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                            for (int i = 0; i < Measurement_Count_Copy; i++)
                            {
                                Measurement_Data_Copy[i] = Measurement_Data_Copy[i] + Value;
                            }
                            Create_Waveform_Window("Addition Math Waveform [All Samples]: Samples + " + Value, Value, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                            Measurement_Data_Copy = null;
                            Measurement_Data_DateTime = null;
                        }
                        catch (Exception Ex)
                        {
                            Insert_Log(Ex.Message, 1);
                            Insert_Log("Cannot create Addition (All Samples) (Samples + Value) Math Waveform. Try again.", 1);
                        }
                    });
                }
                else
                {
                    string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                    string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                    Task.Run(() =>
                    {
                        try
                        {
                            int Measurement_Count_Copy = Measurement_Count;
                            double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                            Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                            DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                            Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                            for (int i = 0; i < Measurement_Count_Copy; i++)
                            {
                                Measurement_Data_Copy[i] = Value + Measurement_Data_Copy[i];
                            }
                            Create_Waveform_Window("Addition Math Waveform [All Samples]: " + Value + " + Samples", Value, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                            Measurement_Data_Copy = null;
                            Measurement_Data_DateTime = null;
                        }
                        catch (Exception Ex)
                        {
                            Insert_Log(Ex.Message, 1);
                            Insert_Log("Cannot create Addition (All Samples) (Value + Samples) Math Waveform. Try again.", 1);
                        }
                    });
                }
            }
            else
            {
                if (isValid == false)
                {
                    Addition_TextBox_Math_AllSamples.Text = String.Empty;
                    Insert_Log("Cannot create Addition (All Samples) Math Waveform. Value's input field must only have numbers, no text.", 1);
                }
                else
                {
                    Insert_Log("Cannot create Addition (All Samples) Math Waveform.", 1);
                }
            }
        }

        private void Subtraction_Button_Math_AllSamples_Click(object sender, RoutedEventArgs e)
        {
            (bool isValid, double Value) = Text_Num(Subtraction_TextBox_Math_AllSamples.Text, true, false);
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValid == true & isValidGraphColor == true)
            {
                if (Subtraction_AllSamples_Samples_Value.IsSelected == true)
                {
                    string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                    string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                    Task.Run(() =>
                    {
                        try
                        {
                            int Measurement_Count_Copy = Measurement_Count;
                            double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                            Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                            DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                            Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                            for (int i = 0; i < Measurement_Count_Copy; i++)
                            {
                                Measurement_Data_Copy[i] = Measurement_Data_Copy[i] - Value;
                            }
                            Create_Waveform_Window("Subtraction Math Waveform [All Samples]: Samples - " + Value, Value, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                            Measurement_Data_Copy = null;
                            Measurement_Data_DateTime = null;
                        }
                        catch (Exception Ex)
                        {
                            Insert_Log(Ex.Message, 1);
                            Insert_Log("Cannot create Subtraction (All Samples) (Samples - Value) Math Waveform. Try again.", 1);
                        }
                    });
                }
                else
                {
                    string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                    string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                    Task.Run(() =>
                    {
                        try
                        {
                            int Measurement_Count_Copy = Measurement_Count;
                            double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                            Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                            DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                            Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                            for (int i = 0; i < Measurement_Count_Copy; i++)
                            {
                                Measurement_Data_Copy[i] = Value - Measurement_Data_Copy[i];
                            }
                            Create_Waveform_Window("Subtraction Math Waveform [All Samples]: " + Value + " - Samples", Value, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                            Measurement_Data_Copy = null;
                            Measurement_Data_DateTime = null;
                        }
                        catch (Exception Ex)
                        {
                            Insert_Log(Ex.Message, 1);
                            Insert_Log("Cannot create Subtraction (All Samples) (Value - Samples) Math Waveform. Try again.", 1);
                        }
                    });
                }
            }
            else
            {
                if (isValid == false)
                {
                    Subtraction_TextBox_Math_AllSamples.Text = String.Empty;
                    Insert_Log("Cannot create Subtraction (All Samples) Math Waveform. Value's input field must only have numbers, no text.", 1);
                }
                else
                {
                    Insert_Log("Cannot create Subtraction (All Samples) Math Waveform.", 1);
                }
            }
        }

        private void Multiplication_Button_Math_AllSamples_Click(object sender, RoutedEventArgs e)
        {
            (bool isValid, double Value) = Text_Num(Mutiplication_TextBox_Math_AllSamples.Text, true, false);
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValid == true & isValidGraphColor == true)
            {
                if (Multiplication_AllSamples_Samples_Value.IsSelected == true)
                {
                    string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                    string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                    Task.Run(() =>
                    {
                        try
                        {
                            int Measurement_Count_Copy = Measurement_Count;
                            double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                            Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                            DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                            Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                            for (int i = 0; i < Measurement_Count_Copy; i++)
                            {
                                Measurement_Data_Copy[i] = Measurement_Data_Copy[i] * Value;
                            }
                            Create_Waveform_Window("Multiplication Math Waveform [All Samples]: Samples * " + Value, Value, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                            Measurement_Data_Copy = null;
                            Measurement_Data_DateTime = null;
                        }
                        catch (Exception Ex)
                        {
                            Insert_Log(Ex.Message, 1);
                            Insert_Log("Cannot create Multiplication (All Samples) (Samples * Value) Math Waveform. Try again.", 1);
                        }
                    });
                }
                else
                {
                    string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                    string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                    Task.Run(() =>
                    {
                        try
                        {
                            int Measurement_Count_Copy = Measurement_Count;
                            double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                            Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                            DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                            Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                            for (int i = 0; i < Measurement_Count_Copy; i++)
                            {
                                Measurement_Data_Copy[i] = Value * Measurement_Data_Copy[i];
                            }
                            Create_Waveform_Window("Multiplication Math Waveform [All Samples]: " + Value + " * Samples", Value, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                            Measurement_Data_Copy = null;
                            Measurement_Data_DateTime = null;
                        }
                        catch (Exception Ex)
                        {
                            Insert_Log(Ex.Message, 1);
                            Insert_Log("Cannot create Multiplication (All Samples) (Value * Samples) Math Waveform. Try again.", 1);
                        }
                    });
                }
            }
            else
            {
                if (isValid == false)
                {
                    Mutiplication_TextBox_Math_AllSamples.Text = String.Empty;
                    Insert_Log("Cannot create Multiplication (All Samples) Math Waveform. Value's input field must only have numbers.", 1);
                }
                else
                {
                    Insert_Log("Cannot create Multiplication (All Samples) Math Waveform.", 1);
                }
            }
        }

        private void Division_Button_Math_AllSamples_Click(object sender, RoutedEventArgs e)
        {
            (bool isValid, double Value) = Text_Num(Division_TextBox_Math_AllSamples.Text, true, false);
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValid == true & isValidGraphColor == true)
            {
                if (Value != 0)
                {
                    if (Division_AllSamples_Samples_Value.IsSelected == true)
                    {
                        string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                        string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                        Task.Run(() =>
                        {
                            try
                            {
                                int Measurement_Count_Copy = Measurement_Count;
                                double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                                Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                                DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                                Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                                for (int i = 0; i < Measurement_Count_Copy; i++)
                                {
                                    Measurement_Data_Copy[i] = Measurement_Data_Copy[i] / Value;
                                }
                                Create_Waveform_Window("Division Math Waveform [All Samples]: Samples / " + Value, Value, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                                Measurement_Data_Copy = null;
                                Measurement_Data_DateTime = null;
                            }
                            catch (Exception Ex)
                            {
                                Insert_Log(Ex.Message, 1);
                                Insert_Log("Cannot create Division (All Samples) (Samples / Value) Math Waveform. Try again.", 1);
                            }
                        });
                    }
                    else
                    {
                        string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                        string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                        Task.Run(() =>
                        {
                            try
                            {
                                int Measurement_Count_Copy = Measurement_Count;
                                double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                                Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                                DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                                Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                                for (int i = 0; i < Measurement_Count_Copy; i++)
                                {
                                    Measurement_Data_Copy[i] = Value / Measurement_Data_Copy[i];
                                    if (double.IsInfinity(Measurement_Data_Copy[i]) == true) //Check is answer is infinite, if yes then set it to 0
                                    {
                                        Measurement_Data_Copy[i] = 0;
                                    }
                                }
                                Create_Waveform_Window("Division Math Waveform [All Samples]: " + Value + " / Samples", Value, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                                Measurement_Data_Copy = null;
                                Measurement_Data_DateTime = null;
                            }
                            catch (Exception Ex)
                            {
                                Insert_Log(Ex.Message, 1);
                                Insert_Log("Cannot create Division (All Samples) (Value / Samples) Math Waveform. Try again.", 1);
                            }
                        });
                    }
                }
                else
                {
                    Insert_Log("Cannot create Division (All Samples) (Value / Samples) Math Waveform." + " Cannot divide by " + Value, 1);
                }
            }
            else
            {
                if (isValid == false)
                {
                    Division_TextBox_Math_AllSamples.Text = String.Empty;
                    Insert_Log("Cannot create Division (All Samples) Math Waveform. Value's input field must only have numbers, no text.", 1);
                }
                else
                {
                    Insert_Log("Cannot create Division (All Samples) Math Waveform.", 1);
                }
            }
        }

        private void Percentage_Error_Button_AllSamples_Click(object sender, RoutedEventArgs e)
        {
            (bool isValid, double Value) = Text_Num(Percentage_Error_TextBox_AllSamples.Text, true, false);
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValid == true & isValidGraphColor == true)
            {
                if (Value != 0)
                {
                    string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                    string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                    Task.Run(() =>
                    {
                        try
                        {
                            int Measurement_Count_Copy = Measurement_Count;
                            double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                            Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                            DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                            Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                            for (int i = 0; i < Measurement_Count_Copy; i++)
                            {
                                Measurement_Data_Copy[i] = Math.Abs((Measurement_Data_Copy[i] - Value) / Value) * 100;
                            }
                            Create_Waveform_Window("% Error Math Waveform [All Samples]: |(Samples - " + Value + ") / " + Value + "| x 100", Value, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                            Measurement_Data_Copy = null;
                            Measurement_Data_DateTime = null;
                        }
                        catch (Exception Ex)
                        {
                            Insert_Log(Ex.Message, 1);
                            Insert_Log("Cannot create % Error Math Waveform (All Samples). Try again.", 1);
                        }
                    });
                }
                else
                {
                    Insert_Log("Cannot create % Error Math Waveform (All Samples): Value must not be " + Value, 1);
                }
            }
            else
            {
                if (isValid == false)
                {
                    Insert_Log("Cannot create % Error Math Waveform (All Samples). Value must be a real number.", 1);
                }
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create % Error Math Waveform (All Samples). Graph Color values are not valid.", 1);
                }
            }
        }

        private void DB_Button_Math_AllSamples_Click(object sender, RoutedEventArgs e)
        {
            (bool isValid_DB_1_Value, double DB_1_Value) = Text_Num(DB_1_Math_AllSamples.Text, true, false);
            (bool isValid_DB_2_Value, double DB_2_Value) = Text_Num(DB_2_Math_AllSamples.Text, false, false);
            (bool isValid_DB_3_Value, double DB_3_Value) = Text_Num(DB_3_Math_AllSamples.Text, false, false);
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValid_DB_1_Value == true & isValid_DB_2_Value == true & isValid_DB_3_Value == true & isValidGraphColor == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = Measurement_Count;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            Measurement_Data_Copy[i] = (DB_1_Value) * (Math.Log(((Math.Abs(Measurement_Data_Copy[i])) / DB_3_Value), DB_2_Value));
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("DB (All Samples Math Waveform): " + DB_1_Value + " x log" + DB_2_Value + " (Samples / " + DB_3_Value + ")", DB_3_Value, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create DB (All Samples) Math Waveform. Try again.", 1);
                    }
                });
            }
            else
            {
                if ((isValid_DB_1_Value == false) || (isValid_DB_2_Value == false) || (isValid_DB_3_Value == false))
                {
                    Insert_Log("Cannot create DB (All Samples) Math Waveform. The base and the argument of the logarithm must be positive. Check your inputted values.", 1);
                }
                if (isValidGraphColor == false) 
                {
                    Insert_Log("Cannot create DB (All Samples) Math Waveform. Check your Graph Color values.", 1);
                }
            }
        }

        private void DBM_Button_Math_AllSamples_Click(object sender, RoutedEventArgs e)
        {
            (bool isValid_DBM_1_Value, double DBM_1_Value) = Text_Num(DBM_1_Math_AllSamples.Text, true, false);
            (bool isValid_DBM_2_Value, double DBM_2_Value) = Text_Num(DBM_2_Math_AllSamples.Text, false, false);
            (bool isValid_DBM_3_Value, double DBM_3_Value) = Text_Num(DBM_3_Math_AllSamples.Text, false, false);
            (bool isValid_DBM_4_Value, double DBM_4_Value) = Text_Num(DBM_4_Math_AllSamples.Text, false, false);
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValid_DBM_1_Value == true & isValid_DBM_2_Value == true & isValid_DBM_3_Value == true & isValid_DBM_4_Value == true & isValidGraphColor == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = Measurement_Count;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            Measurement_Data_Copy[i] = (DBM_1_Value) * (Math.Log(((((Math.Pow(Measurement_Data_Copy[i], 2)) / DBM_3_Value)) / DBM_4_Value), DBM_2_Value));
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("DBM (All Samples Math Waveform): " + DBM_1_Value + " x log" + DBM_2_Value + " ((Samples^2 / " + DBM_3_Value + ") / " + DBM_4_Value + ")", DBM_3_Value, 0, Measurement_Count_Copy -1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create DBM (All Samples) Math Waveform. Try again.", 1);
                    }
                });
            }
            else
            {
                if ((isValid_DBM_1_Value == false) || (isValid_DBM_2_Value == false) || (isValid_DBM_3_Value == false) || (isValid_DBM_4_Value == false))
                {
                    Insert_Log("Cannot create DBM (All Samples) Math Waveform. The base and the argument of the logarithm must be positive. Check your inputted values.", 1);
                }
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create DBM (All Samples) Math Waveform. Check your Graph Color values.", 1);
                }
            }
        }

        private void Value_Power_AllSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            (bool isValid, double Value) = Text_Num(Value_Power_AllSample_Text.Text, true, false);
            if (isValidGraphColor == true & isValid == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = Measurement_Count;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            Measurement_Data_Copy[i] = Math.Pow(Value, Measurement_Data_Copy[i]);
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("(Value)^(Samples) Math Waveform [All Samples]", 0, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create (Value)^(Samples) (All Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create (Value)^(Samples) (All Samples) Math Waveform. Check your Graph Color values.", 1);
                }
                if (isValid == false)
                {
                    Insert_Log("Cannot create (Value)^(Samples) (All Samples) Math Waveform. Check your Value.", 1);
                }
            }
        }

        private void AllSample_Power_Value_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            (bool isValid, double Value) = Text_Num(AllSample_Power_Value_Text.Text, true, false);
            if (isValidGraphColor == true & isValid == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = Measurement_Count;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            Measurement_Data_Copy[i] = Math.Pow(Measurement_Data_Copy[i], Value);
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("(Samples)^(Value) Math Waveform [All Samples]", 0, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create (Samples)^(Value) (All Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create (Samples)^(Value) (All Samples) Math Waveform. Check your Graph Color values.", 1);
                }
                if (isValid == false)
                {
                    Insert_Log("Cannot create (Samples)^(Value) (All Samples) Math Waveform. Check your Value.", 1);
                }
            }
        }

        private void Log_AllSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValidGraphColor == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = Measurement_Count;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            Measurement_Data_Copy[i] = Math.Log10(Measurement_Data_Copy[i]);
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Logarithm Math Waveform [All Samples]", 0, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Logarithm (All Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Logarithm (All Samples) Math Waveform. Check your Graph Color values.", 1);
                }
            }
        }

        private void Ln_AllSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValidGraphColor == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = Measurement_Count;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            Measurement_Data_Copy[i] = Math.Log(Measurement_Data_Copy[i]);
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Natural Logarithm Math Waveform [All Samples]", 0, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Natural Logarithm (All Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Natural Logarithm (All Samples) Math Waveform. Check your Graph Color values.", 1);
                }
            }
        }

        private void Square_AllSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValidGraphColor == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = Measurement_Count;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            Measurement_Data_Copy[i] = Math.Sqrt(Measurement_Data_Copy[i]);
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Square Root Math Waveform [All Samples]", 0, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Square Root (All Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Square Root (All Samples) Math Waveform. Check your Graph Color values.", 1);
                }
            }
        }

        private void Abs_AllSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValidGraphColor == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = Measurement_Count;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            Measurement_Data_Copy[i] = Math.Abs(Measurement_Data_Copy[i]);
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Absolute Value Math Waveform [All Samples]", 0, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Absolute Value (All Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Absolute Value (All Samples) Math Waveform. Check your Graph Color values.", 1);
                }
            }
        }

        private void Sine_AllSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValidGraphColor == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                bool inDegrees = Sine_AllSample_Degrees.IsSelected;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = Measurement_Count;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            if (inDegrees == true)
                            {
                                Measurement_Data_Copy[i] = (Math.Sin(Measurement_Data_Copy[i]) * (180 / Math.PI));
                            }
                            else
                            {
                                Measurement_Data_Copy[i] = Math.Sin(Measurement_Data_Copy[i]);
                            }
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Sine Math Waveform [All Samples]", 0, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Sine (All Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Sine (All Samples) Math Waveform. Check your Graph Color values.", 1);
                }
            }
        }

        private void Cosine_AllSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValidGraphColor == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                bool inDegrees = Cosine_AllSample_Degrees.IsSelected;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = Measurement_Count;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            if (inDegrees == true)
                            {
                                Measurement_Data_Copy[i] = (Math.Cos(Measurement_Data_Copy[i]) * (180 / Math.PI));
                            }
                            else
                            {
                                Measurement_Data_Copy[i] = Math.Cos(Measurement_Data_Copy[i]);
                            }
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Cosine Math Waveform [All Samples]", 0, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Cosine (All Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Cosine (All Samples) Math Waveform. Check your Graph Color values.", 1);
                }
            }
        }

        private void Tangent_AllSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValidGraphColor == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                bool inDegrees = Tangent_AllSample_Degrees.IsSelected;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = Measurement_Count;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            if (inDegrees == true)
                            {
                                Measurement_Data_Copy[i] = (Math.Tan(Measurement_Data_Copy[i]) * (180 / Math.PI));
                            }
                            else
                            {
                                Measurement_Data_Copy[i] = Math.Tan(Measurement_Data_Copy[i]);
                            }
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Tangent Math Waveform [All Samples]", 0, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Tangent (All Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Tangent (All Samples) Math Waveform. Check your Graph Color values.", 1);
                }
            }
        }

        private void Inverse_Sine_AllSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValidGraphColor == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                bool inDegrees = Inverse_Sine_AllSample_Degrees.IsSelected;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = Measurement_Count;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            if (inDegrees == true)
                            {
                                Measurement_Data_Copy[i] = (Math.Asin(Measurement_Data_Copy[i]) * (180 / Math.PI));
                            }
                            else
                            {
                                Measurement_Data_Copy[i] = Math.Asin(Measurement_Data_Copy[i]);
                            }
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Inverse Sine Math Waveform [All Samples]", 0, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Inverse Sine (All Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Inverse Sine (All Samples) Math Waveform. Check your Graph Color values.", 1);
                }
            }
        }

        private void Inverse_Cosine_AllSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValidGraphColor == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                bool inDegrees = Inverse_Cosine_AllSample_Degrees.IsSelected;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = Measurement_Count;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            if (inDegrees == true)
                            {
                                Measurement_Data_Copy[i] = (Math.Acos(Measurement_Data_Copy[i]) * (180 / Math.PI));
                            }
                            else
                            {
                                Measurement_Data_Copy[i] = Math.Acos(Measurement_Data_Copy[i]);
                            }
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Inverse Cosine Math Waveform [All Samples]", 0, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Inverse Cosine (All Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Inverse Cosine (All Samples) Math Waveform. Check your Graph Color values.", 1);
                }
            }
        }

        private void Inverse_Tangent_AllSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValidGraphColor == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                bool inDegrees = Inverse_Tangent_AllSample_Degrees.IsSelected;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = Measurement_Count;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            if (inDegrees == true)
                            {
                                Measurement_Data_Copy[i] = (Math.Atan(Measurement_Data_Copy[i]) * (180 / Math.PI));
                            }
                            else
                            {
                                Measurement_Data_Copy[i] = Math.Atan(Measurement_Data_Copy[i]);
                            }
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Inverse Tangent Math Waveform [All Samples]", 0, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Inverse Tangent (All Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Inverse Tangent (All Samples) Math Waveform. Check your Graph Color values.", 1);
                }
            }
        }

        private void Hyperbolic_Sine_AllSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValidGraphColor == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                bool inDegrees = Hyperbolic_Sine_AllSample_Degrees.IsSelected;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = Measurement_Count;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            if (inDegrees == true)
                            {
                                Measurement_Data_Copy[i] = (Math.Sinh(Measurement_Data_Copy[i]) * (180 / Math.PI));
                            }
                            else
                            {
                                Measurement_Data_Copy[i] = Math.Sinh(Measurement_Data_Copy[i]);
                            }
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Hyperbolic Sine Math Waveform [All Samples]", 0, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Hyperbolic Sine (All Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Hyperbolic Sine (All Samples) Math Waveform. Check your Graph Color values.", 1);
                }
            }
        }

        private void Hyperbolic_Cosine_AllSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValidGraphColor == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                bool inDegrees = Hyperbolic_Cosine_AllSample_Degrees.IsSelected;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = Measurement_Count;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            if (inDegrees == true)
                            {
                                Measurement_Data_Copy[i] = (Math.Cosh(Measurement_Data_Copy[i]) * (180 / Math.PI));
                            }
                            else
                            {
                                Measurement_Data_Copy[i] = Math.Cosh(Measurement_Data_Copy[i]);
                            }
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Hyperbolic Cosine Math Waveform [All Samples]", 0, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Hyperbolic Cosine (All Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Hyperbolic Cosine (All Samples) Math Waveform. Check your Graph Color values.", 1);
                }
            }
        }

        private void Hyperbolic_Tangent_AllSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValidGraphColor == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_AllSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_AllSamples.Text;
                bool inDegrees = Hyperbolic_Tangent_AllSample_Degrees.IsSelected;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = Measurement_Count;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            if (inDegrees == true)
                            {
                                Measurement_Data_Copy[i] = (Math.Tanh(Measurement_Data_Copy[i]) * (180 / Math.PI));
                            }
                            else
                            {
                                Measurement_Data_Copy[i] = Math.Tanh(Measurement_Data_Copy[i]);
                            }
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Hyperbolic Tangent Math Waveform [All Samples]", 0, 0, Measurement_Count_Copy - 1, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Hyperbolic Tangent (All Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Hyperbolic Tangent (All Samples) Math Waveform. Check your Graph Color values.", 1);
                }
            }
        }

        private void GraphColor_SetButton_Math_AllSamples_Click(object sender, RoutedEventArgs e)
        {
            (bool isValid, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_AllSamples_Check();
            if (isValid == true)
            {
                GraphColor_Preview_Math_AllSamples.Fill = new SolidColorBrush(Color.FromArgb(255, (byte)(Value_Red), (byte)(Value_Green), (byte)(Value_Blue)));
            }
        }

        private (bool, int, int, int) GraphColor_Math_AllSamples_Check()
        {
            (bool isValid_Red, double Value_Red) = Text_Num(Red_GraphColor_TextBox_Math_AllSamples.Text, false, true);
            (bool isValid_Green, double Value_Green) = Text_Num(Green_GraphColor_TextBox_Math_AllSamples.Text, false, true);
            (bool isValid_Blue, double Value_Blue) = Text_Num(Blue_GraphColor_TextBox_Math_AllSamples.Text, false, true);
            if ((isValid_Red == true) & (isValid_Green == true) & (isValid_Blue == true))
            {
                if ((Value_Red <= 255) & (Value_Green <= 255) & (Value_Blue <= 255))
                {
                    return (true, (int)Value_Red, (int)Value_Green, (int)Value_Blue);
                }
                else
                {
                    if (Value_Red > 255)
                    {
                        Red_GraphColor_TextBox_Math_AllSamples.Text = string.Empty;
                    }
                    if (Value_Green > 255)
                    {
                        Green_GraphColor_TextBox_Math_AllSamples.Text = string.Empty;
                    }
                    if (Value_Blue > 255)
                    {
                        Blue_GraphColor_TextBox_Math_AllSamples.Text = string.Empty;
                    }
                    Insert_Log("Math (All Samples) Graph Color values must be positive integers and must be between 0 and 255.", 1);
                    return (false, 0, 0, 0);
                }
            }
            else
            {
                if (isValid_Red == false)
                {
                    Red_GraphColor_TextBox_Math_AllSamples.Text = string.Empty;
                }
                if (isValid_Green == false)
                {
                    Green_GraphColor_TextBox_Math_AllSamples.Text = string.Empty;
                }
                if (isValid_Blue == false)
                {
                    Blue_GraphColor_TextBox_Math_AllSamples.Text = string.Empty;
                }
                Insert_Log("Math (All Samples) Graph Color values must be positive integers and must be between 0 and 255.", 1);
                return (false, 0, 0, 0);
            }

        }

        private void GraphColor_RandomizeButton_Math_AllSamples_Click(object sender, RoutedEventArgs e)
        {
            Random RGB_Value = new Random();
            int Value_Red = RGB_Value.Next(0, 255);
            int Value_Green = RGB_Value.Next(0, 255);
            int Value_Blue = RGB_Value.Next(0, 255);
            Red_GraphColor_TextBox_Math_AllSamples.Text = Value_Red.ToString();
            Green_GraphColor_TextBox_Math_AllSamples.Text = Value_Green.ToString();
            Blue_GraphColor_TextBox_Math_AllSamples.Text = Value_Blue.ToString();
            GraphColor_Preview_Math_AllSamples.Fill = new SolidColorBrush(Color.FromArgb(255, (byte)(Value_Red), (byte)(Value_Green), (byte)(Value_Blue)));
        }

        //--------------------------- Math (All Samples)----------------------

        //--------------------------- Math (N Samples)----------------------
        private (bool, int, int) MathNsamples_Range()
        {
            (bool isValid_Start, double Start_Value) = Text_Num(Start_Math_NSamples_TextBox.Text, false, true);
            (bool isValid_End, double End_Value) = Text_Num(End_Math_NSamples_TextBox.Text, false, true);
            if (isValid_Start == true & isValid_End == true)
            {
                if (Start_Value < End_Value)
                {
                    if (End_Value < Measurement_Count)
                    {
                        return (true, (int)Start_Value, (int)End_Value);
                    }
                    else
                    {
                        Insert_Log("Math N Samples End Value must be less than or equal to Total N Samples Captured.", 1);
                        return (false, 0, 0);
                    }
                }
                else
                {
                    Insert_Log("Math N Samples Start Value must be less than End Value.", 1);
                    return (false, 0, 0);
                }
            }
            else
            {
                if (isValid_Start == false)
                {
                    Insert_Log("Math N Samples Start Value is invalid. Value must be an positive integer.", 1);
                    Start_Math_NSamples_TextBox.Text = String.Empty;
                }
                if (isValid_End == false)
                {
                    Insert_Log("Math N Samples End Value is invalid. Value must be an positive integer.", 1);
                    End_Math_NSamples_TextBox.Text = String.Empty;
                }
                return (false, 0, 0);
            }
        }

        private void Addition_Button_Math_NSamples_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValid, double Value) = Text_Num(Addition_TextBox_Math_NSamples.Text, true, false);
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (IsValidRange == true & isValid == true & isValidGraphColor == true)
            {
                if (Addition_NSamples_Samples_Value.IsSelected == true)
                {
                    string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                    string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                    Task.Run(() =>
                    {
                        try
                        {
                            int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                            double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                            Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                            DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                            Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                            for (int i = 0; i < Measurement_Count_Copy; i++)
                            {
                                Measurement_Data_Copy[i] = Measurement_Data_Copy[i] + Value;
                            }
                            Create_Waveform_Window("Addition Math Waveform [" + StartValue + ", " + EndValue + "]: Samples + " + Value, Value, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                            Measurement_Data_Copy = null;
                            Measurement_Data_DateTime = null;
                        }
                        catch (Exception Ex)
                        {
                            Insert_Log(Ex.Message, 1);
                            Insert_Log("Cannot create Addition (N Samples) (Samples + Value) Math Waveform. Try again.", 1);
                        }
                    });
                }
                else
                {
                    string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                    string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                    Task.Run(() =>
                    {
                        try
                        {
                            int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                            double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                            Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                            DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                            Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                            for (int i = 0; i < Measurement_Count_Copy; i++)
                            {
                                Measurement_Data_Copy[i] = Value + Measurement_Data_Copy[i];
                            }
                            Create_Waveform_Window("Addition Math Waveform [" + StartValue + ", " + EndValue + "]: " + Value + " + Samples", Value, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                            Measurement_Data_Copy = null;
                            Measurement_Data_DateTime = null;
                        }
                        catch (Exception Ex)
                        {
                            Insert_Log(Ex.Message, 1);
                            Insert_Log("Cannot create Addition (N Samples) (Value + Samples) Math Waveform. Try again.", 1);
                        }
                    });
                }
            }
            else
            {
                if (isValid == false)
                {
                    Addition_TextBox_Math_NSamples.Text = string.Empty;
                    Insert_Log("Cannot create Addition (N Samples) Math Waveform. Value's input field must only have numbers, no text.", 1);
                }
                else
                {
                    Insert_Log("Cannot create Addition (N Samples) Math Waveform.", 1);
                }
            }
        }

        private void Subtraction_Button_Math_NSamples_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValid, double Value) = Text_Num(Subtraction_TextBox_Math_NSamples.Text, true, false);
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (IsValidRange == true & isValid == true & isValidGraphColor == true)
            {
                if (Subtraction_NSamples_Samples_Value.IsSelected == true)
                {
                    string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                    string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                    Task.Run(() =>
                    {
                        try
                        {
                            int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                            double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                            Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                            DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                            Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                            for (int i = 0; i < Measurement_Count_Copy; i++)
                            {
                                Measurement_Data_Copy[i] = Measurement_Data_Copy[i] - Value;
                            }
                            Create_Waveform_Window("Subtraction Math Waveform [" + StartValue + ", " + EndValue + "]: Samples - " + Value, Value, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                            Measurement_Data_Copy = null;
                            Measurement_Data_DateTime = null;
                        }
                        catch (Exception Ex)
                        {
                            Insert_Log(Ex.Message, 1);
                            Insert_Log("Cannot create Subtraction (N Samples) (Samples - Value) Math Waveform. Try again.", 1);
                        }
                    });
                }
                else
                {
                    string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                    string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                    Task.Run(() =>
                    {
                        try
                        {
                            int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                            double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                            Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                            DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                            Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                            for (int i = 0; i < Measurement_Count_Copy; i++)
                            {
                                Measurement_Data_Copy[i] = Value - Measurement_Data_Copy[i];
                            }
                            Create_Waveform_Window("Subtraction Math Waveform [" + StartValue + ", " + EndValue + "]: " + Value + " - Samples", Value, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                            Measurement_Data_Copy = null;
                            Measurement_Data_DateTime = null;
                        }
                        catch (Exception Ex)
                        {
                            Insert_Log(Ex.Message, 1);
                            Insert_Log("Cannot create Subtraction (N Samples) (Value - Samples) Math Waveform. Try again.", 1);
                        }
                    });
                }
            }
            else
            {
                if (isValid == false)
                {
                    Subtraction_TextBox_Math_NSamples.Text = string.Empty;
                    Insert_Log("Cannot create Subtraction (N Samples) Math Waveform. Value's input field must only have numbers, no text.", 1);
                }
                else
                {
                    Insert_Log("Cannot create Subtraction (N Samples) Math Waveform.", 1);
                }
            }
        }

        private void Multiplication_Button_Math_NSamples_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValid, double Value) = Text_Num(Multiplication_TextBox_Math_NSamples.Text, true, false);
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (IsValidRange == true & isValid == true & isValidGraphColor == true)
            {
                if (Multiplication_NSamples_Samples_Value.IsSelected == true)
                {
                    string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                    string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                    Task.Run(() =>
                    {
                        try
                        {
                            int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                            double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                            Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                            DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                            Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                            for (int i = 0; i < Measurement_Count_Copy; i++)
                            {
                                Measurement_Data_Copy[i] = Measurement_Data_Copy[i] * Value;
                            }
                            Create_Waveform_Window("Multiplication Math Waveform [" + StartValue + ", " + EndValue + "]: Samples * " + Value, Value, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                            Measurement_Data_Copy = null;
                            Measurement_Data_DateTime = null;
                        }
                        catch (Exception Ex)
                        {
                            Insert_Log(Ex.Message, 1);
                            Insert_Log("Cannot create Multiplication (N Samples) (Samples * Value) Math Waveform. Try again.", 1);
                        }
                    });
                }
                else
                {
                    string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                    string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                    Task.Run(() =>
                    {
                        try
                        {
                            int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                            double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                            Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                            DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                            Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                            for (int i = 0; i < Measurement_Count_Copy; i++)
                            {
                                Measurement_Data_Copy[i] = Value * Measurement_Data_Copy[i];
                            }
                            Create_Waveform_Window("Multiplication Math Waveform [" + StartValue + ", " + EndValue + "]: " + Value + " * Samples", Value, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                            Measurement_Data_Copy = null;
                            Measurement_Data_DateTime = null;
                        }
                        catch (Exception Ex)
                        {
                            Insert_Log(Ex.Message, 1);
                            Insert_Log("Cannot create Multiplication (N Samples) (Value * Samples) Math Waveform. Try again.", 1);
                        }
                    });
                }
            }
            else
            {
                if (isValid == false)
                {
                    Multiplication_TextBox_Math_NSamples.Text = string.Empty;
                    Insert_Log("Cannot create Multiplication (N Samples) Math Waveform. Value's input field must only have numbers.", 1);
                }
                else
                {
                    Insert_Log("Cannot create Multiplication (N Samples) Math Waveform.", 1);
                }
            }
        }

        private void Division_Button_Math_NSamples_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValid, double Value) = Text_Num(Division_TextBox_Math_NSamples.Text, true, false);
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (IsValidRange == true & isValid == true & isValidGraphColor == true)
            {
                if (Value != 0)
                {
                    if (Division_NSamples_Samples_Value.IsSelected == true)
                    {
                        string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                        string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                        Task.Run(() =>
                        {
                            try
                            {
                                int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                                double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                                Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                                DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                                Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                                for (int i = 0; i < Measurement_Count_Copy; i++)
                                {
                                    Measurement_Data_Copy[i] = Measurement_Data_Copy[i] / Value;
                                }
                                Create_Waveform_Window("Division Math Waveform [" + StartValue + ", " + EndValue + "]: Samples / " + Value, Value, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                                Measurement_Data_Copy = null;
                                Measurement_Data_DateTime = null;
                            }
                            catch (Exception Ex)
                            {
                                Insert_Log(Ex.Message, 1);
                                Insert_Log("Cannot create Division (N Samples) (Samples / Value) Math Waveform. Try again.", 1);
                            }
                        });
                    }
                    else
                    {
                        string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                        string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                        Task.Run(() =>
                        {
                            try
                            {
                                int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                                double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                                Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                                DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                                Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                                for (int i = 0; i < Measurement_Count_Copy; i++)
                                {
                                    Measurement_Data_Copy[i] = Value / Measurement_Data_Copy[i];
                                    if (double.IsInfinity(Measurement_Data_Copy[i]) == true) //Check is answer is infinite, if yes then set it to 0
                                    {
                                        Measurement_Data_Copy[i] = 0;
                                    }
                                }
                                Create_Waveform_Window("Division Math Waveform [" + StartValue + ", " + EndValue + "]: " + Value + " / Samples", Value, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                                Measurement_Data_Copy = null;
                                Measurement_Data_DateTime = null;
                            }
                            catch (Exception Ex)
                            {
                                Insert_Log(Ex.Message, 1);
                                Insert_Log("Cannot create Division (N Samples) (Value / Samples) Math Waveform. Try again.", 1);
                            }
                        });
                    }
                }
                else
                {
                    Insert_Log("Cannot create Division (N Samples) (Value / Samples) Math Waveform." + " Cannot divide by " + Value, 1);
                }
            }
            else
            {
                if (isValid == false)
                {
                    Division_TextBox_Math_NSamples.Text = string.Empty;
                    Insert_Log("Cannot create Division (N Samples) Math Waveform. Value's input field must only have numbers, no text.", 1);
                }
                else
                {
                    Insert_Log("Cannot create Division (N Samples) Math Waveform.", 1);
                }
            }
        }

        private void Percentage_Error_Button_NSamples_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValid, double Value) = Text_Num(Percentage_Error_TextBox_NSamples.Text, true, false);
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (isValid == true & isValidGraphColor == true & IsValidRange == true)
            {
                if (Value != 0)
                {
                    string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                    string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                    Task.Run(() =>
                    {
                        try
                        {
                            int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                            double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                            Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                            DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                            Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                            for (int i = 0; i < Measurement_Count_Copy; i++)
                            {
                                Measurement_Data_Copy[i] = Math.Abs((Measurement_Data_Copy[i] - Value) / Value) * 100;
                            }
                            Create_Waveform_Window("% Error Math Waveform [" + StartValue + ", " + EndValue + "]: |(Samples - " + Value + ") / " + Value + "| x 100", Value, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                            Measurement_Data_Copy = null;
                            Measurement_Data_DateTime = null;
                        }
                        catch (Exception Ex)
                        {
                            Insert_Log(Ex.Message, 1);
                            Insert_Log("Cannot create % Error Math Waveform (N Samples). Try again.", 1);
                        }
                    });
                }
                else
                {
                    Insert_Log("Cannot create % Error Math Waveform (N Samples): Value must not be " + Value, 1);
                }
            }
            else
            {
                if (isValid == false)
                {
                    Insert_Log("Cannot create % Error Math Waveform (N Samples). Value must be a real number.", 1);
                }
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create % Error Math Waveform (N Samples). Graph Color values are not valid.", 1);
                }
                if (IsValidRange == false)
                {
                    Insert_Log("Cannot create % Error Math Waveform (N Samples). Check N Samples Start and End input values.", 1);
                }
            }
        }

        private void DB_Button_Math_NSamples_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValid_DB_1_Value, double DB_1_Value) = Text_Num(DB_1_Math_NSamples.Text, true, false);
            (bool isValid_DB_2_Value, double DB_2_Value) = Text_Num(DB_2_Math_NSamples.Text, false, false);
            (bool isValid_DB_3_Value, double DB_3_Value) = Text_Num(DB_3_Math_NSamples.Text, false, false);
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (isValid_DB_1_Value == true & isValid_DB_2_Value == true & isValid_DB_3_Value == true & isValidGraphColor == true & IsValidRange == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            Measurement_Data_Copy[i] = (DB_1_Value) * (Math.Log(((Math.Abs(Measurement_Data_Copy[i])) / DB_3_Value), DB_2_Value));
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("DB (N Samples Math Waveform [" + StartValue + ", " + EndValue + "]): " + DB_1_Value + " x log" + DB_2_Value + " (Samples / " + DB_3_Value + ")", DB_3_Value, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create DB (N Samples) Math Waveform. Try again.", 1);
                    }
                });
            }
            else
            {
                if (IsValidRange == false) 
                {
                    Insert_Log("Cannot create DB (N Samples) Math Waveform. Check N Samples Start and End input values.", 1);
                }
                if ((isValid_DB_1_Value == false) || (isValid_DB_2_Value == false) || (isValid_DB_3_Value == false))
                {
                    Insert_Log("Cannot create DB (N Samples) Math Waveform. The base and the argument of the logarithm must be positive. Check your inputted values.", 1);
                }
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create DB (N Samples) Math Waveform. Check your Graph Color values.", 1);
                }
            }
        }

        private void DBM_Button_Math_NSamples_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValid_DBM_1_Value, double DBM_1_Value) = Text_Num(DBM_1_Math_NSamples.Text, true, false);
            (bool isValid_DBM_2_Value, double DBM_2_Value) = Text_Num(DBM_2_Math_NSamples.Text, false, false);
            (bool isValid_DBM_3_Value, double DBM_3_Value) = Text_Num(DBM_3_Math_NSamples.Text, false, false);
            (bool isValid_DBM_4_Value, double DBM_4_Value) = Text_Num(DBM_4_Math_NSamples.Text, false, false);
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (isValid_DBM_1_Value == true & isValid_DBM_2_Value == true & isValid_DBM_3_Value == true & isValid_DBM_4_Value == true & isValidGraphColor == true & IsValidRange == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            Measurement_Data_Copy[i] = (DBM_1_Value) * (Math.Log(((((Math.Pow(Measurement_Data_Copy[i], 2)) / DBM_3_Value)) / DBM_4_Value), DBM_2_Value));
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("DBM (N Samples Math Waveform [" + StartValue + ", " + EndValue + "]): " + DBM_1_Value + " x log" + DBM_2_Value + " ((Samples^2 / " + DBM_3_Value + ") / " + DBM_4_Value + ")", DBM_3_Value, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create DBM (N Samples) Math Waveform. Try again.", 1);
                    }
                });
            }
            else
            {
                if (IsValidRange == true) 
                {
                    Insert_Log("Cannot create DBM (N Samples) Math Waveform. Check N Samples Start and End input values.", 1);
                }
                if ((isValid_DBM_1_Value == false) || (isValid_DBM_2_Value == false) || (isValid_DBM_3_Value == false) || (isValid_DBM_4_Value == false))
                {
                    Insert_Log("Cannot create DBM (N Samples) Math Waveform. The base and the argument of the logarithm must be positive. Check your inputted values.", 1);
                }
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create DBM (N Samples) Math Waveform. Check your Graph Color values.", 1);
                }
            }
        }

        private void Value_Power_NSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            (bool isValid, double Value) = Text_Num(Value_Power_NSample_Text.Text, true, false);
            if (isValidGraphColor == true & isValid == true & IsValidRange == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            Measurement_Data_Copy[i] = Math.Pow(Value, Measurement_Data_Copy[i]);
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("(Value)^(Samples) Math Waveform [" + StartValue + ", " + EndValue + "]", 0, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create (Value)^(Samples) (N Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create (Value)^(Samples) (N Samples) Math Waveform. Check your Graph Color values.", 1);
                }
                if (isValid == false)
                {
                    Insert_Log("Cannot create (Value)^(Samples) (N Samples) Math Waveform. Check your Value.", 1);
                }
                if (IsValidRange == true)
                {
                    Insert_Log("Check N Samples Start and End input values.", 1);
                }
            }
        }

        private void NSample_Power_Value_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            (bool isValid, double Value) = Text_Num(NSample_Power_Value_Text.Text, true, false);
            if (isValidGraphColor == true & isValid == true & IsValidRange == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            Measurement_Data_Copy[i] = Math.Pow(Measurement_Data_Copy[i], Value);
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("(Samples)^(Value) Math Waveform [" + StartValue + ", " + EndValue + "]", 0, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create (Samples)^(Value) (N Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create (Samples)^(Value) (N Samples) Math Waveform. Check your Graph Color values.", 1);
                }
                if (isValid == false)
                {
                    Insert_Log("Cannot create (Samples)^(Value) (N Samples) Math Waveform. Check your Value.", 1);
                }
                if (IsValidRange == true)
                {
                    Insert_Log("Check N Samples Start and End input values.", 1);
                }
            }
        }

        private void Log_NSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (isValidGraphColor == true & IsValidRange == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            Measurement_Data_Copy[i] = Math.Log10(Measurement_Data_Copy[i]);
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Logarithm Math Waveform [" + StartValue + ", " + EndValue + "]", 0, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Logarithm (N Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Logarithm (N Samples) Math Waveform. Check your Graph Color values.", 1);
                }
                if (IsValidRange == true)
                {
                    Insert_Log("Check N Samples Start and End input values.", 1);
                }
            }
        }

        private void Ln_NSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (isValidGraphColor == true & IsValidRange == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            Measurement_Data_Copy[i] = Math.Log(Measurement_Data_Copy[i]);
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Natural Logarithm Math Waveform [" + StartValue + ", " + EndValue + "]", 0, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Natural Logarithm (N Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Natural Logarithm (N Samples) Math Waveform. Check your Graph Color values.", 1);
                }
                if (IsValidRange == true)
                {
                    Insert_Log("Check N Samples Start and End input values.", 1);
                }
            }
        }

        private void Square_NSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (isValidGraphColor == true & IsValidRange == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            Measurement_Data_Copy[i] = Math.Sqrt(Measurement_Data_Copy[i]);
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Square Root Math Waveform [" + StartValue + ", " + EndValue + "]", 0, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Square Root (N Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Square Root (N Samples) Math Waveform. Check your Graph Color values.", 1);
                }
                if (IsValidRange == true)
                {
                    Insert_Log("Check N Samples Start and End input values.", 1);
                }
            }
        }

        private void Abs_NSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (isValidGraphColor == true & IsValidRange == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            Measurement_Data_Copy[i] = Math.Abs(Measurement_Data_Copy[i]);
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Absolute Value Math Waveform [" + StartValue + ", " + EndValue + "]", 0, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Absolute Value (N Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Absolute Value (N Samples) Math Waveform. Check your Graph Color values.", 1);
                }
                if (IsValidRange == true)
                {
                    Insert_Log("Check N Samples Start and End input values.", 1);
                }
            }
        }

        private void Sine_NSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (isValidGraphColor == true & IsValidRange == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                bool inDegrees = Sine_NSample_Degrees.IsSelected;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            if (inDegrees == true)
                            {
                                Measurement_Data_Copy[i] = (Math.Sin(Measurement_Data_Copy[i]) * (180 / Math.PI));
                            }
                            else
                            {
                                Measurement_Data_Copy[i] = Math.Sin(Measurement_Data_Copy[i]);
                            }
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Sine Math Waveform [" + StartValue + ", " + EndValue + "]", 0, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Sine (N Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Sine (N Samples) Math Waveform. Check your Graph Color values.", 1);
                }
                if (IsValidRange == true)
                {
                    Insert_Log("Check N Samples Start and End input values.", 1);
                }
            }
        }

        private void Cosine_NSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (isValidGraphColor == true & IsValidRange == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                bool inDegrees = Cosine_NSample_Degrees.IsSelected;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            if (inDegrees == true)
                            {
                                Measurement_Data_Copy[i] = (Math.Cos(Measurement_Data_Copy[i]) * (180 / Math.PI));
                            }
                            else
                            {
                                Measurement_Data_Copy[i] = Math.Cos(Measurement_Data_Copy[i]);
                            }
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Cosine Math Waveform [" + StartValue + ", " + EndValue + "]", 0, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Cosine (N Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Cosine (N Samples) Math Waveform. Check your Graph Color values.", 1);
                }
                if (IsValidRange == true)
                {
                    Insert_Log("Check N Samples Start and End input values.", 1);
                }
            }
        }

        private void Tangent_NSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (isValidGraphColor == true & IsValidRange == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                bool inDegrees = Tangent_NSample_Degrees.IsSelected;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            if (inDegrees == true)
                            {
                                Measurement_Data_Copy[i] = (Math.Tan(Measurement_Data_Copy[i]) * (180 / Math.PI));
                            }
                            else
                            {
                                Measurement_Data_Copy[i] = Math.Tan(Measurement_Data_Copy[i]);
                            }
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Tangent Math Waveform [" + StartValue + ", " + EndValue + "]", 0, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Tangent (N Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Tangent (N Samples) Math Waveform. Check your Graph Color values.", 1);
                }
                if (IsValidRange == true)
                {
                    Insert_Log("Check N Samples Start and End input values.", 1);
                }
            }
        }

        private void Inverse_Sine_NSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (isValidGraphColor == true & IsValidRange == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                bool inDegrees = Inverse_Sine_NSample_Degrees.IsSelected;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            if (inDegrees == true)
                            {
                                Measurement_Data_Copy[i] = (Math.Asin(Measurement_Data_Copy[i]) * (180 / Math.PI));
                            }
                            else
                            {
                                Measurement_Data_Copy[i] = Math.Asin(Measurement_Data_Copy[i]);
                            }
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Inverse Sine Math Waveform [" + StartValue + ", " + EndValue + "]", 0, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Inverse Sine (N Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Inverse Sine (N Samples) Math Waveform. Check your Graph Color values.", 1);
                }
                if (IsValidRange == true)
                {
                    Insert_Log("Check N Samples Start and End input values.", 1);
                }
            }
        }

        private void Inverse_Cosine_NSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (isValidGraphColor == true & IsValidRange == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                bool inDegrees = Inverse_Cosine_NSample_Degrees.IsSelected;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            if (inDegrees == true)
                            {
                                Measurement_Data_Copy[i] = (Math.Acos(Measurement_Data_Copy[i]) * (180 / Math.PI));
                            }
                            else
                            {
                                Measurement_Data_Copy[i] = Math.Acos(Measurement_Data_Copy[i]);
                            }
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Inverse Cosine Math Waveform [" + StartValue + ", " + EndValue + "]", 0, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Inverse Cosine (N Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Inverse Cosine (N Samples) Math Waveform. Check your Graph Color values.", 1);
                }
                if (IsValidRange == true)
                {
                    Insert_Log("Check N Samples Start and End input values.", 1);
                }
            }
        }

        private void Inverse_Tangent_NSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (isValidGraphColor == true & IsValidRange == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                bool inDegrees = Inverse_Tangent_NSample_Degrees.IsSelected;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            if (inDegrees == true)
                            {
                                Measurement_Data_Copy[i] = (Math.Atan(Measurement_Data_Copy[i]) * (180 / Math.PI));
                            }
                            else
                            {
                                Measurement_Data_Copy[i] = Math.Atan(Measurement_Data_Copy[i]);
                            }
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Inverse Tangent Math Waveform [" + StartValue + ", " + EndValue + "]", 0, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Inverse Tangent (N Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Inverse Tangent (N Samples) Math Waveform. Check your Graph Color values.", 1);
                }
                if (IsValidRange == true)
                {
                    Insert_Log("Check N Samples Start and End input values.", 1);
                }
            }
        }

        private void Hyperbolic_Sine_NSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (isValidGraphColor == true & IsValidRange == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                bool inDegrees = Hyperbolic_Sine_NSample_Degrees.IsSelected;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            if (inDegrees == true)
                            {
                                Measurement_Data_Copy[i] = (Math.Sinh(Measurement_Data_Copy[i]) * (180 / Math.PI));
                            }
                            else
                            {
                                Measurement_Data_Copy[i] = Math.Sinh(Measurement_Data_Copy[i]);
                            }
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Hyperbolic Sine Math Waveform [" + StartValue + ", " + EndValue + "]", 0, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Hyperbolic Sine (N Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Hyperbolic Sine (N Samples) Math Waveform. Check your Graph Color values.", 1);
                }
                if (IsValidRange == true)
                {
                    Insert_Log("Check N Samples Start and End input values.", 1);
                }
            }
        }

        private void Hyperbolic_Cosine_NSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (isValidGraphColor == true & IsValidRange == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                bool inDegrees = Hyperbolic_Cosine_NSample_Degrees.IsSelected;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            if (inDegrees == true)
                            {
                                Measurement_Data_Copy[i] = (Math.Cosh(Measurement_Data_Copy[i]) * (180 / Math.PI));
                            }
                            else
                            {
                                Measurement_Data_Copy[i] = Math.Cosh(Measurement_Data_Copy[i]);
                            }
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Hyperbolic Cosine Math Waveform [" + StartValue + ", " + EndValue + "]", 0, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Hyperbolic Cosine (N Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Hyperbolic Cosine (N Samples) Math Waveform. Check your Graph Color values.", 1);
                }
                if (IsValidRange == true)
                {
                    Insert_Log("Check N Samples Start and End input values.", 1);
                }
            }
        }

        private void Hyperbolic_Tangent_NSample_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = MathNsamples_Range();
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (isValidGraphColor == true & IsValidRange == true)
            {
                string Graph_Title = GraphTitle_TextBox_Math_NSamples.Text;
                string Y_Axis_Title = YAxis_TextBox_Math_NSamples.Text;
                bool inDegrees = Hyperbolic_Tangent_NSample_Degrees.IsSelected;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                        for (int i = 0; i < Measurement_Count_Copy; i++)
                        {
                            if (inDegrees == true)
                            {
                                Measurement_Data_Copy[i] = (Math.Tanh(Measurement_Data_Copy[i]) * (180 / Math.PI));
                            }
                            else
                            {
                                Measurement_Data_Copy[i] = Math.Tanh(Measurement_Data_Copy[i]);
                            }
                            if (double.IsNaN(Measurement_Data_Copy[i]) || double.IsInfinity(Measurement_Data_Copy[i]))
                            {
                                Measurement_Data_Copy[i] = 0;
                            }
                        }
                        Create_Waveform_Window("Hyperbolic Tangent Math Waveform [" + StartValue + ", " + EndValue + "]", 0, StartValue, EndValue, Graph_Title, Y_Axis_Title, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Hyperbolic Tangent (N Samples) Math Waveform, try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Insert_Log("Cannot create Hyperbolic Tangent (N Samples) Math Waveform. Check your Graph Color values.", 1);
                }
                if (IsValidRange == true)
                {
                    Insert_Log("Check N Samples Start and End input values.", 1);
                }
            }
        }

        private void GraphColor_SetButton_Math_NSamples_Click(object sender, RoutedEventArgs e)
        {
            (bool isValid, int Value_Red, int Value_Green, int Value_Blue) = GraphColor_Math_NSamples_Check();
            if (isValid == true)
            {
                GraphColor_Preview_Math_NSamples.Fill = new SolidColorBrush(Color.FromArgb(255, (byte)(Value_Red), (byte)(Value_Green), (byte)(Value_Blue)));
            }
        }

        private (bool, int, int, int) GraphColor_Math_NSamples_Check()
        {
            (bool isValid_Red, double Value_Red) = Text_Num(Red_GraphColor_TextBox_Math_NSamples.Text, false, true);
            (bool isValid_Green, double Value_Green) = Text_Num(Green_GraphColor_TextBox_Math_NSamples.Text, false, true);
            (bool isValid_Blue, double Value_Blue) = Text_Num(Blue_GraphColor_TextBox_Math_NSamples.Text, false, true);
            if ((isValid_Red == true) & (isValid_Green == true) & (isValid_Blue == true))
            {
                if ((Value_Red <= 255) & (Value_Green <= 255) & (Value_Blue <= 255))
                {
                    return (true, (int)Value_Red, (int)Value_Green, (int)Value_Blue);
                }
                else
                {
                    if (Value_Red > 255)
                    {
                        Red_GraphColor_TextBox_Math_NSamples.Text = string.Empty;
                    }
                    if (Value_Green > 255)
                    {
                        Green_GraphColor_TextBox_Math_NSamples.Text = string.Empty;
                    }
                    if (Value_Blue > 255)
                    {
                        Blue_GraphColor_TextBox_Math_NSamples.Text = string.Empty;
                    }
                    Insert_Log("Math (N Samples) Graph Color values must be positive integers and must be between 0 and 255.", 1);
                    return (false, 0, 0, 0);
                }
            }
            else
            {
                if (isValid_Red == false)
                {
                    Red_GraphColor_TextBox_Math_NSamples.Text = string.Empty;
                }
                if (isValid_Green == false)
                {
                    Green_GraphColor_TextBox_Math_NSamples.Text = string.Empty;
                }
                if (isValid_Blue == false)
                {
                    Blue_GraphColor_TextBox_Math_NSamples.Text = string.Empty;
                }
                Insert_Log("Math (N Samples) Graph Color values must be positive integers and must be between 0 and 255.", 1);
                return (false, 0, 0, 0);
            }

        }

        private void GraphColor_RandomizeButton_Math_NSamples_Click(object sender, RoutedEventArgs e)
        {
            Random RGB_Value = new Random();
            int Value_Red = RGB_Value.Next(0, 255);
            int Value_Green = RGB_Value.Next(0, 255);
            int Value_Blue = RGB_Value.Next(0, 255);
            Red_GraphColor_TextBox_Math_NSamples.Text = Value_Red.ToString();
            Green_GraphColor_TextBox_Math_NSamples.Text = Value_Green.ToString();
            Blue_GraphColor_TextBox_Math_NSamples.Text = Value_Blue.ToString();
            GraphColor_Preview_Math_NSamples.Fill = new SolidColorBrush(Color.FromArgb(255, (byte)(Value_Red), (byte)(Value_Green), (byte)(Value_Blue)));
        }

        //--------------------------- Math (N Samples)----------------------

        //--------------------------- Histogram (All Samples)----------------------
        private (bool, int, int) Histogram_Range()
        {
            (bool isValid_Start, double Start_Value) = Text_Num(Start_Histogram_NSamples_TextBox.Text, false, true);
            (bool isValid_End, double End_Value) = Text_Num(End_Histogram_NSamples_TextBox.Text, false, true);
            if (isValid_Start == true & isValid_End == true)
            {
                if (Start_Value < End_Value)
                {
                    if (End_Value < Measurement_Count)
                    {
                        return (true, (int)Start_Value, (int)End_Value);
                    }
                    else
                    {
                        Insert_Log("Histogram N Samples End Value must be less than or equal to Total N Samples Captured.", 1);
                        return (false, 0, 0);
                    }
                }
                else
                {
                    Insert_Log("Histogram N Samples Start Value must be less than End Value.", 1);
                    return (false, 0, 0);
                }
            }
            else
            {
                if (isValid_Start == false)
                {
                    Insert_Log("Histogram N Samples Start Value is invalid. Value must be an positive integer.", 1);
                    Start_Histogram_NSamples_TextBox.Text = String.Empty;
                }
                if (isValid_End == false)
                {
                    Insert_Log("Histogram N Samples End Value is invalid. Value must be an positive integer.", 1);
                    End_Histogram_NSamples_TextBox.Text = String.Empty;
                }
                return (false, 0, 0);
            }
        }

        private void Calculate_Histogram_AllSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = Histogram_Color_Check();
            (bool isBinSize, double BinSize) = Text_Num(BinSize_TextBox_Histogram_NSamples.Text, false, false);
            (bool isBarWidth, double BarWidth) = Text_Num(BarWidth_TextBox_Histogram_NSamples.Text, false, false);
            (bool isBarThickness, double BarThickness) = Text_Num(BarBorder_TextBox_Histogram_NSamples.Text, false, false);
            if (isValidGraphColor == true & isBinSize == true & isBarWidth == true & isBarThickness)
            {
                bool Curve = false;
                if (MeanCurve_CheckBox_Histogram_NSamples.IsChecked == true)
                {
                    Curve = true;
                }
                else
                {
                    Curve = false;
                };
                string Graph_Title = GraphTitle_TextBox_Histogram_NSamples.Text;
                string X_Axis_Label = XAxisTitle_TextBox_Histogram_NSamples.Text;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = Measurement_Count;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, Measurement_Data_DateTime, Measurement_Count_Copy);

                        Create_Histogram_Window("Histogram Waveform [" + 0 + ", " + Measurement_Count_Copy + "]", 0, Measurement_Count_Copy, Graph_Title, X_Axis_Label, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime, BinSize, BarWidth, (float)BarThickness, Curve);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Histogram Waveform. Try again.", 1);
                    }
                });
            }
            else
            {
                if (isValidGraphColor == false)
                {
                    Red_Histogram_TextBox.Text = string.Empty;
                    Green_Histogram_TextBox.Text = string.Empty;
                    Blue_Histogram_TextBox.Text = string.Empty;
                    Insert_Log("Histogram [All Samples]: Color values are invalid. Set new values between 0 and 255 and try again.", 1);
                }
                if (isBinSize == false)
                {
                    BinSize_TextBox_Histogram_NSamples.Text = string.Empty;
                    Insert_Log("Histogram [All Samples]: Bin Size is invalid. Must be a positive number.", 1);
                }
                if (isBarWidth == false)
                {
                    BarWidth_TextBox_Histogram_NSamples.Text = string.Empty;
                    Insert_Log("Histogram [All Samples]: Bar Width is invalid. Must be a positive number.", 1);
                }
                if (isBarThickness == false)
                {
                    BarBorder_TextBox_Histogram_NSamples.Text = string.Empty;
                    Insert_Log("Histogram [All Samples]: Bar Border thickness is invalid. Must be a positive number.", 1);
                }
            }
        }

        //--------------------------- Histogram (All Samples)----------------------

        //--------------------------- Histogram (N Samples)----------------------

        private void Calculate_Histogram_NSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = Histogram_Range();
            (bool isValidGraphColor, int Value_Red, int Value_Green, int Value_Blue) = Histogram_Color_Check();
            (bool isBinSize, double BinSize) = Text_Num(BinSize_TextBox_Histogram_NSamples.Text, false, false);
            (bool isBarWidth, double BarWidth) = Text_Num(BarWidth_TextBox_Histogram_NSamples.Text, false, false);
            (bool isBarThickness, double BarThickness) = Text_Num(BarBorder_TextBox_Histogram_NSamples.Text, false, false);
            if (IsValidRange == true & isValidGraphColor == true & isBinSize == true & isBarWidth == true & isBarThickness)
            {
                bool Curve = false;
                if (MeanCurve_CheckBox_Histogram_NSamples.IsChecked == true)
                {
                    Curve = true;
                }
                else
                {
                    Curve = false;
                };
                string Graph_Title = GraphTitle_TextBox_Histogram_NSamples.Text;
                string X_Axis_Label = XAxisTitle_TextBox_Histogram_NSamples.Text;
                Task.Run(() =>
                {
                    try
                    {
                        int Measurement_Count_Copy = (EndValue - StartValue) + 1;
                        double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                        Array.Copy(Measurement_Data, StartValue, Measurement_Data_Copy, 0, Measurement_Count_Copy);

                        DateTime[] Measurement_Data_DateTime = new DateTime[Measurement_Count_Copy];
                        Array.Copy(Measurement_DateTime, StartValue, Measurement_Data_DateTime, 0, Measurement_Count_Copy);

                        Create_Histogram_Window("Histogram Waveform [" + StartValue + ", " + EndValue + "]", StartValue, EndValue, Graph_Title, X_Axis_Label, Value_Red, Value_Green, Value_Blue, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime, BinSize, BarWidth, (float)BarThickness, Curve);
                        Measurement_Data_Copy = null;
                        Measurement_Data_DateTime = null;
                    }
                    catch (Exception Ex)
                    {
                        Insert_Log(Ex.Message, 1);
                        Insert_Log("Cannot create Histogram Waveform. Try again.", 1);
                    }
                });
            }
            else 
            {
                if (IsValidRange == false) 
                {
                    Start_Histogram_NSamples_TextBox.Text = string.Empty;
                    End_Histogram_NSamples_TextBox.Text = string.Empty;
                    Insert_Log("Histogram [N Samples]: Start, End Range is invalid. Set new range and try again.", 1);
                }
                if (isValidGraphColor == false)
                {
                    Red_Histogram_TextBox.Text = string.Empty;
                    Green_Histogram_TextBox.Text = string.Empty;
                    Blue_Histogram_TextBox.Text = string.Empty;
                    Insert_Log("Histogram [N Samples]: Color values are invalid. Set new values between 0 and 255 and try again.", 1);
                }
                if (isBinSize == false)
                {
                    BinSize_TextBox_Histogram_NSamples.Text = string.Empty;
                    Insert_Log("Histogram [N Samples]: Bin Size is invalid. Must be a positive number.", 1);
                }
                if (isBarWidth == false)
                {
                    BarWidth_TextBox_Histogram_NSamples.Text = string.Empty;
                    Insert_Log("Histogram [N Samples]: Bar Width is invalid. Must be a positive number.", 1);
                }
                if (isBarThickness == false)
                {
                    BarBorder_TextBox_Histogram_NSamples.Text = string.Empty;
                    Insert_Log("Histogram [N Samples]: Bar Border thickness is invalid. Must be a positive number.", 1);
                }
            }
        }

        private void Histogram_Color_Set_Click(object sender, RoutedEventArgs e)
        {
            (bool isValid, int Value_Red, int Value_Green, int Value_Blue) = Histogram_Color_Check();
            if (isValid == true)
            {
                GraphColor_Histogram.Fill = new SolidColorBrush(Color.FromArgb(255, (byte)(Value_Red), (byte)(Value_Green), (byte)(Value_Blue)));
            }
        }

        private (bool, int, int, int) Histogram_Color_Check()
        {
            (bool isValid_Red, double Value_Red) = Text_Num(Red_Histogram_TextBox.Text, false, true);
            (bool isValid_Green, double Value_Green) = Text_Num(Green_Histogram_TextBox.Text, false, true);
            (bool isValid_Blue, double Value_Blue) = Text_Num(Blue_Histogram_TextBox.Text, false, true);
            if ((isValid_Red == true) & (isValid_Green == true) & (isValid_Blue == true))
            {
                if ((Value_Red <= 255) & (Value_Green <= 255) & (Value_Blue <= 255))
                {
                    return (true, (int)Value_Red, (int)Value_Green, (int)Value_Blue);
                }
                else
                {
                    if (Value_Red > 255)
                    {
                        Red_Histogram_TextBox.Text = string.Empty;
                    }
                    if (Value_Green > 255)
                    {
                        Green_Histogram_TextBox.Text = string.Empty;
                    }
                    if (Value_Blue > 255)
                    {
                        Blue_Histogram_TextBox.Text = string.Empty;
                    }
                    Insert_Log("Histogram Graph Color values must be positive integers and must be between 0 and 255.", 1);
                    return (false, 0, 0, 0);
                }
            }
            else
            {
                if (isValid_Red == false)
                {
                    Red_Histogram_TextBox.Text = string.Empty;
                }
                if (isValid_Green == false)
                {
                    Green_Histogram_TextBox.Text = string.Empty;
                }
                if (isValid_Blue == false)
                {
                    Blue_Histogram_TextBox.Text = string.Empty;
                }
                Insert_Log("Histogram Graph Color values must be positive integers and must be between 0 and 255.", 1);
                return (false, 0, 0, 0);
            }
        }

        private void Histogram_Color_RandomizeButton_Click(object sender, RoutedEventArgs e)
        {
            Random RGB_Value = new Random();
            int Value_Red = RGB_Value.Next(0, 255);
            int Value_Green = RGB_Value.Next(0, 255);
            int Value_Blue = RGB_Value.Next(0, 255);
            Red_Histogram_TextBox.Text = Value_Red.ToString();
            Green_Histogram_TextBox.Text = Value_Green.ToString();
            Blue_Histogram_TextBox.Text = Value_Blue.ToString();
            GraphColor_Histogram.Fill = new SolidColorBrush(Color.FromArgb(255, (byte)(Value_Red), (byte)(Value_Green), (byte)(Value_Blue)));
        }

        //--------------------------- Histogram (N Samples)----------------------

        //---------------------------- Misc -----------------------------------------
        private (bool, int, int) TimeDifference_Range()
        {
            (bool isValid_Start, double Start_Value) = Text_Num(Start_TimeDifference_NSamples_TextBox.Text, false, true);
            (bool isValid_End, double End_Value) = Text_Num(End_TimeDifference_NSamples_TextBox.Text, false, true);
            if (isValid_Start == true & isValid_End == true)
            {
                if (Start_Value < End_Value)
                {
                    if (End_Value < Measurement_Count)
                    {
                        return (true, (int)Start_Value, (int)End_Value);
                    }
                    else
                    {
                        Insert_Log("Time Difference End Value must be less than or equal to Total N Samples Captured.", 1);
                        return (false, 0, 0);
                    }
                }
                else
                {
                    Insert_Log("Time Difference Start Value must be less than End Value.", 1);
                    return (false, 0, 0);
                }
            }
            else
            {
                if (isValid_Start == false)
                {
                    Insert_Log("Time Difference Start Value is invalid. Value must be an positive integer.", 1);
                    Start_Histogram_NSamples_TextBox.Text = String.Empty;
                }
                if (isValid_End == false)
                {
                    Insert_Log("Time Difference End Value is invalid. Value must be an positive integer.", 1);
                    End_Histogram_NSamples_TextBox.Text = String.Empty;
                }
                return (false, 0, 0);
            }
        }

        private void Calculate_TimeDifference_NSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            (bool IsValidRange, int StartValue, int EndValue) = TimeDifference_Range();
            if (IsValidRange == true)
            {
                try
                {
                    if (TimeDifference_S_N.IsSelected == true)
                    {
                        TimeSpan duration = DateTime.Parse(Measurement_DateTime[EndValue].ToString()).Subtract(DateTime.Parse(Measurement_DateTime[StartValue].ToString()));
                        Insert_Log("[Start Sample: " + Measurement_DateTime[StartValue].ToString() + ", " + (decimal)Measurement_Data[StartValue] + Measurement_Unit + ", End Sample: " + Measurement_DateTime[EndValue].ToString() + ", " + (decimal)Measurement_Data[EndValue] + Measurement_Unit + "]", 0);
                        Insert_Log("∆ Time Difference between [Start Sample: " + StartValue + ", End Sample: " + EndValue + "] is " + duration.TotalSeconds + " Seconds", 0);
                    }
                    else if (TimeDifference_M_N.IsSelected == true)
                    {
                        TimeSpan duration = DateTime.Parse(Measurement_DateTime[EndValue].ToString()).Subtract(DateTime.Parse(Measurement_DateTime[StartValue].ToString()));
                        Insert_Log("[Start Sample: " + Measurement_DateTime[StartValue].ToString() + ", " + (decimal)Measurement_Data[StartValue] + Measurement_Unit + ", End Sample: " + Measurement_DateTime[EndValue].ToString() + ", " + (decimal)Measurement_Data[EndValue] + Measurement_Unit + "]", 0);
                        Insert_Log("∆ Time Difference between [Start Sample: " + StartValue + ", End Sample: " + EndValue + "] is " + duration.TotalMinutes + " Minutes", 0);
                    }
                    else if (TimeDifference_H_N.IsSelected == true)
                    {
                        TimeSpan duration = DateTime.Parse(Measurement_DateTime[EndValue].ToString()).Subtract(DateTime.Parse(Measurement_DateTime[StartValue].ToString()));
                        Insert_Log("[Start Sample: " + Measurement_DateTime[StartValue].ToString() + ", " + (decimal)Measurement_Data[StartValue] + Measurement_Unit + ", End Sample: " + Measurement_DateTime[EndValue].ToString() + ", " + (decimal)Measurement_Data[EndValue] + Measurement_Unit + "]", 0);
                        Insert_Log("∆ Time Difference between [Start Sample: " + StartValue + ", End Sample: " + EndValue + "] is " + duration.TotalHours + " Hours", 0);
                    }
                }
                catch (Exception)
                {
                    Insert_Log("Time Difference [N Samples]: Calculation failed. Try again.", 1);
                }
            }
        }

        private void Calculate_TimeDifference_AllSamples_Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TimeDifference_S_All.IsSelected == true)
                {
                    TimeSpan duration = DateTime.Parse(Measurement_DateTime[(Measurement_Count - 1)].ToString()).Subtract(DateTime.Parse(Measurement_DateTime[0].ToString()));
                    Insert_Log("[Start Sample: " + Measurement_DateTime[0].ToString() + ", " + (decimal)Measurement_Data[0] + Measurement_Unit + ", End Sample: " + Measurement_DateTime[(Measurement_Count - 1)].ToString() + ", " + (decimal)Measurement_Data[(Measurement_Count - 1)] + Measurement_Unit + "]", 0);
                    Insert_Log("∆ Time Difference between [Start Sample: " + 0 + ", End Sample: " + (Measurement_Count - 1) + "] is " + duration.TotalSeconds + " Seconds", 0);
                }
                else if (TimeDifference_M_All.IsSelected == true)
                {
                    TimeSpan duration = DateTime.Parse(Measurement_DateTime[(Measurement_Count - 1)].ToString()).Subtract(DateTime.Parse(Measurement_DateTime[0].ToString()));
                    Insert_Log("[Start Sample: " + Measurement_DateTime[0].ToString() + ", " + (decimal)Measurement_Data[0] + Measurement_Unit + ", End Sample: " + Measurement_DateTime[(Measurement_Count - 1)].ToString() + ", " + (decimal)Measurement_Data[(Measurement_Count - 1)] + Measurement_Unit + "]", 0);
                    Insert_Log("∆ Time Difference between [Start Sample: " + 0 + ", End Sample: " + (Measurement_Count - 1) + "] is " + duration.TotalMinutes + " Minutes", 0);
                }
                else if (TimeDifference_H_All.IsSelected == true)
                {
                    TimeSpan duration = DateTime.Parse(Measurement_DateTime[(Measurement_Count - 1)].ToString()).Subtract(DateTime.Parse(Measurement_DateTime[0].ToString()));
                    Insert_Log("[Start Sample: " + Measurement_DateTime[0].ToString() + ", " + (decimal)Measurement_Data[0] + Measurement_Unit + ", End Sample: " + Measurement_DateTime[(Measurement_Count - 1)].ToString() + ", " + (decimal)Measurement_Data[(Measurement_Count - 1)] + Measurement_Unit + "]", 0);
                    Insert_Log("∆ Time Difference between [Start Sample: " + 0 + ", End Sample: " + (Measurement_Count - 1) + "] is " + duration.TotalHours + " Hours", 0);
                }
            }
            catch (Exception)
            {
                Insert_Log("Time Difference [All Samples]: Calculation failed. Try again.", 1);
            }
        }

        //---------------------------- Misc -----------------------------------------

        //Inserts a message into the output log control
        private void Insert_Log(string Message, int Code)
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
                    Status = "[Statistics]";
                    Color = Brushes.DodgerBlue;
                    break;
                case 4:
                    Status = "[Math]";
                    Color = Brushes.BlueViolet;
                    break;
                case 5:
                    Status = "[Message]";
                    Color = Brushes.Black;
                    break;
                default:
                    Status = "";
                    Color = Brushes.Magenta;
                    break;
            }
            this.Dispatcher.Invoke(DispatcherPriority.Background, new ThreadStart(delegate
            {
                Output_Log.Inlines.Add(new Run("[" + date + "]" + " " + Status + " " + Message + "\n") { Foreground = Color });
                if (Auto_Scroll.IsChecked == true)
                {
                    Output_Log_Scroll.ScrollToBottom();
                }
                Output_Log_Tab.IsSelected = true;
            }));
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

        //Creates Math Waveform Windows
        private void Create_Waveform_Window(string Window_Title, double Value, int Start_Sample, int End_Sample, string Graph_Title, string Y_Axis_Label, int Red, int Green, int Blue, double[] Measurement_Data, int Measurement_Count, DateTime[] Measurement_DateTime)
        {
            try
            {
                Thread Waveform_Thread = new Thread(new ThreadStart(() =>
                {
                    Math_Waveform Calculate_Waveform = new Math_Waveform(Graph_Owner, Window_Title, Value, Start_Sample, End_Sample, Graph_Title, Y_Axis_Label, Red, Green, Blue, Measurement_Data, Measurement_Count, Measurement_DateTime);
                    Calculate_Waveform.Show();
                    Calculate_Waveform.Closed += (sender2, e2) => Calculate_Waveform.Dispatcher.InvokeShutdown();
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
                Insert_Log(Ex.Message, 1);
                Insert_Log("Math Waveform Window creation failed.", 1);
            }
        }

        //Creates DateTime Math Waveform Windows
        private void Create_Waveform_Window(string Window_Title, double Value, int Start_Sample, int End_Sample, string Graph_Title, string Y_Axis_Label, int Red, int Green, int Blue, double[] Measurement_Data, int Measurement_Count, double[] Measurement_DateTime)
        {
            try
            {
                Thread Waveform_Thread = new Thread(new ThreadStart(() =>
                {
                    DateTime_Math_Waveform Calculate_Waveform = new DateTime_Math_Waveform(Graph_Owner, Window_Title, Value, Start_Sample, End_Sample, Graph_Title, Y_Axis_Label, Red, Green, Blue, Measurement_Data, Measurement_Count, Measurement_DateTime);
                    Calculate_Waveform.Show();
                    Calculate_Waveform.Closed += (sender2, e2) => Calculate_Waveform.Dispatcher.InvokeShutdown();
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
                Insert_Log(Ex.Message, 1);
                Insert_Log("DateTime Math Waveform Window creation failed.", 1);
            }
        }

        private void Plot_Data_in_MathWaveform_DateTime_Button_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    int Measurement_Count_Copy = Measurement_Count;
                    double[] Measurement_Data_Copy = new double[Measurement_Count_Copy];
                    Array.Copy(Measurement_Data, Measurement_Data_Copy, Measurement_Count_Copy);

                    double[] Measurement_Data_DateTime = new double[Measurement_Count_Copy];

                    for (int i = 0; i < Measurement_Count_Copy; i++)
                    {
                        Measurement_Data_DateTime[i] = this.Measurement_DateTime[i].ToOADate();
                    }

                    Create_Waveform_Window("N Sample to Date Time Waveform: " + Measurement_Unit, 0, 0, Measurement_Count_Copy - 1, "", Measurement_Unit, 30, 144, 255, Measurement_Data_Copy, Measurement_Count_Copy, Measurement_Data_DateTime);
                    Measurement_Data_Copy = null;
                    Measurement_Data_DateTime = null;
                }
                catch (Exception Ex)
                {
                    Insert_Log(Ex.Message, 1);
                    Insert_Log("Cannot create Math Waveform (Date Time X-Axis), try again.", 1);
                }
            });
        }

        //Creates Histogram Windows
        private void Create_Histogram_Window(string Window_Title, int Start_Sample, int End_Sample, string Graph_Title, string X_Axis_Label, int Red, int Green, int Blue, double[] Measurement_Data, int Measurement_Count, DateTime[] Measurement_DateTime, double BinSize, double BarWidth, float BarBorder, bool Curve)
        {
            try
            {
                Thread Waveform_Thread = new Thread(new ThreadStart(() =>
                {
                    Histogram_Waveform Histogram_Waveform = new Histogram_Waveform(Graph_Owner, Window_Title, Start_Sample, End_Sample, Graph_Title, X_Axis_Label, Red, Green, Blue, Measurement_Data, Measurement_Count, Measurement_DateTime, BinSize, BarWidth, BarBorder, Curve);
                    Histogram_Waveform.Show();
                    Histogram_Waveform.Closed += (sender2, e2) => Histogram_Waveform.Dispatcher.InvokeShutdown();
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
                Insert_Log(Ex.Message, 1);
                Insert_Log("Histogram Window creation failed.", 1);
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            try
            {
                DataProcess.Stop();
                DataProcess.Dispose();
                GraphRender.Stop();
                Data_Queue.Dispose();
                Measurement_Data = null;
                Measurement_DateTime = null;
                Measurement_Plot = null;
            }
            catch (Exception)
            {

            }
        }
    }
}
