using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
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
using System.Windows.Threading;
using Microsoft.Win32;
using ScottPlot;

namespace HP_34401A
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class N_Sample_Graph_Window : Window
    {
        //Data is initially stored here, before it gets removed for processing.
        public BlockingCollection<string> Data_Queue = new BlockingCollection<string>();

        //Set Window Title, helps determine which instrument owns this Graph Window
        public string Measurement_Unit;
        public string Graph_Y_Axis_Label;

        public bool Graph_Reset = false;

        //Wavefrom Information Values
        int Total_Waveforms;
        int Total_Samples;
        int Total_Positive_Samples;
        int Total_Negative_Samples;
        double Waveform_1_Average;
        double Waveform_1_Maximum;
        double Waveform_1_Minimum;

        int N_Sample_Value;
        int Block_Array_Copy_Size;

        ScottPlot.Plottable.SignalPlot waveform_1_Plot;
        ScottPlot.Plottable.SignalPlot waveform_2_Plot;
        ScottPlot.Plottable.SignalPlot waveform_3_Plot;
        ScottPlot.Plottable.SignalPlot waveform_4_Plot;
        ScottPlot.Plottable.SignalPlot waveform_5_Plot;
        ScottPlot.Plottable.SignalPlot waveform_6_Plot;
        ScottPlot.Plottable.SignalPlot waveform_7_Plot;
        ScottPlot.Plottable.SignalPlot waveform_8_Plot;
        ScottPlot.Plottable.SignalPlot waveform_9_Plot;
        ScottPlot.Plottable.SignalPlot waveform_10_Plot;

        double[] waveform_1;
        double[] waveform_2;
        double[] waveform_3;
        double[] waveform_4;
        double[] waveform_5;
        double[] waveform_6;
        double[] waveform_7;
        double[] waveform_8;
        double[] waveform_9;
        double[] waveform_10;

        private System.Timers.Timer Add_to_Arrays;
        private DispatcherTimer Update_Screen;

        int Waveforms_On_Screen = 1;

        bool Enable_Auto_Axis = true;

        public N_Sample_Graph_Window(int N_Sample_Value, string Measurement_Unit, string Y_Axis_Label, string Window_Title)
        {
            InitializeComponent();

            this.N_Sample_Value = N_Sample_Value;
            Block_Array_Copy_Size = N_Sample_Value * sizeof(double);

            this.Measurement_Unit = Measurement_Unit;
            this.Graph_Y_Axis_Label = Y_Axis_Label;
            this.Title = Window_Title + " N Sample Graph";

            initialize_Array(N_Sample_Value);
            initialize_Graph_Waveforms();
            Update_Screen_Initialize();
            Add_to_Arrays_Initialize();
            Graph_RightClick_Menu();
        }

        public void initialize_Graph_Waveforms()
        {
            Graph.Plot.XLabel("N Samples");
            Graph.Plot.YLabel(Graph_Y_Axis_Label);
            Update_Measurement_Unit();
            waveform_10_Plot = Graph.Plot.AddSignal(waveform_10, color: System.Drawing.ColorTranslator.FromHtml("#ffead3"), label: "Waveform 10");
            waveform_9_Plot = Graph.Plot.AddSignal(waveform_9, color: System.Drawing.ColorTranslator.FromHtml("#ffc99d"), label: "Waveform 9");
            waveform_8_Plot = Graph.Plot.AddSignal(waveform_8, color: System.Drawing.ColorTranslator.FromHtml("#ffba85"), label: "Waveform 8");
            waveform_7_Plot = Graph.Plot.AddSignal(waveform_7, color: System.Drawing.ColorTranslator.FromHtml("#ffaa6f"), label: "Waveform 7");
            waveform_6_Plot = Graph.Plot.AddSignal(waveform_6, color: System.Drawing.ColorTranslator.FromHtml("#ff995a"), label: "Waveform 6");
            waveform_5_Plot = Graph.Plot.AddSignal(waveform_5, color: System.Drawing.ColorTranslator.FromHtml("#ff8746"), label: "Waveform 5");
            waveform_4_Plot = Graph.Plot.AddSignal(waveform_4, color: System.Drawing.ColorTranslator.FromHtml("#ff7333"), label: "Waveform 4");
            waveform_3_Plot = Graph.Plot.AddSignal(waveform_3, color: System.Drawing.ColorTranslator.FromHtml("#ff5d22"), label: "Waveform 3");
            waveform_2_Plot = Graph.Plot.AddSignal(waveform_2, color: System.Drawing.ColorTranslator.FromHtml("#ff4010"), label: "Waveform 2");
            waveform_1_Plot = Graph.Plot.AddSignal(waveform_1, color: System.Drawing.ColorTranslator.FromHtml("#ff0000"), label: "Waveform 1");
            Show_Waveforms(1);
        }

        private void Update_Measurement_Unit()
        {
            this.Dispatcher.Invoke(DispatcherPriority.ContextIdle, new ThreadStart(delegate
            {
                Sample_Average_Label_Unit.Content = Measurement_Unit;
                Max_Recorded_Sample_Label_Unit.Content = Measurement_Unit;
                Min_Recorded_Sample_Label_Unit.Content = Measurement_Unit;
                Graph.Plot.YLabel(Graph_Y_Axis_Label);
                Graph.Render();
            }));
        }

        private void Update_Screen_Initialize()
        {
            Update_Screen = new DispatcherTimer();
            Update_Screen.Interval = TimeSpan.FromSeconds(0);
            Update_Screen.Tick += Update_Screen_Event;
            Update_Screen.IsEnabled = false;
        }

        private void Update_Screen_Event(object sender, EventArgs e)
        {
            Total_Waveforms_Label.Content = Total_Waveforms.ToString();
            Total_Samples_Label.Content = Total_Samples.ToString();
            Total_Positive_Label.Content = Total_Positive_Samples.ToString();
            Total_Negative_Label.Content = Total_Negative_Samples.ToString();

            Sample_Average_Label.Content = Math.Round(Waveform_1_Average, 7).ToString();
            Max_Recorded_Sample_Label.Content = Math.Round(Waveform_1_Maximum, 7).ToString();
            Min_Recorded_Sample_Label.Content = Math.Round(Waveform_1_Minimum, 7).ToString();

            if (Enable_Auto_Axis == true) 
            {
                Graph.Plot.AxisAuto();
            }

            Graph.Render();
            Update_Screen.IsEnabled = false;
        }

        private void Add_to_Arrays_Initialize()
        {
            Add_to_Arrays = new System.Timers.Timer();
            Add_to_Arrays.Interval = 1;
            Add_to_Arrays.Elapsed += Add_to_Arrays_Event;
            Add_to_Arrays.AutoReset = false;
            Add_to_Arrays.Enabled = true;
        }

        private void Add_to_Arrays_Event(Object source, ElapsedEventArgs e)
        {
            if (Graph_Reset == true) 
            {
                clear_Arrays();
                Thread.Sleep(1500);
                while (Data_Queue.TryTake(out _)) { }
                Update_Measurement_Unit();
                Graph_Reset = false;
            }

            while (Data_Queue.Count() >= N_Sample_Value)
            {
                Array_Level_Down();
                for (int i = 0; i < N_Sample_Value; i++)
                {
                    string[] Data_Dequeue = Data_Queue.Take().Split(',');
                    if (Data_Dequeue[1].Contains("E+37"))
                    {

                    }
                    else
                    {
                        waveform_1[i] = double.Parse(Data_Dequeue[1]);
                        Total_Samples++;
                        if (waveform_1[i] >= 0)
                        {
                            Total_Positive_Samples++;
                        }
                        else 
                        {
                            Total_Negative_Samples++;
                        }
                    }
                }
                Waveform_1_Average = waveform_1.Average();
                Waveform_1_Maximum = waveform_1.Max();
                Waveform_1_Minimum = waveform_1.Min();
                Total_Waveforms++;
                Update_Screen.Start();
            }
            Add_to_Arrays.Enabled = true;
        }

        public void Array_Level_Down()
        {
            if (Waveforms_On_Screen == 10)
            {
                System.Buffer.BlockCopy(waveform_9, 0, waveform_10, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_8, 0, waveform_9, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_7, 0, waveform_8, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_6, 0, waveform_7, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_5, 0, waveform_6, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_4, 0, waveform_5, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_3, 0, waveform_4, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_2, 0, waveform_3, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_1, 0, waveform_2, 0, Block_Array_Copy_Size);
            }
            else if (Waveforms_On_Screen == 9) 
            {
                System.Buffer.BlockCopy(waveform_8, 0, waveform_9, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_7, 0, waveform_8, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_6, 0, waveform_7, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_5, 0, waveform_6, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_4, 0, waveform_5, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_3, 0, waveform_4, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_2, 0, waveform_3, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_1, 0, waveform_2, 0, Block_Array_Copy_Size);
            }
            else if (Waveforms_On_Screen == 8)
            {
                System.Buffer.BlockCopy(waveform_7, 0, waveform_8, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_6, 0, waveform_7, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_5, 0, waveform_6, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_4, 0, waveform_5, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_3, 0, waveform_4, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_2, 0, waveform_3, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_1, 0, waveform_2, 0, Block_Array_Copy_Size);
            }
            else if (Waveforms_On_Screen == 7)
            {
                System.Buffer.BlockCopy(waveform_6, 0, waveform_7, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_5, 0, waveform_6, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_4, 0, waveform_5, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_3, 0, waveform_4, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_2, 0, waveform_3, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_1, 0, waveform_2, 0, Block_Array_Copy_Size);
            }
            else if (Waveforms_On_Screen == 6)
            {
                System.Buffer.BlockCopy(waveform_5, 0, waveform_6, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_4, 0, waveform_5, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_3, 0, waveform_4, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_2, 0, waveform_3, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_1, 0, waveform_2, 0, Block_Array_Copy_Size);
            }
            else if (Waveforms_On_Screen == 5)
            {
                System.Buffer.BlockCopy(waveform_4, 0, waveform_5, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_3, 0, waveform_4, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_2, 0, waveform_3, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_1, 0, waveform_2, 0, Block_Array_Copy_Size);
            }
            else if (Waveforms_On_Screen == 4)
            {
                System.Buffer.BlockCopy(waveform_3, 0, waveform_4, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_2, 0, waveform_3, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_1, 0, waveform_2, 0, Block_Array_Copy_Size);
            }
            else if (Waveforms_On_Screen == 3)
            {
                System.Buffer.BlockCopy(waveform_2, 0, waveform_3, 0, Block_Array_Copy_Size);
                System.Buffer.BlockCopy(waveform_1, 0, waveform_2, 0, Block_Array_Copy_Size);
            }
            else if (Waveforms_On_Screen == 2)
            {
                System.Buffer.BlockCopy(waveform_1, 0, waveform_2, 0, Block_Array_Copy_Size);
            }
        }

        public void initialize_Array(int N_Samples)
        {
            waveform_1 = new double[N_Samples];
            waveform_2 = new double[N_Samples];
            waveform_3 = new double[N_Samples];
            waveform_4 = new double[N_Samples];
            waveform_5 = new double[N_Samples];
            waveform_6 = new double[N_Samples];
            waveform_7 = new double[N_Samples];
            waveform_8 = new double[N_Samples];
            waveform_9 = new double[N_Samples];
            waveform_10 = new double[N_Samples];
        }

        private void clear_Arrays() 
        {
            Array.Clear(waveform_1, 0, N_Sample_Value);
            Array.Clear(waveform_2, 0, N_Sample_Value);
            Array.Clear(waveform_3, 0, N_Sample_Value);
            Array.Clear(waveform_4, 0, N_Sample_Value);
            Array.Clear(waveform_5, 0, N_Sample_Value);
            Array.Clear(waveform_6, 0, N_Sample_Value);
            Array.Clear(waveform_7, 0, N_Sample_Value);
            Array.Clear(waveform_8, 0, N_Sample_Value);
            Array.Clear(waveform_9, 0, N_Sample_Value);
            Array.Clear(waveform_10, 0, N_Sample_Value);
        }

        //-----------------------------Menu Options--------------------------
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
            catch (Exception)
            {
                SystemSounds.Hand.Play();
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

        private void Save_Graph_Image_Click(object sender, RoutedEventArgs e)
        {
            Save_Graph_to_Image();
        }

        private void Exit_Graph_Window_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        //---------------------------------Axis Settings----------------------------
        private void X_Axis_Text_Button_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.Label(X_Axis_Set_Text.Text);
            Graph.Render();
            X_Axis_Set_Text.Text = string.Empty;
        }

        private void Y_Axis_Text_Button_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.YAxis.Label(Y_Axis_Set_Text.Text);
            Graph.Render();
            Y_Axis_Set_Text.Text = string.Empty;
        }

        private void X_Axis_Show_Click(object sender, RoutedEventArgs e)
        {
            if (X_Axis_Show.IsChecked == true)
            {
                Graph.Plot.XAxis.Ticks(true);
                Graph.Render();
            }
            else
            {
                Graph.Plot.XAxis.Ticks(false);
                Graph.Render();
            }
        }

        private void X_Axis_Tick_Rotation_0_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.TickLabelStyle(rotation: 0);
            Graph.Render();
            X_Axis_Tick_Rotation_0.IsChecked = true;
            X_Axis_Tick_Rotation_45.IsChecked = false;
        }

        private void X_Axis_Tick_Rotation_45_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.TickLabelStyle(rotation: 45);
            Graph.Render();
            X_Axis_Tick_Rotation_0.IsChecked = false;
            X_Axis_Tick_Rotation_45.IsChecked = true;
        }

        private void X_Axis_Minor_Grid_Click(object sender, RoutedEventArgs e)
        {
            if (X_Axis_Minor_Grid.IsChecked == true)
            {
                Graph.Plot.XAxis.MinorGrid(true);
                Graph.Render();
            }
            else
            {
                Graph.Plot.XAxis.MinorGrid(false);
                Graph.Render();
            }
        }

        private void X_Axis_Multiplier_Notation_Click(object sender, RoutedEventArgs e)
        {
            if (X_Axis_Multiplier_Notation.IsChecked == true)
            {
                Graph.Plot.XAxis.TickLabelNotation(multiplier: true);
                Graph.Render();
            }
            else
            {
                Graph.Plot.XAxis.TickLabelNotation(multiplier: false);
                Graph.Render();
            }
        }

        private void X_Axis_Tick_Ruler_Mode_Click(object sender, RoutedEventArgs e)
        {
            if (X_Axis_Tick_Ruler_Mode.IsChecked == true)
            {
                Graph.Plot.XAxis.RulerMode(true);
                Graph.Render();
            }
            else
            {
                Graph.Plot.XAxis.RulerMode(false);
                Graph.Render();
            }
        }

        private void Y_Axis_Show_Click(object sender, RoutedEventArgs e)
        {
            if (Y_Axis_Show.IsChecked == true)
            {
                Graph.Plot.YAxis.Ticks(true);
                Graph.Render();
            }
            else
            {
                Graph.Plot.YAxis.Ticks(false);
                Graph.Render();
            }
        }

        private void Y_Axis_Tick_Rotation_0_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.YAxis.TickLabelStyle(rotation: 0);
            Graph.Render();
            Y_Axis_Tick_Rotation_0.IsChecked = true;
            Y_Axis_Tick_Rotation_45.IsChecked = false;
        }

        private void Y_Axis_Tick_Rotation_45_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.YAxis.TickLabelStyle(rotation: 45);
            Graph.Render();
            Y_Axis_Tick_Rotation_0.IsChecked = false;
            Y_Axis_Tick_Rotation_45.IsChecked = true;
        }

        private void Y_Axis_Minor_Grid_Click(object sender, RoutedEventArgs e)
        {
            if (Y_Axis_Minor_Grid.IsChecked == true)
            {
                Graph.Plot.YAxis.MinorGrid(true);
                Graph.Render();
            }
            else
            {
                Graph.Plot.YAxis.MinorGrid(false);
                Graph.Render();
            }
        }

        private void Y_Axis_Multiplier_Notation_Click(object sender, RoutedEventArgs e)
        {
            if (Y_Axis_Multiplier_Notation.IsChecked == true)
            {
                Graph.Plot.YAxis.TickLabelNotation(multiplier: true);
                Graph.Render();
            }
            else
            {
                Graph.Plot.YAxis.TickLabelNotation(multiplier: false);
                Graph.Render();
            }
        }

        private void Y_Axis_Tick_Ruler_Mode_Click(object sender, RoutedEventArgs e)
        {
            if (Y_Axis_Tick_Ruler_Mode.IsChecked == true)
            {
                Graph.Plot.YAxis.RulerMode(true);
                Graph.Render();
            }
            else
            {
                Graph.Plot.YAxis.RulerMode(false);
                Graph.Render();
            }
        }

        //Font Size
        private void Font_Size_12_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.XAxis.TickLabelStyle(fontSize: 12);
            Graph.Plot.YAxis.TickLabelStyle(fontSize: 12);
            Graph.Render();
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
            }
            else
            {
                Graph.Plot.XAxis.Grid(false);
                Graph.Render();
            }
        }

        private void Graph_Horizontal_Grid_Click(object sender, RoutedEventArgs e)
        {
            if (Graph_Horizontal_Grid.IsChecked == true)
            {
                Graph.Plot.YAxis.Grid(true);
                Graph.Render();
            }
            else
            {
                Graph.Plot.YAxis.Grid(false);
                Graph.Render();
            }
        }

        private void Grid_Style_Default_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Grid(lineStyle: LineStyle.Solid);
            Graph.Render();
            Grid_Style_Default.IsChecked = true;
            Grid_Style_Dotted.IsChecked = false;
            Grid_Style_Dashed.IsChecked = false;
            Grid_Style_Dot_Dash.IsChecked = false;
        }

        private void Grid_Style_Dotted_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Grid(lineStyle: LineStyle.Dot);
            Graph.Render();
            Grid_Style_Default.IsChecked = false;
            Grid_Style_Dotted.IsChecked = true;
            Grid_Style_Dashed.IsChecked = false;
            Grid_Style_Dot_Dash.IsChecked = false;
        }

        private void Grid_Style_Dashed_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Grid(lineStyle: LineStyle.Dash);
            Graph.Render();
            Grid_Style_Default.IsChecked = false;
            Grid_Style_Dotted.IsChecked = false;
            Grid_Style_Dashed.IsChecked = true;
            Grid_Style_Dot_Dash.IsChecked = false;
        }

        private void Grid_Style_Dot_Dash_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.Grid(lineStyle: LineStyle.DashDot);
            Graph.Render();
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
            }
            else
            {
                Graph.Plot.Legend(false);
                Graph.Render();
            }
        }

        private void Legend_TopLeft_Click(object sender, RoutedEventArgs e)
        {
            Show_legend.IsChecked = true;
            Graph.Plot.Legend(location: Alignment.UpperLeft);
            Graph.Render();
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
            Legend_TopLeft.IsChecked = false;
            Legend_TopRight.IsChecked = false;
            Legend_BottomLeft.IsChecked = false;
            Legend_BottomRight.IsChecked = true;
        }

        //-------------------------GRaph Color Settings-----------------------------------
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

        private void Red_Palette_Click(object sender, RoutedEventArgs e)
        {
            Color_Palette_Selected(0);
            waveform_1_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ff0000");
            waveform_2_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ff4010");
            waveform_3_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ff5d22");
            waveform_4_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ff7333");
            waveform_5_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ff8746");
            waveform_6_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ff995a");
            waveform_7_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffaa6f");
            waveform_8_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffba85");
            waveform_9_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffc99d");
            waveform_10_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffead3");
        }

        private void Green_Palette_Click(object sender, RoutedEventArgs e)
        {
            Color_Palette_Selected(1);
            waveform_1_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#00ff00");
            waveform_2_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#42ff2d");
            waveform_3_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#5dff44");
            waveform_4_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#72ff56");
            waveform_5_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#83ff66");
            waveform_6_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#93ff75");
            waveform_7_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#a0ff83");
            waveform_8_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#adff91");
            waveform_9_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#b9ff9f");
            waveform_10_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#caffb4");
        }

        private void Blue_Palette_Click(object sender, RoutedEventArgs e)
        {
            Color_Palette_Selected(2);
            waveform_1_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#00c0ff");
            waveform_2_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#33c9fe");
            waveform_3_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#4ed1fd");
            waveform_4_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#66dafb");
            waveform_5_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#7ce2fb");
            waveform_6_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#90e9fa");
            waveform_7_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#a4f1fb");
            waveform_8_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#b8f8fc");
            waveform_9_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#cbffff");
            waveform_10_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ddffff");
        }

        private void Yellow_Palette_Click(object sender, RoutedEventArgs e)
        {
            Color_Palette_Selected(3);
            waveform_1_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffff00");
            waveform_2_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffff38");
            waveform_3_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffff53");
            waveform_4_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffff68");
            waveform_5_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffff7c");
            waveform_6_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffff8e");
            waveform_7_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffff9f");
            waveform_8_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffffb0");
            waveform_9_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffffc0");
            waveform_10_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffffd7");
        }

        private void Pink_Palette_Click(object sender, RoutedEventArgs e)
        {
            Color_Palette_Selected(4);
            waveform_1_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ff1493");
            waveform_2_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ff41a5");
            waveform_3_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ff5cb6");
            waveform_4_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ff72c6");
            waveform_5_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ff86d4");
            waveform_6_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ff99e1");
            waveform_7_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffaaed");
            waveform_8_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#febbf7");
            waveform_9_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffcbff");
            waveform_10_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffe5ff");
        }

        private void Violet_Palette_Click(object sender, RoutedEventArgs e)
        {
            Color_Palette_Selected(5);
            waveform_1_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#9400d3");
            waveform_2_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#a631d7");
            waveform_3_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#b64ddc");
            waveform_4_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#c566e1");
            waveform_5_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#d37ee6");
            waveform_6_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#e095ec");
            waveform_7_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ebacf2");
            waveform_8_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#f6c3f8");
            waveform_9_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffdaff");
            waveform_10_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#ffeaff");
        }

        private void Black_Palette_Click(object sender, RoutedEventArgs e)
        {
            Color_Palette_Selected(6);
            waveform_1_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#000000");
            waveform_2_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#1c1c1c");
            waveform_3_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#313131");
            waveform_4_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#484848");
            waveform_5_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#616161");
            waveform_6_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#7a7a7a");
            waveform_7_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#959595");
            waveform_8_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#b0b0b0");
            waveform_9_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#cccccc");
            waveform_10_Plot.Color = System.Drawing.ColorTranslator.FromHtml("#dfdfdf");
        }

        private void Color_Palette_Selected(int Select)
        {
            if (Select == 0)
            {
                Red_Palette.IsChecked = true;
            }
            else
            {
                Red_Palette.IsChecked = false;
            }
            if (Select == 1)
            {
                Green_Palette.IsChecked = true;
            }
            else
            {
                Green_Palette.IsChecked = false;
            }
            if (Select == 2)
            {
                Blue_Palette.IsChecked = true;
            }
            else
            {
                Blue_Palette.IsChecked = false;
            }
            if (Select == 3)
            {
                Yellow_Palette.IsChecked = true;
            }
            else
            {
                Yellow_Palette.IsChecked = false;
            }
            if (Select == 4)
            {
                Pink_Palette.IsChecked = true;
            }
            else
            {
                Pink_Palette.IsChecked = false;
            }
            if (Select == 5)
            {
                Violet_Palette.IsChecked = true;
            }
            else
            {
                Violet_Palette.IsChecked = false;
            }
            if (Select == 6)
            {
                Black_Palette.IsChecked = true;
            }
            else
            {
                Black_Palette.IsChecked = false;
            }
        }

        //-------------------------------------------------------------------------

        private void Force_Auto_Axis_Click(object sender, RoutedEventArgs e)
        {
            Graph.Plot.AxisAuto();
        }

        private void Auto_Axis_Enable_Click(object sender, RoutedEventArgs e)
        {
            if (Auto_Axis_Enable.IsChecked == true)
            {
                Enable_Auto_Axis = true;
            }
            else 
            {
                Enable_Auto_Axis = false;
            }
        }

        private void Waveforms_1_Enable_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Waveforms_On_Screen, 1);
            Waveforms_Selector(1);
            Show_Waveforms(1);
        }

        private void Waveforms_2_Enable_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Waveforms_On_Screen, 2);
            Waveforms_Selector(2);
            Show_Waveforms(2);
        }

        private void Waveforms_3_Enable_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Waveforms_On_Screen, 3);
            Waveforms_Selector(3);
            Show_Waveforms(3);
        }

        private void Waveforms_4_Enable_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Waveforms_On_Screen, 4);
            Waveforms_Selector(4);
            Show_Waveforms(4);
        }

        private void Waveforms_5_Enable_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Waveforms_On_Screen, 5);
            Waveforms_Selector(5);
            Show_Waveforms(5);
        }

        private void Waveforms_6_Enable_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Waveforms_On_Screen, 6);
            Waveforms_Selector(6);
            Show_Waveforms(6);
        }

        private void Waveforms_7_Enable_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Waveforms_On_Screen, 7);
            Waveforms_Selector(7);
            Show_Waveforms(7);
        }

        private void Waveforms_8_Enable_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Waveforms_On_Screen, 8);
            Waveforms_Selector(8);
            Show_Waveforms(8);
        }

        private void Waveforms_9_Enable_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Waveforms_On_Screen, 9);
            Waveforms_Selector(9);
            Show_Waveforms(9);
        }

        private void Waveforms_10_Enable_Click(object sender, RoutedEventArgs e)
        {
            Interlocked.Exchange(ref Waveforms_On_Screen, 10);
            Waveforms_Selector(10);
            Show_Waveforms(10);
        }

        private void Waveforms_Selector(int Select) 
        {
            if (Select == 1)
            {
                Waveforms_1_Enable.IsChecked = true;
            } 
            else 
            {
                Waveforms_1_Enable.IsChecked = false;
            }
            if (Select == 2)
            {
                Waveforms_2_Enable.IsChecked = true;
            }
            else
            {
                Waveforms_2_Enable.IsChecked = false;
            }
            if (Select == 3)
            {
                Waveforms_3_Enable.IsChecked = true;
            }
            else
            {
                Waveforms_3_Enable.IsChecked = false;
            }
            if (Select == 4)
            {
                Waveforms_4_Enable.IsChecked = true;
            }
            else
            {
                Waveforms_4_Enable.IsChecked = false;
            }
            if (Select == 5)
            {
                Waveforms_5_Enable.IsChecked = true;
            }
            else
            {
                Waveforms_5_Enable.IsChecked = false;
            }
            if (Select == 6)
            {
                Waveforms_6_Enable.IsChecked = true;
            }
            else
            {
                Waveforms_6_Enable.IsChecked = false;
            }
            if (Select == 7)
            {
                Waveforms_7_Enable.IsChecked = true;
            }
            else
            {
                Waveforms_7_Enable.IsChecked = false;
            }
            if (Select == 8)
            {
                Waveforms_8_Enable.IsChecked = true;
            }
            else
            {
                Waveforms_8_Enable.IsChecked = false;
            }
            if (Select == 9)
            {
                Waveforms_9_Enable.IsChecked = true;
            }
            else
            {
                Waveforms_9_Enable.IsChecked = false;
            }
            if (Select == 10)
            {
                Waveforms_10_Enable.IsChecked = true;
            }
            else
            {
                Waveforms_10_Enable.IsChecked = false;
            }
        }

        private void Show_Waveforms(int Select) 
        {
            if (Select == 1)
            {
                waveform_10_Plot.IsVisible = false;
                waveform_9_Plot.IsVisible = false;
                waveform_8_Plot.IsVisible = false;
                waveform_7_Plot.IsVisible = false;
                waveform_6_Plot.IsVisible = false;
                waveform_5_Plot.IsVisible = false;
                waveform_4_Plot.IsVisible = false;
                waveform_3_Plot.IsVisible = false;
                waveform_2_Plot.IsVisible = false;
            }
            else if (Select == 2)
            {
                waveform_10_Plot.IsVisible = false;
                waveform_9_Plot.IsVisible = false;
                waveform_8_Plot.IsVisible = false;
                waveform_7_Plot.IsVisible = false;
                waveform_6_Plot.IsVisible = false;
                waveform_5_Plot.IsVisible = false;
                waveform_4_Plot.IsVisible = false;
                waveform_3_Plot.IsVisible = false;
                waveform_2_Plot.IsVisible = true;
            }
            else if (Select == 3)
            {
                waveform_10_Plot.IsVisible = false;
                waveform_9_Plot.IsVisible = false;
                waveform_8_Plot.IsVisible = false;
                waveform_7_Plot.IsVisible = false;
                waveform_6_Plot.IsVisible = false;
                waveform_5_Plot.IsVisible = false;
                waveform_4_Plot.IsVisible = false;
                waveform_3_Plot.IsVisible = true;
                waveform_2_Plot.IsVisible = true;
            }
            else if (Select == 4)
            {
                waveform_10_Plot.IsVisible = false;
                waveform_9_Plot.IsVisible = false;
                waveform_8_Plot.IsVisible = false;
                waveform_7_Plot.IsVisible = false;
                waveform_6_Plot.IsVisible = false;
                waveform_5_Plot.IsVisible = false;
                waveform_4_Plot.IsVisible = true;
                waveform_3_Plot.IsVisible = true;
                waveform_2_Plot.IsVisible = true;
            }
            else if (Select == 5)
            {
                waveform_10_Plot.IsVisible = false;
                waveform_9_Plot.IsVisible = false;
                waveform_8_Plot.IsVisible = false;
                waveform_7_Plot.IsVisible = false;
                waveform_6_Plot.IsVisible = false;
                waveform_5_Plot.IsVisible = true;
                waveform_4_Plot.IsVisible = true;
                waveform_3_Plot.IsVisible = true;
                waveform_2_Plot.IsVisible = true;
            }
            else if (Select == 6)
            {
                waveform_10_Plot.IsVisible = false;
                waveform_9_Plot.IsVisible = false;
                waveform_8_Plot.IsVisible = false;
                waveform_7_Plot.IsVisible = false;
                waveform_6_Plot.IsVisible = true;
                waveform_5_Plot.IsVisible = true;
                waveform_4_Plot.IsVisible = true;
                waveform_3_Plot.IsVisible = true;
                waveform_2_Plot.IsVisible = true;
            }
            else if (Select == 7)
            {
                waveform_10_Plot.IsVisible = false;
                waveform_9_Plot.IsVisible = false;
                waveform_8_Plot.IsVisible = false;
                waveform_7_Plot.IsVisible = true;
                waveform_6_Plot.IsVisible = true;
                waveform_5_Plot.IsVisible = true;
                waveform_4_Plot.IsVisible = true;
                waveform_3_Plot.IsVisible = true;
                waveform_2_Plot.IsVisible = true;
            }
            else if (Select == 8)
            {
                waveform_10_Plot.IsVisible = false;
                waveform_9_Plot.IsVisible = false;
                waveform_8_Plot.IsVisible = true;
                waveform_7_Plot.IsVisible = true;
                waveform_6_Plot.IsVisible = true;
                waveform_5_Plot.IsVisible = true;
                waveform_4_Plot.IsVisible = true;
                waveform_3_Plot.IsVisible = true;
                waveform_2_Plot.IsVisible = true;
            }
            else if (Select == 9)
            {
                waveform_10_Plot.IsVisible = false;
                waveform_9_Plot.IsVisible = true;
                waveform_8_Plot.IsVisible = true;
                waveform_7_Plot.IsVisible = true;
                waveform_6_Plot.IsVisible = true;
                waveform_5_Plot.IsVisible = true;
                waveform_4_Plot.IsVisible = true;
                waveform_3_Plot.IsVisible = true;
                waveform_2_Plot.IsVisible = true;
            }
            else if (Select == 10)
            {
                waveform_10_Plot.IsVisible = true;
                waveform_9_Plot.IsVisible = true;
                waveform_8_Plot.IsVisible = true;
                waveform_7_Plot.IsVisible = true;
                waveform_6_Plot.IsVisible = true;
                waveform_5_Plot.IsVisible = true;
                waveform_4_Plot.IsVisible = true;
                waveform_3_Plot.IsVisible = true;
                waveform_2_Plot.IsVisible = true;
            }
        }

        private void Reset_Graph_Click(object sender, RoutedEventArgs e)
        {
            Graph_Reset = true;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            try
            {
                Add_to_Arrays.Stop();
                Add_to_Arrays.Dispose();

                Update_Screen.Stop();
                Update_Screen = null;

                waveform_1_Plot = null;
                waveform_2_Plot = null;
                waveform_3_Plot = null;
                waveform_4_Plot = null;
                waveform_5_Plot = null;
                waveform_6_Plot = null;
                waveform_7_Plot = null;
                waveform_8_Plot = null;
                waveform_9_Plot = null;
                waveform_10_Plot = null;

                waveform_1 = null;
                waveform_2 = null;
                waveform_3 = null;
                waveform_4 = null;
                waveform_5 = null;
                waveform_6 = null;
                waveform_7 = null;
                waveform_8 = null;
                waveform_9 = null;
                waveform_10 = null;

                Data_Queue.Dispose();
            }
            catch (Exception)
            {

            }
        }
    }
}
